using HsmsLite.Protocol;

namespace HsmsLite.Gem
{
    /// <summary>Shared plumbing for the S1/S2/S6 message builders: header construction (setting
    /// the W-bit when a reply is expected) and stream/function verification on receipt.</summary>
    internal static class GemMessage
    {
        public static HsmsMessage Build(ushort sessionId, byte stream, byte function, bool replyExpected,
            uint systemBytes, Secs2Item? body = null)
        {
            var byte3 = replyExpected ? (byte)(function | 0x80) : function;
            var header = new HsmsMessageHeader(sessionId, stream, byte3, HsmsSType.DataMessage, systemBytes);
            return new HsmsMessage(header, body?.Encode() ?? Array.Empty<byte>());
        }

        public static void AssertStreamFunction(HsmsMessage msg, byte expectedStream, byte expectedFunction)
        {
            var actualFunction = (byte)(msg.Header.Byte3 & 0x7F);
            if (msg.Header.Byte2 != expectedStream || actualFunction != expectedFunction)
                throw new GemMessageException(
                    $"Expected S{expectedStream}F{expectedFunction} but received S{msg.Header.Byte2}F{actualFunction}.");
        }

        public static Secs2Item? ParseBody(HsmsMessage msg) => msg.Body.Length == 0 ? null : Secs2Item.Decode(msg.Body);
    }
}
