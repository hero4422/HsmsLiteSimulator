using System.Text;

namespace HsmsLite.Gem
{
    public sealed class Secs2List : Secs2Item
    {
        public IReadOnlyList<Secs2Item> Items { get; }

        public Secs2List(params Secs2Item[] items) => Items = items;
        public Secs2List(IEnumerable<Secs2Item> items) => Items = items.ToArray();

        public override byte[] Encode()
        {
            var childBytes = Items.SelectMany(item => item.Encode()).ToArray();
            return EncodeHeaderAndValue(FormatList, Items.Count, childBytes);
        }

        internal static Secs2List DecodeBody(ReadOnlySpan<byte> data, int offset, int itemCount, out int bytesConsumed)
        {
            var items = new List<Secs2Item>(itemCount);
            var pos = offset;
            for (var i = 0; i < itemCount; i++)
            {
                items.Add(Decode(data[pos..], out var consumed));
                pos += consumed;
            }
            bytesConsumed = pos;
            return new Secs2List(items);
        }

        public override string ToString() => $"[{string.Join(", ", Items)}]";

        public override void WriteTree(StringBuilder sb, int indent)
        {
            sb.Append(' ', indent).Append("<L  ").Append(Items.Count).AppendLine();
            foreach (var item in Items)
                item.WriteTree(sb, indent + 4);
            sb.Append(' ', indent).AppendLine("> ..");
        }
    }
}
