using System;
using SIPSorcery.Net;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class Vp8PacketReorderBufferUnitTest
    {
        [Fact]
        public void ProcessPacket_ReleasesBufferedPacketsWhenGapCloses()
        {
            var buffer = new Vp8PacketReorderBuffer();
            var start = DateTime.UtcNow;

            var first = buffer.ProcessPacket(CreateVp8Packet(10, true), start);
            var outOfOrder = buffer.ProcessPacket(CreateVp8Packet(12), start.AddMilliseconds(5));
            var recovered = buffer.ProcessPacket(CreateVp8Packet(11), start.AddMilliseconds(10));

            Assert.Single(first.ReleasedPackets);
            Assert.Empty(outOfOrder.ReleasedPackets);
            Assert.Equal(2, recovered.ReleasedPackets.Count);
            Assert.Equal(11, recovered.ReleasedPackets[0].Header.SequenceNumber);
            Assert.Equal(12, recovered.ReleasedPackets[1].Header.SequenceNumber);
        }

        [Fact]
        public void ProcessPacket_ResetsWhenGapTimeoutExpires()
        {
            var buffer = new Vp8PacketReorderBuffer();
            var start = DateTime.UtcNow;

            buffer.ProcessPacket(CreateVp8Packet(100, true), start);
            buffer.ProcessPacket(CreateVp8Packet(102), start.AddMilliseconds(5));

            var timeoutResult = buffer.ProcessPacket(CreateVp8Packet(103), start.AddMilliseconds(70));
            var resynced = buffer.ProcessPacket(CreateVp8Packet(104, true), start.AddMilliseconds(75));

            Assert.True(timeoutResult.FrameResetRequired);
            Assert.Equal(Vp8PacketDropReason.Resync, timeoutResult.DropReason);
            Assert.Empty(timeoutResult.ReleasedPackets);
            Assert.Single(resynced.ReleasedPackets);
            Assert.Equal(104, resynced.ReleasedPackets[0].Header.SequenceNumber);
        }

        private static RTPPacket CreateVp8Packet(ushort sequenceNumber, bool start = false)
        {
            var packet = new RTPPacket(15);
            packet.Header.SequenceNumber = sequenceNumber;
            packet.Header.PayloadType = 96;
            packet.Payload = start
                ? new byte[] { 0x10, 0x00, 0x00 }
                : new byte[] { 0x00, 0x00, 0x00 };
            return packet;
        }
    }
}
