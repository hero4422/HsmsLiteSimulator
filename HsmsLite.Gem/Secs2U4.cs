using System.Buffers.Binary;

using System.Text;

namespace HsmsLite.Gem
{
    public sealed class Secs2U4 : Secs2Item
    {
        public uint Value { get; }

        public Secs2U4(uint value) => Value = value;

        public override byte[] Encode()
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, Value);
            return EncodeHeaderAndValue(FormatU4, 4, bytes);
        }

        internal static Secs2U4 DecodeBody(ReadOnlySpan<byte> data, int offset, int byteCount, out int bytesConsumed)
        {
            if (byteCount != 4)
                throw new Secs2FormatException($"SECS-II U4 must be 4 bytes, got {byteCount}.");
            bytesConsumed = offset + 4;
            return new Secs2U4(BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4)));
        }

        public override string ToString() => Value.ToString();

        public override void WriteTree(StringBuilder sb, int indent)
            => sb.Append(' ', indent).Append("<U4  1 ").Append(Value).AppendLine(" >");
    }
}
