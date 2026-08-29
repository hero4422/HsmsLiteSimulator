using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HsmsLite.Protocol
{
    // 헤더 + 바디
    public sealed class HsmsMessage
    {
        public HsmsMessageHeader Header { get; }
        public byte[] Body { get; }

        public HsmsMessage(HsmsMessageHeader header, byte[]? body = null)
        {
            Header = header;
            Body = body ?? Array.Empty<byte>();
        }

        public static HsmsMessage Control(HsmsSType sType, uint systemBytes, byte byte3 = 0)
            => new(HsmsMessageHeader.Control(sType, systemBytes, byte3));

        public static HsmsMessage DataText(ushort sessionId, byte stream, byte function, uint systemBytes, string text)
        {
            var header = new HsmsMessageHeader(sessionId, stream, function, HsmsSType.DataMessage, systemBytes);
            return new HsmsMessage(header, Encoding.UTF8.GetBytes(text));
        }

        public string BodyAsText() => Body.Length == 0 ? string.Empty : Encoding.UTF8.GetString(Body);

        /// <summary>Reconstructs the exact bytes this message would occupy on the wire (4-byte
        /// length prefix + 10-byte header + body), for trace logging (hex dumps) independent of
        /// the actual send/receive path.</summary>
        public byte[] ToWireBytes()
        {
            var headerBytes = Header.ToBytes();
            var totalLength = headerBytes.Length + Body.Length;

            var frame = new byte[4 + totalLength];
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(0, 4), (uint)totalLength);
            headerBytes.CopyTo(frame, 4);
            Body.CopyTo(frame, 4 + headerBytes.Length);
            return frame;
        }

        public override string ToString()
        {
            var bodyPreview = Body.Length == 0 ? "(empty)" : $"\"{BodyAsText()}\"";
            return $"{Header} Body={bodyPreview}";
        }
    }
}
