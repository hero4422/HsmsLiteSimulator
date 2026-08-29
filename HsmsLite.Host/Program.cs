using HsmsLite.Protocol;
using Serilog;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace HsmsLite.Host
{
    internal class Program
    {
        private static readonly string _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log");

        // 요청-응답 매칭용 딕셔너리 및 소켓 쓰기 동기화 락
        private static readonly ConcurrentDictionary<uint, TaskCompletionSource<HsmsMessage>> _pendingRequests
            = new ConcurrentDictionary<uint, TaskCompletionSource<HsmsMessage>>();
        private static readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        public static async Task Main(string[] args)
        {
            // 로거 초기화
            ConfigureLogging();

            try
            {
                Log.Information("HOST");

                var targetHost = args.Length > 0 ? args[0] : "127.0.0.1";
                var port = ParsePort(args, defaultPort: 5000);

                await RunHostClientAsync(targetHost, port);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host application terminated unexpectedly.");
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
            Log.Information($"Connecting to {targetHost}:{port}");
            await client.ConnectAsync(targetHost, port);

            var stream = client.GetStream();
            Log.Information("Tcp connected.");

            var sm = new HsmsStateMachine();
            var sysBytes = new SystemBytesGenerator();

            sm.StateChanged += (from, to) => Log.Information($"State: {from} -> {to}");
            sm.OnTcpConnected();

            using var cts = new CancellationTokenSource();

            // 백그라운드 메시지 수신 루프 시작
            var receiveLoopTask = Task.Run(() => ReceiveLoopAsync(stream, sm, cts.Token));

            try
            {
                // Select 요청
                var selectRsp = await SendAndWaitAsync(stream, HsmsMessage.Control(HsmsSType.SelectReq, sysBytes.Next()));
                var status = (HsmsSelectStatus)selectRsp.Header.Byte3;
                Log.Information($"Select.rsp status = {status}");

                if(status != HsmsSelectStatus.Ok)
                    throw new HsmsProtocolException($"Equipment refused Select: {status}");

                sm.OnSelected();

                // Equipment 상태 조회
                var statusRsp = await SendAndWaitAsync(stream, HsmsMessage.DataText(1, 1, 1, sysBytes.Next(), "StatusRequest"));
                Log.Information($"Equipment status: \"{statusRsp.BodyAsText()}\"");

                // Unsolicited Event Report 수신 대기
                await Task.Delay(TimeSpan.FromSeconds(4.5));

                // Linktest Keep-alive
                var linktestRsp = await SendAndWaitAsync(stream, HsmsMessage.Control(HsmsSType.LinktestReq, sysBytes.Next()));
                Log.Information("Linktest.rsp received - link is healthy.");

                // 추가 Event Report 수신 대기 후 세션 종료
                await Task.Delay(TimeSpan.FromSeconds(4.5));

                await WriteAsync(stream, HsmsMessage.Control(HsmsSType.SeparateReq, sysBytes.Next()));
                Log.Information("Sent Separate.req - ending session.");
                sm.OnSeparatedOrDeselected();
            }
            catch (HsmsProtocolException ex)
            {
                Log.Error($"Protocol error: {ex.Message}");
            }
            catch (TimeoutException ex)
            {
                Log.Error($"Timeout error: {ex.Message}");
            }
            finally
            {
                cts.Cancel();
                client.Close();
                sm.OnTcpDisconnected();

                try { await receiveLoopTask; } catch { /* 수신 루프 종료 예외 무시 */ }
                Log.Information("Host exiting.");
            }
        }

        //백그라운드 수신 루프(요청 응답 매칭 및 Unsolicited 이벤트 처리)
        private static async Task ReceiveLoopAsync(NetworkStream stream, HsmsStateMachine sm, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var msg = await HsmsFraming.ReadAsync(stream, ct);
                    if (msg is null)
                    {
                        Log.Information("Equipment closed the TCP connection.");
                        return;
                    }

                    Log.Information($"RECV {msg}");
                    sm.AssertValid(msg.Header.SType);

                    // 대기 중인 요청이 있으면 TaskCompletionSource에 결과 전달
                    if (_pendingRequests.TryRemove(msg.Header.SystemBytes, out var waiter))
                    {
                        waiter.TrySetResult(msg);
                        continue;
                    }

                    // Await하는 요청이 없는 경우 -> Equipment의 자발적(Unsolicited) 메시지
                    if (msg.Header.SType == HsmsSType.DataMessage)
                    {
                        Log.Information($"  -> unsolicited event: \"{msg.BodyAsText()}\"");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (HsmsProtocolException ex)
            {
                Log.Error($"Protocol violation: {ex.Message}");
            }
            catch (IOException)
            {
                Log.Information("Connection lost while receiving.");
            }
        }

        // 메시지 전송 (동시성 제어를 위한 SemaphoreSlim 적용)
        private static async Task WriteAsync(NetworkStream stream, HsmsMessage msg)
        {
            await _writeLock.WaitAsync();
            try
            {
                await HsmsFraming.WriteAsync(stream, msg, CancellationToken.None);
                Log.Information($"SEND {msg}");
            }
            finally
            {
                _writeLock.Release();
            }
        }

        // 요청 전송 및 응답 대기 (타임아웃 핸들링)
        private static async Task<HsmsMessage> SendAndWaitAsync(NetworkStream stream, HsmsMessage request, TimeSpan? timeout = null)
        {
            var tcs = new TaskCompletionSource<HsmsMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[request.Header.SystemBytes] = tcs;

            await WriteAsync(stream, request);

            using var timeoutCts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
            try
            {
                await using (timeoutCts.Token.Register(() => tcs.TrySetException(
                    new TimeoutException($"Timed out waiting for response to SystemBytes={request.Header.SystemBytes}."))))
                {
                    return await tcs.Task;
                }
            }
            finally
            {
                // Belt-and-suspenders: normally ReceiveLoopAsync already removed this entry when the
                // response arrived. On timeout (or any other failure) it never does, so without this
                // the entry would sit in _pendingRequests forever - clean it up unconditionally.
                _pendingRequests.TryRemove(request.Header.SystemBytes, out _);
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
    }
}
