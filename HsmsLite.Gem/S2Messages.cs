using HsmsLite.Protocol;

namespace HsmsLite.Gem
{
    /// <summary>Stream 2 GEM messages: Host Command Send/Acknowledge.</summary>
    public static class S2Messages
    {
        // S2F41 - Host Command Send: L,2 { RCMD(A), L,0 { } } (no command parameters in this demo)
        public static HsmsMessage BuildS2F41(ushort sessionId, uint systemBytes, string rcmd)
            => GemMessage.Build(sessionId, 2, 41, replyExpected: true, systemBytes,
                new Secs2List(new Secs2Ascii(rcmd), new Secs2List()));

        public static string ParseS2F41(HsmsMessage msg)
        {
            GemMessage.AssertStreamFunction(msg, 2, 41);
            var list = (Secs2List)GemMessage.ParseBody(msg)!;
            return ((Secs2Ascii)list.Items[0]).Value;
        }

        // S2F42 - Host Command Acknowledge: L,2 { HCACK(Boolean), L,0 { } }
        public static HsmsMessage BuildS2F42(ushort sessionId, uint systemBytes, bool accepted)
            => GemMessage.Build(sessionId, 2, 42, replyExpected: false, systemBytes,
                new Secs2List(new Secs2Boolean(accepted), new Secs2List()));

        public static bool ParseS2F42(HsmsMessage msg)
        {
            GemMessage.AssertStreamFunction(msg, 2, 42);
            var list = (Secs2List)GemMessage.ParseBody(msg)!;
            return ((Secs2Boolean)list.Items[0]).Value;
        }
    }
}
