using HsmsLite.Protocol;

namespace HsmsLite.Gem
{
    /// <summary>
    /// Human-readable names for the GEM (SEMI E30) data messages and HSMS (SEMI E37) control
    /// messages this project implements, used to annotate communication trace logs the way
    /// traditional SECS/GEM driver logs do (e.g. "S1F13 Establish Communications Request").
    /// </summary>
    public static class GemMessageNames
    {
        public static string? DataMessageName(byte stream, byte function) => (stream, function) switch
        {
            (1, 1) => "Are You There",
            (1, 2) => "On Line Data",
            (1, 3) => "Selected Equipment Status Request",
            (1, 4) => "Selected Equipment Status Data",
            (1, 13) => "Establish Communications Request",
            (1, 14) => "Establish Communications Request Acknowledge",
            (2, 41) => "Host Command Send",
            (2, 42) => "Host Command Acknowledge",
            (6, 11) => "Event Report Send",
            (6, 12) => "Event Report Acknowledge",
            _ => null,
        };

        public static string ControlMessageName(HsmsSType sType) => sType switch
        {
            HsmsSType.SelectReq => "Select.req",
            HsmsSType.SelectRsp => "Select.rsp",
            HsmsSType.DeselectReq => "Deselect.req",
            HsmsSType.DeselectRsp => "Deselect.rsp",
            HsmsSType.LinktestReq => "Linktest.req",
            HsmsSType.LinktestRsp => "Linktest.rsp",
            HsmsSType.RejectReq => "Reject.req",
            HsmsSType.SeparateReq => "Separate.req",
            _ => sType.ToString(),
        };

        /// <summary>Just the name, no "SxFy" prefix - for embedding next to header fields that are
        /// already printed separately (see <see cref="HsmsCommTrace"/>).</summary>
        public static string Name(HsmsMessage msg)
        {
            var header = msg.Header;
            if (header.SType != HsmsSType.DataMessage)
                return ControlMessageName(header.SType);

            var function = (byte)(header.Byte3 & 0x7F);
            return DataMessageName(header.Byte2, function) ?? $"S{header.Byte2}F{function}";
        }

        /// <summary>A standalone display label, e.g. "S1F13 Establish Communications Request" or
        /// "Select.req" - for transaction markers that aren't already next to a header dump.</summary>
        public static string Describe(HsmsMessage msg)
        {
            var header = msg.Header;
            if (header.SType != HsmsSType.DataMessage)
                return ControlMessageName(header.SType);

            var function = (byte)(header.Byte3 & 0x7F);
            var name = DataMessageName(header.Byte2, function);
            return name is null ? $"S{header.Byte2}F{function}" : $"S{header.Byte2}F{function} {name}";
        }
    }
}
