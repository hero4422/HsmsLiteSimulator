using HsmsLite.Protocol;

namespace HsmsLite.Gem
{
    /// <summary>Stream 6 GEM messages: unsolicited Event Report Send/Acknowledge.</summary>
    public static class S6Messages
    {
        // S6F11 - Event Report Send: L,3 { DATAID(U4), CEID(U4), L,n { report values } }
        public static HsmsMessage BuildS6F11(ushort sessionId, uint systemBytes, uint dataId, uint ceid,
            IReadOnlyList<Secs2Item> reportValues)
            => GemMessage.Build(sessionId, 6, 11, replyExpected: true, systemBytes,
                new Secs2List(new Secs2U4(dataId), new Secs2U4(ceid), new Secs2List(reportValues)));

        public static (uint DataId, uint Ceid, IReadOnlyList<Secs2Item> Values) ParseS6F11(HsmsMessage msg)
        {
            GemMessage.AssertStreamFunction(msg, 6, 11);
            var list = (Secs2List)GemMessage.ParseBody(msg)!;
            var values = ((Secs2List)list.Items[2]).Items;
            return (((Secs2U4)list.Items[0]).Value, ((Secs2U4)list.Items[1]).Value, values);
        }

        // S6F12 - Event Report Acknowledge: single Boolean ack
        public static HsmsMessage BuildS6F12(ushort sessionId, uint systemBytes, bool accepted)
            => GemMessage.Build(sessionId, 6, 12, replyExpected: false, systemBytes, new Secs2Boolean(accepted));

        public static bool ParseS6F12(HsmsMessage msg)
        {
            GemMessage.AssertStreamFunction(msg, 6, 12);
            return ((Secs2Boolean)GemMessage.ParseBody(msg)!).Value;
        }
    }
}
