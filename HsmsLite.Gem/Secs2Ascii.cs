using System.Text;

namespace HsmsLite.Gem
{
    public sealed class Secs2Ascii : Secs2Item
    {
        public string Value { get; }

        public Secs2Ascii(string value) => Value = value;

        public override byte[] Encode()
            => EncodeHeaderAndValue(FormatAscii, Encoding.ASCII.GetByteCount(Value), Encoding.ASCII.GetBytes(Value));

        internal static Secs2Ascii DecodeBody(ReadOnlySpan<byte> data, int offset, int byteCount, out int bytesConsumed)
        {
            bytesConsumed = offset + byteCount;
            return new Secs2Ascii(Encoding.ASCII.GetString(data.Slice(offset, byteCount)));
        }

        public override string ToString() => $"\"{Value}\"";

        public override void WriteTree(StringBuilder sb, int indent)
            => sb.Append(' ', indent).Append("<A  ").Append(Value.Length).Append(" '").Append(Value).AppendLine("'>");
    }
}
