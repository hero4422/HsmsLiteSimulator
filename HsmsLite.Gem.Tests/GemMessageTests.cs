namespace HsmsLite.Gem.Tests
{
    public class GemMessageTests
    {
        [Fact]
        public void Test_S1F2RoundTrip()
        {
            var msg = S1Messages.BuildS1F2(sessionId: 1, systemBytes: 1, "HSMSLITE-EQP", "1.0.0");
            var (mdln, softRev) = S1Messages.ParseS1F2(msg);
            Assert.Equal("HSMSLITE-EQP", mdln);
            Assert.Equal("1.0.0", softRev);
        }

        [Fact]
        public void Test_S1F14RoundTrip()
        {
            var msg = S1Messages.BuildS1F14(sessionId: 1, systemBytes: 2, commAccepted: true, "HSMSLITE-EQP", "1.0.0");
            var (commAccepted, mdln, softRev) = S1Messages.ParseS1F14(msg);
            Assert.True(commAccepted);
            Assert.Equal("HSMSLITE-EQP", mdln);
            Assert.Equal("1.0.0", softRev);
        }

        [Fact]
        public void Test_S1F3F4RoundTrip()
        {
            var svids = new uint[] { 1, 2, 3 };
            var request = S1Messages.BuildS1F3(sessionId: 1, systemBytes: 3, svids);
            Assert.Equal(svids, S1Messages.ParseS1F3(request));

            var values = new uint[] { 101, 202, 303 };
            var reply = S1Messages.BuildS1F4(sessionId: 1, systemBytes: 3, values);
            Assert.Equal(values, S1Messages.ParseS1F4(reply));
        }

        [Fact]
        public void Test_S2F41F42RoundTrip()
        {
            var request = S2Messages.BuildS2F41(sessionId: 1, systemBytes: 4, "START");
            Assert.Equal("START", S2Messages.ParseS2F41(request));

            var reply = S2Messages.BuildS2F42(sessionId: 1, systemBytes: 4, accepted: true);
            Assert.True(S2Messages.ParseS2F42(reply));
        }

        [Fact]
        public void Test_S6F11F12RoundTrip()
        {
            var reportValues = new Secs2Item[] { new Secs2Ascii("RUN"), new Secs2U4(25) };
            var request = S6Messages.BuildS6F11(sessionId: 1, systemBytes: 5, dataId: 1, ceid: 1001, reportValues);
            var (dataId, ceid, values) = S6Messages.ParseS6F11(request);
            Assert.Equal(1U, dataId);
            Assert.Equal(1001U, ceid);
            Assert.Equal("RUN", ((Secs2Ascii)values[0]).Value);
            Assert.Equal(25U, ((Secs2U4)values[1]).Value);

            var reply = S6Messages.BuildS6F12(sessionId: 1, systemBytes: 5, accepted: true);
            Assert.True(S6Messages.ParseS6F12(reply));
        }

        [Fact]
        public void Test_WBitSetOnlyWhenReplyExpected()
        {
            var request = S1Messages.BuildS1F1(sessionId: 1, systemBytes: 6);
            var reply = S1Messages.BuildS1F2(sessionId: 1, systemBytes: 6, "M", "1.0");

            Assert.Equal((byte)0x81, request.Header.Byte3); // function 1 | W-bit
            Assert.Equal((byte)0x02, reply.Header.Byte3);   // function 2, no W-bit
        }

        [Fact]
        public void Test_StreamFunctionMismatchThrows()
        {
            var wrongMessage = S1Messages.BuildS1F1(sessionId: 1, systemBytes: 7);
            Assert.Throws<GemMessageException>(() => S1Messages.ParseS1F2(wrongMessage));
        }
    }
}
