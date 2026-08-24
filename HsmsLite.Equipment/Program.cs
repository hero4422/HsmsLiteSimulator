using HsmsLite.Protocol;
using Serilog;
using Serilog.Core;
using System.Net;
using System.Net.Sockets;

namespace HsmsLite.Equipment
{
    public class Program
    {
        private static readonly string _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log");
        public static async Task Main(string[] args)
        {
            // 로거 초기화
            ConfigureLogging();

            try
            {
                Log.Information("EQUIPMENT");

                int port = ParsePort(args, defaultPort: 5000);

                // 취소 토큰 설정
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

                // 서버 수신기 실행
                await RunServerAsync(port, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Log.Information("Server shutdown requested.");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly.");
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
            sm.StateChanged += (from, to) => Log.Information($"State: {from} -> {to}");
            sm.OnTcpConnected();

            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
            var ct = sessionCts.Token;

            // Background task: once Selected, push a simulated equipment event report every 4 seconds
            // until the Host separates or the connection drops.
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
                        var payload = $"EventReport#{eventNo};EquipmentState=RUN;LotId=LOT-{1000 + eventNo};UnitsProcessed={eventNo * 25}";
                        var msg = HsmsMessage.DataText(sessionId: 1, stream: 6, function: 11, sysBytes.Next(), payload);
                        await HsmsFraming.WriteAsync(stream, msg, ct);
                        Log.Information($"SEND {msg}");
                    }
                }
                catch (OperationCanceledException) { }
            }, ct);

            try
            {
                while (true)
                {
                    var msg = await HsmsFraming.ReadAsync(stream, ct);
                    if (msg is null)
                    {
                        Log.Information("Host closed the TCP connection.");
                        break;
                    }

                    Log.Information($"RECV {msg}");
                    sm.AssertValid(msg.Header.SType);

                    switch (msg.Header.SType)
                    {
                        case HsmsSType.SelectReq:
                            {
                                sm.OnSelected();
                                var rsp = HsmsMessage.Control(HsmsSType.SelectRsp, msg.Header.SystemBytes, (byte)HsmsSelectStatus.Ok);
                                await HsmsFraming.WriteAsync(stream, rsp, ct);
                                Log.Information($"SEND {rsp}");
                                break;
                            }

                        case HsmsSType.LinktestReq:
                            {
                                var rsp = HsmsMessage.Control(HsmsSType.LinktestRsp, msg.Header.SystemBytes);
                                await HsmsFraming.WriteAsync(stream, rsp, ct);
                                Log.Information($"SEND {rsp}");
                                break;
                            }

                        case HsmsSType.DataMessage:
                            {
                                Log.Information($"  -> equipment received data: \"{msg.BodyAsText()}\"");
                                if (msg.BodyAsText().StartsWith("StatusRequest", StringComparison.Ordinal))
                                {
                                    var reply = HsmsMessage.DataText(msg.Header.SessionId, msg.Header.Byte2, 12,
                                        msg.Header.SystemBytes, "EquipmentState=RUN;RecipeId=RCP-07;Alarm=None");
                                    await HsmsFraming.WriteAsync(stream, reply, ct);
                                    Log.Information($"SEND {reply}");
                                }
                                break;
                            }

                        case HsmsSType.SeparateReq:
                            Log.Information("Separate.req received - closing session.");
                            sm.OnSeparatedOrDeselected();
                            sm.OnTcpDisconnected();
                            return;

                        default:
                            Log.Information($"  (unhandled SType {msg.Header.SType}, ignoring)");
                            break;
                    }
                }
            }
            catch (HsmsProtocolException ex)
            {
                Log.Error($"Protocol violation: {ex.Message}");
            }
            catch (OperationCanceledException) { }
            catch (IOException ex)
            {
                Log.Error($"Connection lost: {ex.Message}");
            }
            finally
            {
                sessionCts.Cancel();
                try { await eventTask; } catch { /* already logged */ }
                sm.OnTcpDisconnected();
                Log.Information("Session ended.\n");
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

        // 포트 파싱 분리
        private static int ParsePort(string[] args, int defaultPort)
        {
            if (args.Length > 0 && int.TryParse(args[0], out var port))
            {
                return port;
            }
            return defaultPort;
        }

        // 서버 수신 루프 분리
        private static async Task RunServerAsync(int port, CancellationToken ct)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            Log.Information($"Listening on 127.0.0.1:{port}. Waiting for Host to connect...");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(ct);
                    Log.Information($"TCP connected from {client.Client.RemoteEndPoint}.");

                    await HandleSessionAsync(client, ct);
                }
            }
            finally
            {
                listener.Stop();
                Log.Information("Server listener stopped.");
            }
        }
    }
}
