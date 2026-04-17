using System;
using System.Collections.Generic;

namespace SIPSorcery.Net
{
    public enum Vp8PacketDropReason
    {
        None = 0,
        Duplicate = 1,
        Late = 2,
        Resync = 3
    }

    public sealed class Vp8PacketReorderResult
    {
        public List<RTPPacket> ReleasedPackets { get; } = new List<RTPPacket>();
        public bool FrameResetRequired { get; set; }
        public int BufferedPacketCount { get; set; }
        public Vp8PacketDropReason DropReason { get; set; }
    }

    public sealed class Vp8PacketReorderBuffer
    {
        private const int DEFAULT_MAX_BUFFERED_PACKETS = 32;
        private static readonly TimeSpan DEFAULT_GAP_TIMEOUT = TimeSpan.FromMilliseconds(60);

        private readonly Dictionary<ushort, RTPPacket> _buffer = new Dictionary<ushort, RTPPacket>();
        private ushort _expectedSequenceNumber;
        private DateTime _gapDetectedAtUtc = DateTime.MinValue;
        private bool _hasExpectedSequence;
        private bool _awaitingFrameStart = true;

        public Vp8PacketReorderBuffer(int maxBufferedPackets = DEFAULT_MAX_BUFFERED_PACKETS, TimeSpan? gapTimeout = null)
        {
            MaxBufferedPackets = maxBufferedPackets > 0 ? maxBufferedPackets : DEFAULT_MAX_BUFFERED_PACKETS;
            GapTimeout = gapTimeout ?? DEFAULT_GAP_TIMEOUT;
        }

        public int MaxBufferedPackets { get; }
        public TimeSpan GapTimeout { get; }

        public Vp8PacketReorderResult ProcessPacket(RTPPacket packet, DateTime utcNow)
        {
            var result = new Vp8PacketReorderResult();
            ushort sequenceNumber = (ushort)packet.Header.SequenceNumber;
            bool isStartPacket = IsVp8StartPacket(packet);

            if (_awaitingFrameStart)
            {
                if (!isStartPacket)
                {
                    result.DropReason = Vp8PacketDropReason.Resync;
                    result.BufferedPacketCount = _buffer.Count;
                    return result;
                }

                StartFrom(sequenceNumber);
            }

            if (_hasExpectedSequence && sequenceNumber == _expectedSequenceNumber)
            {
                ReleasePacket(packet, utcNow, result);
                return result;
            }

            if (_hasExpectedSequence && IsSequenceBehind(sequenceNumber, _expectedSequenceNumber))
            {
                result.DropReason = _buffer.ContainsKey(sequenceNumber)
                    ? Vp8PacketDropReason.Duplicate
                    : Vp8PacketDropReason.Late;
                result.BufferedPacketCount = _buffer.Count;
                return result;
            }

            if (_buffer.ContainsKey(sequenceNumber))
            {
                result.DropReason = Vp8PacketDropReason.Duplicate;
                result.BufferedPacketCount = _buffer.Count;
                return result;
            }

            if (_hasExpectedSequence && GetForwardDistance(_expectedSequenceNumber, sequenceNumber) > MaxBufferedPackets)
            {
                ResetToFrameBoundary();
                result.FrameResetRequired = true;
                result.DropReason = Vp8PacketDropReason.Resync;
                if (isStartPacket)
                {
                    StartFrom(sequenceNumber);
                    ReleasePacket(packet, utcNow, result);
                }

                return result;
            }

            _buffer[sequenceNumber] = packet;
            if (_gapDetectedAtUtc == DateTime.MinValue)
            {
                _gapDetectedAtUtc = utcNow;
            }

            if (_buffer.Count > MaxBufferedPackets || utcNow - _gapDetectedAtUtc >= GapTimeout)
            {
                ResetToFrameBoundary();
                result.FrameResetRequired = true;
                result.DropReason = Vp8PacketDropReason.Resync;
                if (isStartPacket)
                {
                    StartFrom(sequenceNumber);
                    ReleasePacket(packet, utcNow, result);
                }

                return result;
            }

            result.BufferedPacketCount = _buffer.Count;
            return result;
        }

        public void ResetToFrameBoundary()
        {
            _buffer.Clear();
            _gapDetectedAtUtc = DateTime.MinValue;
            _hasExpectedSequence = false;
            _awaitingFrameStart = true;
        }

        private void StartFrom(ushort sequenceNumber)
        {
            _expectedSequenceNumber = sequenceNumber;
            _hasExpectedSequence = true;
            _awaitingFrameStart = false;
            _gapDetectedAtUtc = DateTime.MinValue;
        }

        private void ReleasePacket(RTPPacket packet, DateTime utcNow, Vp8PacketReorderResult result)
        {
            result.ReleasedPackets.Add(packet);
            _expectedSequenceNumber = Increment(packet.Header.SequenceNumber);

            while (_buffer.TryGetValue(_expectedSequenceNumber, out var nextPacket))
            {
                _buffer.Remove(_expectedSequenceNumber);
                result.ReleasedPackets.Add(nextPacket);
                _expectedSequenceNumber = Increment(nextPacket.Header.SequenceNumber);
            }

            _gapDetectedAtUtc = _buffer.Count > 0 ? utcNow : DateTime.MinValue;
            result.BufferedPacketCount = _buffer.Count;
        }

        private static ushort Increment(int sequenceNumber) => unchecked((ushort)(sequenceNumber + 1));

        private static ushort GetForwardDistance(ushort expected, ushort actual) => unchecked((ushort)(actual - expected));

        private static bool IsSequenceBehind(ushort actual, ushort expected)
        {
            ushort distance = unchecked((ushort)(actual - expected));
            return distance != 0 && distance >= 32768;
        }

        private static bool IsVp8StartPacket(RTPPacket packet)
        {
            if (packet?.Payload == null || packet.Payload.Length == 0)
            {
                return false;
            }

            byte descriptor = packet.Payload[0];
            return (descriptor & 0x10) != 0 && (descriptor & 0x07) == 0;
        }
    }
}
