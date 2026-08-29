using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Buffers.Binary;

namespace HsmsLite.Protocol
{
    // 10바이트 헤더
    #region HSMS Header
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
    /*
        [0-1] SessionID: DeviceID, 장비 또는 장비 그룹 구분을 위한 디바이스 아이디
        [2] Header Byte2
            0: Control Message를 의미
            0 이외의 값: 메세지의 Stream 넘버를 의미
            * Stream 전송일 때
            1byte = 8bit
            8개의 비트 중 가장 앞의 비트가 1일 경우 Wait bit 임을 나타냄
        [3] Header Byte3
            0: Control Message를 의미
            0 이외의 값: 메세지의 Function 넘버를 의미
        [4] Ptype
            0: SECS-II사용을 의미
        [5] Stype
            0: Data Message임을 정의. 데이터 전송을 의미
            0 이외의 값: Control Message임을 정의
            현 상태를 결정(1~9)
            1: Select.req
            2: Select.rsp
            3: Deselect.req
            4: Deselect.rsp
            5: Linktest.req
            6: Linktest.rsp
            7: Reject.req
            8: not used
            9: Separate.req
        [6-9] SystemByte: 통신 고유 ID값으로 통신할 때마다 고유값 전송
    */
    #endregion
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
