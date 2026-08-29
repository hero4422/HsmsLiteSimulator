using HsmsLite.Gem;
using HsmsLite.Protocol;
using Serilog;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace HsmsLite.Equipment
{
    public class Program
    {
        private const string Mdln = "HSMSLITE-EQP";
        private const string SoftRev = "1.0.0";
        private static readonly Dictionary<uint, uint> _svidTable = new() { [1] = 101, [2] = 202, [3] = 303 };
        private static readonly HashSet<string> _knownCommands = new(StringComparer.Ordinal) { "START" };

        private static readonly string _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log");
        private static ILogger _log = null!;

        public static async Task Main(string[] args)
        {
            // 로거 초기화
            ConfigureLogging();
            _log = Log.ForContext<Program>(); // SourceContext를 채우려면 static Log.* 대신 컨텍스트 바인딩된 로거를 써야 함

            try
            {
                _log.Information("EQUIPMENT");

                int port = ParsePort(args, defaultPort: 5000);

                // 취소 토큰 설정
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

                // 서버 수신기 실행
                await RunServerAsync(port, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _log.Information("Server shutdown requested.");
            }
            catch (Exception ex)
            {
                _log.Fatal(ex, "Application terminated unexpectedly.");
            }
            finally
            {
                Log.CloseAndFlush(); // Serilog 버퍼 비우기
            }
        }

        private static async Task HandleSessionAsync(TcpClient client, CancellationToken outerCt)
        {
            using var _ = client;
            var stream = client.GetStream();
            var sm = new HsmsStateMachine();
            var sysBytes = new SystemBytesGenerator();
            sm.StateChanged += (from, to) => _log.Information($"State: {from} -> {to}");
            sm.OnTcpConnected();

            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
            var ct = sessionCts.Token;
            var responder = new HsmsRequestResponder(stream);

            // Selected 상태가 되면 4초마다 S6F11 이벤트 리포트를 하나씩 보내고 S6F12 ack을 기다린다.
            // Host가 Separate 하거나 연결이 끊기면 같이 멈춘다.
            var eventTask = Task.Run(async () =>
            {
                var eventNo = 0;
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(4), ct);
                        if (sm.State != HsmsConnectionState.Selected)
                            continue;

                        eventNo++;
                        var reportValues = new Secs2Item[]
                        {
                            new Secs2Ascii("RUN"),
                            new Secs2Ascii($"LOT-{1000 + eventNo}"),
                            new Secs2U4((uint)(eventNo * 25)),
                        };
                        var req = S6Messages.BuildS6F11(sessionId: 1, sysBytes.Next(), dataId: (uint)eventNo, ceid: 1001, reportValues);
                        _log.Information(HsmsCommTrace.TransactionOpen(req));
                        _log.Information(HsmsCommTrace.Format("SEND", req));
                        var sw = Stopwatch.StartNew();
                        var rsp = await responder.SendAndWaitAsync(req, ct: ct);
                        _log.Information(HsmsCommTrace.TransactionClose(req, sw.ElapsedMilliseconds));
                        _log.Information($"S6F12 ack accepted={S6Messages.ParseS6F12(rsp)}");
                    }
                }
                catch (OperationCanceledException) { }
                catch (TimeoutException ex)
                {
                    _log.Error($"S6F11 timed out waiting for ack: {ex.Message}");
                }
            }, ct);

            try
            {
                while (true)
                {
                    var msg = await responder.ReadAsync(ct);
                    if (msg is null)
                    {
                        _log.Information("Host closed the TCP connection.");
                        break;
                    }

                    _log.Information(HsmsCommTrace.Format("RECV", msg));
                    sm.AssertValid(msg.Header.SType);

                    if (responder.TryResolve(msg))
                        continue;

                    switch (msg.Header.SType)
                    {
                        case HsmsSType.SelectReq:
                            {
                                sm.OnSelected();
                                var rsp = HsmsMessage.Control(HsmsSType.SelectRsp, msg.Header.SystemBytes, (byte)HsmsSelectStatus.Ok);
                                await responder.SendAsync(rsp, ct);
                                _log.Information(HsmsCommTrace.Format("SEND", rsp));
                                break;
                            }

                        case HsmsSType.LinktestReq:
                            {
                                var rsp = HsmsMessage.Control(HsmsSType.LinktestRsp, msg.Header.SystemBytes);
                                await responder.SendAsync(rsp, ct);
                                _log.Information(HsmsCommTrace.Format("SEND", rsp));
                                break;
                            }

                        case HsmsSType.DataMessage:
                            {
                                var function = (byte)(msg.Header.Byte3 & 0x7F);
                                HsmsMessage? rsp = (msg.Header.Byte2, function) switch
                                {
                                    (1, 13) => S1Messages.BuildS1F14(msg.Header.SessionId, msg.Header.SystemBytes, true, Mdln, SoftRev),
                                    (1, 1) => S1Messages.BuildS1F2(msg.Header.SessionId, msg.Header.SystemBytes, Mdln, SoftRev),
                                    (1, 3) => S1Messages.BuildS1F4(msg.Header.SessionId, msg.Header.SystemBytes,
                                        S1Messages.ParseS1F3(msg).Select(id => _svidTable.GetValueOrDefault(id)).ToArray()),
                                    (2, 41) => S2Messages.BuildS2F42(msg.Header.SessionId, msg.Header.SystemBytes,
                                        _knownCommands.Contains(S2Messages.ParseS2F41(msg))),
                                    _ => null,
                                };

                                if (rsp is null)
                                {
                                    _log.Information($"  (unhandled S{msg.Header.Byte2}F{function}, ignoring)");
                                    break;
                                }

                                await responder.SendAsync(rsp, ct);
                                _log.Information(HsmsCommTrace.Format("SEND", rsp));
                                break;
                            }

                        case HsmsSType.SeparateReq:
                            _log.Information("Separate.req received - closing session.");
                            sm.OnSeparatedOrDeselected();
                            sm.OnTcpDisconnected();
                            return;

                        default:
                            _log.Information($"  (unhandled SType {msg.Header.SType}, ignoring)");
                            break;
                    }
                }
            }
            catch (HsmsProtocolException ex)
            {
                _log.Error($"Protocol violation: {ex.Message}");
            }
            catch (OperationCanceledException) { }
            catch (IOException ex)
            {
                _log.Error($"Connection lost: {ex.Message}");
            }
            finally
            {
                sessionCts.Cancel();
                try { await eventTask; } catch { /* 위에서 이미 로그 찍음 */ }
                sm.OnTcpDisconnected();
                _log.Information("Session ended.\n");
            }
        }

        // 로깅 설정
        private static void ConfigureLogging()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug() // 로그 레벨 설정
                .WriteTo.Console()    // 콘솔창에 출력
                .WriteTo.File(        // 파일에 출력
                    path: Path.Combine(_logDirectory, "log-.txt"),
                    rollingInterval: RollingInterval.Day, // 매일 새로운 파일 생성
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
                )
                .CreateLogger();
        }

        // 포트 파싱
        private static int ParsePort(string[] args, int defaultPort)
        {
            if (args.Length > 0 && int.TryParse(args[0], out var port))
            {
                return port;
            }
            return defaultPort;
        }

        // 서버 수신 루프
        private static async Task RunServerAsync(int port, CancellationToken ct)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            _log.Information($"Listening on 127.0.0.1:{port}. Waiting for Host to connect...");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(ct);
                    _log.Information($"TCP connected from {client.Client.RemoteEndPoint}.");

                    await HandleSessionAsync(client, ct);
                }
            }
            finally
            {
                listener.Stop();
                _log.Information("Server listener stopped.");
            }
        }
    }
}
