using HsmsLite.Protocol;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace HsmsLite.Protocol.Tests
{
    public class ProtocolTests
    {
        [Fact]
        public void Test_HeaderRoundTrip()
        {
            var header = new HsmsMessageHeader(sessionId: 0x1234, byte2: 6, byte3: 11, HsmsSType.DataMessage, systemBytes: 0xDEADBEEF);
            var bytes = header.ToBytes();
            var parsed = HsmsMessageHeader.Parse(bytes);

            Assert.Equal(HsmsMessageHeader.Length, bytes.Length);
            Assert.Equal((ushort)0x1234, parsed.SessionId);
            Assert.Equal(6, parsed.Byte2);
            Assert.Equal(11, parsed.Byte3);
            Assert.Equal(HsmsSType.DataMessage, parsed.SType);
            Assert.Equal(0xDEADBEEFU, parsed.SystemBytes);
        }

        [Fact]
        public void Test_DataTextRoundTrip()
        {
            var msg = HsmsMessage.DataText(1, 6, 11, 42, "EquipmentState=RUN;LotId=LOT-1001");
            Assert.Equal("EquipmentState=RUN;LotId=LOT-1001", msg.BodyAsText());
            Assert.Equal(HsmsSType.DataMessage, msg.Header.SType);
        }

        [Fact]
        public void Test_StateMachineHappyPath()
        {
            var sm = new HsmsStateMachine();
            Assert.Equal(HsmsConnectionState.NotConnected, sm.State);

            sm.OnTcpConnected();
            Assert.Equal(HsmsConnectionState.Connected, sm.State);

            sm.OnSelected();
            Assert.Equal(HsmsConnectionState.Selected, sm.State);

            sm.OnSeparatedOrDeselected();
            Assert.Equal(HsmsConnectionState.Connected, sm.State);

            sm.OnTcpDisconnected();
            Assert.Equal(HsmsConnectionState.NotConnected, sm.State);
        }

        [Fact]
        public void Test_SelectBeforeConnectRejected()
        {
            var sm = new HsmsStateMachine();
            Assert.Throws<HsmsProtocolException>(() => sm.AssertValid(HsmsSType.SelectReq));
        }

        [Fact]
        public void Test_DataMessageBeforeSelectedRejected()
        {
            var sm = new HsmsStateMachine();
            sm.OnTcpConnected();
            Assert.Throws<HsmsProtocolException>(() => sm.AssertValid(HsmsSType.DataMessage));

            sm.OnSelected();
            sm.AssertValid(HsmsSType.DataMessage);
        }

        [Fact]
        public void Test_DoubleSelectRejected()
        {
            var sm = new HsmsStateMachine();
            sm.OnTcpConnected();
            sm.OnSelected();
            Assert.Throws<HsmsProtocolException>(() => sm.AssertValid(HsmsSType.SelectReq));
        }

        [Fact]
        public void Test_SystemBytesGeneratorIsMonotonic()
        {
            var gen = new SystemBytesGenerator();
            var seen = new HashSet<uint>();
            uint previous = 0;

            for (var i = 0; i < 1000; i++)
            {
                var next = gen.Next();
                Assert.True(next > previous);
                Assert.True(seen.Add(next));
                previous = next;
            }
        }

        [Fact]
        public async Task Test_FramingRoundTripAsync()
        {
            using var buffer = new MemoryStream();
            var sent = HsmsMessage.DataText(1, 6, 11, 7, "EventReport#1;EquipmentState=RUN");

            await HsmsFraming.WriteAsync(buffer, sent);
            buffer.Position = 0;
            var received = await HsmsFraming.ReadAsync(buffer);

            Assert.NotNull(received);
            Assert.Equal(7U, received.Header.SystemBytes);
            Assert.Equal("EventReport#1;EquipmentState=RUN", received.BodyAsText());
        }

        [Fact]
        public async Task Test_FramingMultipleMessagesAsync()
        {
            using var buffer = new MemoryStream();
            await HsmsFraming.WriteAsync(buffer, HsmsMessage.Control(HsmsSType.SelectReq, 1));
            await HsmsFraming.WriteAsync(buffer, HsmsMessage.DataText(1, 1, 1, 2, "StatusRequest"));
            await HsmsFraming.WriteAsync(buffer, HsmsMessage.Control(HsmsSType.SeparateReq, 3));
            buffer.Position = 0;

            var first = await HsmsFraming.ReadAsync(buffer);
            var second = await HsmsFraming.ReadAsync(buffer);
            var third = await HsmsFraming.ReadAsync(buffer);
            var fourth = await HsmsFraming.ReadAsync(buffer);

            Assert.NotNull(first);
            Assert.Equal(HsmsSType.SelectReq, first.Header.SType);

            Assert.NotNull(second);
            Assert.Equal(HsmsSType.DataMessage, second.Header.SType);
            Assert.Equal("StatusRequest", second.BodyAsText());

            Assert.NotNull(third);
            Assert.Equal(HsmsSType.SeparateReq, third.Header.SType);

            Assert.Null(fourth);
        }
    }
}