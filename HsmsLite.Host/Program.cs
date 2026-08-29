using HsmsLite.Gem;
using HsmsLite.Protocol;
using Serilog;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace HsmsLite.Host
{
    internal class Program
    {
        private static readonly string _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log");
        private static ILogger _log = null!;

        public static async Task Main(string[] args)
        {
            // 로거 초기화
            ConfigureLogging();
            _log = Log.ForContext<Program>(); // SourceContext를 채우려면 static Log.* 대신 컨텍스트 바인딩된 로거를 써야 함

            try
            {
                _log.Information("HOST");

                var targetHost = args.Length > 0 ? args[0] : "127.0.0.1";
                var port = ParsePort(args, defaultPort: 5000);

                await RunHostClientAsync(targetHost, port);
            }
            catch (Exception ex)
            {
                _log.Fatal(ex, "Host application terminated unexpectedly.");
            }
            finally
            {
                Log.CloseAndFlush(); // Serilog 버퍼 비우기
            }
        }

        // Host 클라이언트 전체 실행 및 통신 시퀀스 제어
        private static async Task RunHostClientAsync(string targetHost, int port)
        {
            using var client = new TcpClient();
            _log.Information($"Connecting to {targetHost}:{port}");
            await client.ConnectAsync(targetHost, port);

            var stream = client.GetStream();
            _log.Information("Tcp connected.");

            var sm = new HsmsStateMachine();
            var sysBytes = new SystemBytesGenerator();

            sm.StateChanged += (from, to) => _log.Information($"State: {from} -> {to}");
            sm.OnTcpConnected();

            using var cts = new CancellationTokenSource();
            var responder = new HsmsRequestResponder(stream);

            // 백그라운드 메시지 수신 루프 시작
            var receiveLoopTask = Task.Run(() => ReceiveLoopAsync(responder, sm, cts.Token));

            try
            {
                // Select 요청
                var selectRsp = await SendAndTraceAsync(responder, HsmsMessage.Control(HsmsSType.SelectReq, sysBytes.Next()));
                var status = (HsmsSelectStatus)selectRsp.Header.Byte3;
                _log.Information($"Select.rsp status = {status}");

                if(status != HsmsSelectStatus.Ok)
                    throw new HsmsProtocolException($"Equipment refused Select: {status}");

                sm.OnSelected();

                // GEM Establish Communications
                var commRsp = await SendAndTraceAsync(responder, S1Messages.BuildS1F13(1, sysBytes.Next()));
                var (commAccepted, _, _) = S1Messages.ParseS1F14(commRsp);
                if (!commAccepted)
                    throw new HsmsProtocolException("Equipment denied Establish Communications (S1F14 COMMACK=denied).");
                _log.Information("GEM communicating.");

                // 장비 식별 조회
                var idRsp = await SendAndTraceAsync(responder, S1Messages.BuildS1F1(1, sysBytes.Next()));
                var (mdln, softRev) = S1Messages.ParseS1F2(idRsp);
                _log.Information($"Equipment identity: MDLN={mdln} SOFTREV={softRev}");

                // Equipment 상태 조회
                var svids = new uint[] { 1, 2, 3 };
                var statusRsp = await SendAndTraceAsync(responder, S1Messages.BuildS1F3(1, sysBytes.Next(), svids));
                var values = S1Messages.ParseS1F4(statusRsp);
                _log.Information($"Equipment status: {string.Join(", ", svids.Zip(values, (id, v) => $"SVID{id}={v}"))}");

                // Host Command 전송
                var cmdRsp = await SendAndTraceAsync(responder, S2Messages.BuildS2F41(1, sysBytes.Next(), "START"));
                _log.Information($"S2F42 HCACK accepted={S2Messages.ParseS2F42(cmdRsp)}");

                // Unsolicited Event Report(S6F11) 수신 대기
                await Task.Delay(TimeSpan.FromSeconds(4.5));

                // Linktest Keep-alive
                var linktestRsp = await SendAndTraceAsync(responder, HsmsMessage.Control(HsmsSType.LinktestReq, sysBytes.Next()));
                _log.Information("Linktest.rsp received - link is healthy.");

                // 추가 Event Report 수신 대기 후 세션 종료
                await Task.Delay(TimeSpan.FromSeconds(4.5));

                var separateReq = HsmsMessage.Control(HsmsSType.SeparateReq, sysBytes.Next());
                _log.Information(HsmsCommTrace.Format("SEND", separateReq));
                await responder.SendAsync(separateReq);
                _log.Information("Sent Separate.req - ending session.");
                sm.OnSeparatedOrDeselected();
            }
            catch (HsmsProtocolException ex)
            {
                _log.Error($"Protocol error: {ex.Message}");
            }
            catch (TimeoutException ex)
            {
                _log.Error($"Timeout error: {ex.Message}");
            }
            finally
            {
                cts.Cancel();
                client.Close();
                sm.OnTcpDisconnected();

                try { await receiveLoopTask; } catch { /* 수신 루프 종료 예외 무시 */ }
                _log.Information("Host exiting.");
            }
        }

        // 요청 전송 + 트랜잭션 시작/종료 마커 + SEND 트레이스를 한 번에 처리
        private static async Task<HsmsMessage> SendAndTraceAsync(HsmsRequestResponder responder, HsmsMessage request,
            TimeSpan? timeout = null, CancellationToken ct = default)
        {
            _log.Information(HsmsCommTrace.TransactionOpen(request));
            _log.Information(HsmsCommTrace.Format("SEND", request));

            var sw = Stopwatch.StartNew();
            try
            {
                var rsp = await responder.SendAndWaitAsync(request, timeout, ct).ConfigureAwait(false);
                _log.Information(HsmsCommTrace.TransactionClose(request, sw.ElapsedMilliseconds));
                return rsp;
            }
            catch
            {
                _log.Information(HsmsCommTrace.TransactionClose(request, sw.ElapsedMilliseconds) + " FAILED");
                throw;
            }
        }

        //백그라운드 수신 루프(요청 응답 매칭 및 Unsolicited 이벤트 처리)
        private static async Task ReceiveLoopAsync(HsmsRequestResponder responder, HsmsStateMachine sm, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var msg = await responder.ReadAsync(ct);
                    if (msg is null)
                    {
                        _log.Information("Equipment closed the TCP connection.");
                        return;
                    }

                    _log.Information(HsmsCommTrace.Format("RECV", msg));
                    sm.AssertValid(msg.Header.SType);

                    if (responder.TryResolve(msg))
                        continue;

                    // Await하는 요청이 없는 경우 -> Equipment의 자발적(Unsolicited) 메시지
                    if (msg.Header.SType != HsmsSType.DataMessage)
                        continue;

                    var function = (byte)(msg.Header.Byte3 & 0x7F);
                    if (msg.Header.Byte2 == 6 && function == 11)
                    {
                        var (dataId, ceid, values) = S6Messages.ParseS6F11(msg);
                        _log.Information($"  -> unsolicited S6F11 event: DATAID={dataId} CEID={ceid} values=[{string.Join(",", values)}]");
                        var s6f12 = S6Messages.BuildS6F12(msg.Header.SessionId, msg.Header.SystemBytes, accepted: true);
                        await responder.SendAsync(s6f12, ct);
                        _log.Information(HsmsCommTrace.Format("SEND", s6f12));
                    }
                    else
                    {
                        _log.Information($"  -> unsolicited S{msg.Header.Byte2}F{function}, no handler.");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (HsmsProtocolException ex)
            {
                _log.Error($"Protocol violation: {ex.Message}");
            }
            catch (IOException)
            {
                _log.Information("Connection lost while receiving.");
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

        // 포트 파싱 분리 (사용법: Host.exe [targetHost] [port])
        private static int ParsePort(string[] args, int defaultPort)
        {
            if (args.Length > 1 && int.TryParse(args[1], out var port))
            {
                return port;
            }
            return defaultPort;
        }
    }
}
