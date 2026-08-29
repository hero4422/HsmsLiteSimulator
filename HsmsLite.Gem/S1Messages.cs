using HsmsLite.Protocol;

namespace HsmsLite.Gem
{
    /// <summary>Stream 1 GEM messages: Are You There, Establish Communications, and Selected
    /// Equipment Status Request/Data.</summary>
    public static class S1Messages
    {
        // S1F1 - Are You There (empty body)
        public static HsmsMessage BuildS1F1(ushort sessionId, uint systemBytes)
            => GemMessage.Build(sessionId, 1, 1, replyExpected: true, systemBytes);

        // S1F2 - On Line Data: L,2 { MDLN(A), SOFTREV(A) }
        public static HsmsMessage BuildS1F2(ushort sessionId, uint systemBytes, string mdln, string softRev)
            => GemMessage.Build(sessionId, 1, 2, replyExpected: false, systemBytes,
                new Secs2List(new Secs2Ascii(mdln), new Secs2Ascii(softRev)));

        public static (string Mdln, string SoftRev) ParseS1F2(HsmsMessage msg)
        {
            GemMessage.AssertStreamFunction(msg, 1, 2);
            var list = (Secs2List)GemMessage.ParseBody(msg)!;
            return (((Secs2Ascii)list.Items[0]).Value, ((Secs2Ascii)list.Items[1]).Value);
        }

        // S1F13 - Establish Communications Request: L,0 (host-originated, empty list)
        public static HsmsMessage BuildS1F13(ushort sessionId, uint systemBytes)
            => GemMessage.Build(sessionId, 1, 13, replyExpected: true, systemBytes, new Secs2List());

        // S1F14 - Establish Communications Acknowledge: L,2 { COMMACK(Boolean), L,2 { MDLN(A), SOFTREV(A) } }
        public static HsmsMessage BuildS1F14(ushort sessionId, uint systemBytes, bool commAccepted, string mdln, string softRev)
            => GemMessage.Build(sessionId, 1, 14, replyExpected: false, systemBytes,
                new Secs2List(new Secs2Boolean(commAccepted), new Secs2List(new Secs2Ascii(mdln), new Secs2Ascii(softRev))));

        public static (bool CommAccepted, string Mdln, string SoftRev) ParseS1F14(HsmsMessage msg)
        {
            GemMessage.AssertStreamFunction(msg, 1, 14);
            var list = (Secs2List)GemMessage.ParseBody(msg)!;
            var commAccepted = ((Secs2Boolean)list.Items[0]).Value;
            var idList = (Secs2List)list.Items[1];
            return (commAccepted, ((Secs2Ascii)idList.Items[0]).Value, ((Secs2Ascii)idList.Items[1]).Value);
        }

        // S1F3 - Selected Equipment Status Request: L,n { SVID(U4), ... }
        public static HsmsMessage BuildS1F3(ushort sessionId, uint systemBytes, IReadOnlyList<uint> svids)
            => GemMessage.Build(sessionId, 1, 3, replyExpected: true, systemBytes,
                new Secs2List(svids.Select(id => (Secs2Item)new Secs2U4(id))));

        public static IReadOnlyList<uint> ParseS1F3(HsmsMessage msg)
        {
            GemMessage.AssertStreamFunction(msg, 1, 3);
            var list = (Secs2List)GemMessage.ParseBody(msg)!;
            return list.Items.Cast<Secs2U4>().Select(item => item.Value).ToArray();
        }

        // S1F4 - Selected Equipment Status Data: L,n { SV(U4), ... }, same order as the request's SVIDs
        public static HsmsMessage BuildS1F4(ushort sessionId, uint systemBytes, IReadOnlyList<uint> values)
            => GemMessage.Build(sessionId, 1, 4, replyExpected: false, systemBytes,
                new Secs2List(values.Select(v => (Secs2Item)new Secs2U4(v))));

        public static IReadOnlyList<uint> ParseS1F4(HsmsMessage msg)
        {
            GemMessage.AssertStreamFunction(msg, 1, 4);
            var list = (Secs2List)GemMessage.ParseBody(msg)!;
            return list.Items.Cast<Secs2U4>().Select(item => item.Value).ToArray();
        }
    }
}
