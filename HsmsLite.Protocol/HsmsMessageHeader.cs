using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Buffers.Binary;

namespace HsmsLite.Protocol
{
    // 10바이트 헤더
    /// <summary>
    /// The fixed 10-byte HSMS message header (SEMI E37), sent immediately after the 4-byte
    /// message-length prefix on the wire.
    ///
    /// Byte layout (all multi-byte fields are big-endian / network order):
    ///   [0-1] SessionId   - device/session identifier ("Device ID" for data messages, 0xFFFF for
    ///                        most control messages since they are session-independent)
    ///   [2]   Byte2        - Stream (data msg) or unused (0x00) for control messages
    ///   [3]   Byte3        - Function+W-bit (data msg) or Select-Status (control rsp) or unused
    ///   [4]   PType         - presentation type, always 0x00 for SECS-II / HSMS
    ///   [5]   SType         - message type, see <see cref="HsmsSType"/>
    ///   [6-9] SystemBytes    - transaction id used to correlate a .req with its .rsp
    /// </summary>
    public readonly struct HsmsMessageHeader
    {
        
        public const int Length = 10;
        
        public ushort SessionId { get; }
        public byte Byte2 { get; }
        public byte Byte3 { get; }
        public byte PType { get; }
        public HsmsSType SType { get; }
        public uint SystemBytes { get; }

        public HsmsMessageHeader(ushort sessionId, byte byte2, byte byte3, HsmsSType sType, uint systemBytes, byte pType = 0)
        {
            SessionId = sessionId;
            Byte2 = byte2;
            Byte3 = byte3;
            PType = pType;
            SType = sType;
            SystemBytes = systemBytes;
        }

        public byte[] ToBytes()
        {
            var buf = new byte[Length];
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(0, 2), SessionId);
            buf[2] = Byte2;
            buf[3] = Byte3;
            buf[4] = PType;
            buf[5] = (byte)SType;
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(6, 4), SystemBytes);
            return buf;
        }

        public static HsmsMessageHeader Parse(ReadOnlySpan<byte> buf)
        {
            if (buf.Length != Length)
                throw new ArgumentException($"HSMS header must be exactly {Length} bytes, got {buf.Length}.", nameof(buf));
            
            var sessionId = BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(0, 2));
            var byte2 = buf[2];
            var byte3 = buf[3];
            var pType = buf[4];
            var sType = (HsmsSType)buf[5];
            var systemBytes = BinaryPrimitives.ReadUInt32BigEndian(buf.Slice(6, 4));

            return new HsmsMessageHeader(sessionId, byte2, byte3, sType, systemBytes, pType);
        }

        /// <summary>Convenience factory for control messages, which always use SessionId 0xFFFF and Byte2/Byte3 = 0.</summary>
        public static HsmsMessageHeader Control(HsmsSType sType, uint systemBytes, byte byte3 = 0)
            => new(0xFFFF, 0, byte3, sType, systemBytes);

        public override string ToString()
            => $"[SessionId=0x{SessionId:X4} Byte2=0x{Byte2:X2} Byte3=0x{Byte3:X2} PType={PType} SType={SType} SystemBytes={SystemBytes}]";
    }
}
