namespace HsmsLite.Gem.Tests
{
    public class Secs2ItemTests
    {
        [Fact]
        public void Test_AsciiRoundTrip()
        {
            var item = new Secs2Ascii("HSMSLITE-EQP");
            var decoded = (Secs2Ascii)Secs2Item.Decode(item.Encode());
            Assert.Equal("HSMSLITE-EQP", decoded.Value);
        }

        [Fact]
        public void Test_BooleanRoundTrip()
        {
            var item = new Secs2Boolean(true);
            var decoded = (Secs2Boolean)Secs2Item.Decode(item.Encode());
            Assert.True(decoded.Value);
        }

        [Fact]
        public void Test_U4RoundTrip()
        {
            var item = new Secs2U4(0xDEADBEEF);
            var decoded = (Secs2U4)Secs2Item.Decode(item.Encode());
            Assert.Equal(0xDEADBEEFU, decoded.Value);
        }

        [Fact]
        public void Test_NestedListRoundTrip()
        {
            var item = new Secs2List(
                new Secs2Boolean(true),
                new Secs2List(new Secs2Ascii("MDLN"), new Secs2Ascii("1.0.0")));

            var decoded = (Secs2List)Secs2Item.Decode(item.Encode());
            Assert.Equal(2, decoded.Items.Count);
            Assert.True(((Secs2Boolean)decoded.Items[0]).Value);

            var inner = (Secs2List)decoded.Items[1];
            Assert.Equal("MDLN", ((Secs2Ascii)inner.Items[0]).Value);
            Assert.Equal("1.0.0", ((Secs2Ascii)inner.Items[1]).Value);
        }

        [Fact]
        public void Test_EmptyListRoundTrip()
        {
            var item = new Secs2List();
            var decoded = (Secs2List)Secs2Item.Decode(item.Encode());
            Assert.Empty(decoded.Items);
        }

        [Fact]
        public void Test_MultipleItemsConsumeExactBytes()
        {
            var buffer = new Secs2U4(7).Encode().Concat(new Secs2Ascii("ok").Encode()).ToArray();

            var first = Secs2Item.Decode(buffer, out var consumed);
            var second = Secs2Item.Decode(buffer.AsSpan(consumed));

            Assert.Equal(7U, ((Secs2U4)first).Value);
            Assert.Equal("ok", ((Secs2Ascii)second).Value);
        }

        [Fact]
        public void Test_UnsupportedFormatCodeThrows()
        {
            // Binary (format code 8), 1 length byte, 1 byte of body.
            var raw = new byte[] { (8 << 2) | 1, 1, 0x00 };
            Assert.Throws<Secs2FormatException>(() => Secs2Item.Decode(raw));
        }
    }
}
