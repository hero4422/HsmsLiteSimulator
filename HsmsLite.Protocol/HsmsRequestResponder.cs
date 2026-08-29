using System.Collections.Concurrent;

namespace HsmsLite.Protocol
{
    /// <summary>
    /// Sends messages over an HSMS session and correlates replies to pending requests by
    /// SystemBytes (the req/rsp transaction id). One instance per session, shared by whatever
    /// single background task owns the read loop: that task should call <see cref="TryResolve"/>
    /// on every received message before treating it as an incoming request or unsolicited message.
    /// </summary>
    public sealed class HsmsRequestResponder
    {
        private readonly Stream _stream;
        private readonly ConcurrentDictionary<uint, TaskCompletionSource<HsmsMessage>> _pending = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public HsmsRequestResponder(Stream stream) => _stream = stream;

        public Task<HsmsMessage?> ReadAsync(CancellationToken ct = default) => HsmsFraming.ReadAsync(_stream, ct);

        public async Task SendAsync(HsmsMessage message, CancellationToken ct = default)
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await HsmsFraming.WriteAsync(_stream, message, ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<HsmsMessage> SendAndWaitAsync(HsmsMessage request, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<HsmsMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[request.Header.SystemBytes] = tcs;

            await SendAsync(request, ct).ConfigureAwait(false);

            using var timeoutCts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
            try
            {
                await using (timeoutCts.Token.Register(() => tcs.TrySetException(
                    new TimeoutException($"Timed out waiting for response to SystemBytes={request.Header.SystemBytes}."))))
                {
                    return await tcs.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                _pending.TryRemove(request.Header.SystemBytes, out _);
            }
        }

        /// <summary>Resolves a pending <see cref="SendAndWaitAsync"/> if this message is its
        /// reply. Returns false if no request is waiting on this SystemBytes, meaning the caller
        /// should treat the message as an incoming request or an unsolicited message.</summary>
        public bool TryResolve(HsmsMessage message)
        {
            if (_pending.TryRemove(message.Header.SystemBytes, out var waiter))
            {
                waiter.TrySetResult(message);
                return true;
            }
            return false;
        }
    }
}
