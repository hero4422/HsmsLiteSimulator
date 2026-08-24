using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HsmsLite.Protocol
{
    // TCP 프레이밍
    /// <summary>
    /// Wire framing for HSMS over TCP: a 4-byte big-endian length prefix (the byte count of the
    /// header + body that follows), then the 10-byte header, then the body. Handles partial reads,
    /// since TCP gives no message-boundary guarantee.
    /// </summary>
    public static class HsmsFraming
    {
        private const int LengthPrefixSize = 4;
        private const int MaxMessageLength = 4 * 1024 * 1024; // sanity cap for a demo/lite implementation
        
        public static async Task WriteAsync(Stream stream, HsmsMessage message, CancellationToken ct = default)
        {
            var headerBytes = message.Header.ToBytes();
            var totalLength = headerBytes.Length + message.Body.Length;

            var frame = new byte[LengthPrefixSize + totalLength];
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(0, LengthPrefixSize), (uint)totalLength);
            headerBytes.CopyTo(frame, LengthPrefixSize);
            message.Body.CopyTo(frame, LengthPrefixSize + headerBytes.Length);

            await stream.WriteAsync(frame, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        /// <summary>Reads one full frame, or returns null if the peer closed the connection cleanly.</summary>
        public static async Task<HsmsMessage?> ReadAsync(Stream stream, CancellationToken ct = default)
        {
            var lengthBuf = new byte[LengthPrefixSize];
            if (!await ReadExactAsync(stream, lengthBuf, ct).ConfigureAwait(false))
                return null;

            var totalLength = BinaryPrimitives.ReadUInt32BigEndian(lengthBuf);
            if (totalLength < HsmsMessageHeader.Length || totalLength > MaxMessageLength)
                throw new InvalidDataException($"Implausible HSMS message length {totalLength} bytes.");

            var payload = new byte[totalLength];
            if (!await ReadExactAsync(stream, payload, ct).ConfigureAwait(false))
                throw new EndOfStreamException("Connection closed mid-message.");

            var header = HsmsMessageHeader.Parse(payload.AsSpan(0, HsmsMessageHeader.Length));
            var body = payload[HsmsMessageHeader.Length..];
            return new HsmsMessage(header, body);
        }

        private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
                if (read == 0)
                    return offset == 0 ? false : throw new EndOfStreamException("Connection closed mid-message.");
                offset += read;
            }
            return true;
        }
    }
}
