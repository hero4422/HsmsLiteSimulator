using System.Text;

namespace HsmsLite.Gem
{
    public sealed class Secs2Boolean : Secs2Item
    {
        public bool Value { get; }

        public Secs2Boolean(bool value) => Value = value;

        public override byte[] Encode() => EncodeHeaderAndValue(FormatBoolean, 1, new[] { (byte)(Value ? 1 : 0) });

        internal static Secs2Boolean DecodeBody(ReadOnlySpan<byte> data, int offset, int byteCount, out int bytesConsumed)
        {
            if (byteCount != 1)
                throw new Secs2FormatException($"SECS-II Boolean must be 1 byte, got {byteCount}.");
            bytesConsumed = offset + 1;
            return new Secs2Boolean(data[offset] != 0);
        }

        public override string ToString() => Value.ToString();

        public override void WriteTree(StringBuilder sb, int indent)
            => sb.Append(' ', indent).Append("<B  1 ").Append(Value ? "01" : "00").AppendLine(" >");
    }
}
