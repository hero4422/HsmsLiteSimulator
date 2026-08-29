using System.Text;
using HsmsLite.Protocol;

namespace HsmsLite.Gem
{
    /// <summary>
    /// Formats a sent/received <see cref="HsmsMessage"/> as a multi-line communication trace:
    /// a summary line (direction, Stream/Function, SType, timestamp), a raw hex dump of the wire
    /// bytes, and (for data messages with a body) the decoded SECS-II item tree. This mirrors the
    /// trace format traditional SECS/GEM driver logs (e.g. legacy .ocx-based tools) use, which is
    /// far more useful for debugging than a single flat line.
    /// </summary>
    public static class HsmsCommTrace
    {
        public static string Format(string direction, HsmsMessage msg)
        {
            var header = msg.Header;
            var function = (byte)(header.Byte3 & 0x7F);

            var sb = new StringBuilder();
            sb.AppendLine($"({direction}) S{header.Byte2}  F{function} (SType:{(int)header.SType})  {GemMessageNames.Name(msg)}  - {DateTime.Now:MM/dd/yy HH:mm:ss}");
            AppendHexDump(sb, msg.ToWireBytes());

            if (msg.Body.Length > 0)
            {
                try
                {
                    Secs2Item.Decode(msg.Body).WriteTree(sb, 4);
                }
                catch (Secs2FormatException ex)
                {
                    sb.AppendLine($"    (failed to decode SECS-II body: {ex.Message})");
                }
            }

            return sb.ToString().TrimEnd('\r', '\n');
        }

        /// <summary>Marks the start of a request/reply transaction the caller initiated (e.g.
        /// right before sending a .req and awaiting its .rsp), analogous to the "OpenMessage"
        /// markers traditional SECS/GEM driver logs use to bracket a transaction.</summary>
        public static string TransactionOpen(HsmsMessage request)
            => $"---- OPEN  {GemMessageNames.Describe(request)} ----";

        /// <summary>Marks the end of a transaction opened with <see cref="TransactionOpen"/>,
        /// optionally with how long it took to get a reply.</summary>
        public static string TransactionClose(HsmsMessage request, long? elapsedMs = null)
            => elapsedMs is null
                ? $"---- CLOSE {GemMessageNames.Describe(request)} ----"
                : $"---- CLOSE {GemMessageNames.Describe(request)} ({elapsedMs} ms) ----";

        private static void AppendHexDump(StringBuilder sb, byte[] bytes)
        {
            for (var offset = 0; offset < bytes.Length; offset += 16)
            {
                var count = Math.Min(16, bytes.Length - offset);
                sb.Append(offset.ToString("X4")).Append(" >> ");
                for (var i = 0; i < count; i++)
                    sb.Append(bytes[offset + i].ToString("x2")).Append(' ');
                sb.AppendLine();
            }
        }
    }
}
