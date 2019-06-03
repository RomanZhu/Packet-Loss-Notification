#if LOSS_DETECTOR_DEBUG
using System.Text;
using Sources.Tools;
#endif
using System;
using System.Runtime.InteropServices;
using ENet;
using NetStack.Serialization;
using RingBuffer;

namespace LossDetection
{
    public class LossDetector
    {
        private readonly ILossHandler _handler;

        private readonly SequenceValues[]         _sequences;
        private readonly RingBuffer<PacketData>[] _packetDataBuffers;
        private readonly RingBuffer<LostData>     _lostPackets;

        private readonly ushort _maxPeerCount;

        public LossDetector(ILossHandler handler, ushort maxPeerCount, ushort ackWindow)
        {
            _maxPeerCount = maxPeerCount;

            _sequences         = new SequenceValues[_maxPeerCount];
            _packetDataBuffers = new RingBuffer<PacketData>[_maxPeerCount];
            _lostPackets       = new RingBuffer<LostData>(_maxPeerCount * ackWindow, false);

            _handler = handler;

            for (var i = 0; i < _maxPeerCount; i++)
            {
                _packetDataBuffers[i] = new RingBuffer<PacketData>(ackWindow, false);
            }
        }

        public void AddPeer(Peer peer)
        {
            _sequences[peer.ID] = new SequenceValues();
        }

        public void RemovePeer(Peer peer)
        {
            var buffer = _packetDataBuffers[peer.ID];
            while (buffer.Count > 0)
            {
                var data = buffer.Pop();

                _lostPackets.Push(new LostData{PeerId = (ushort)peer.ID, Data = data});
            }
        }


        /// <returns>False if you exceeded ACK window and should drop connection.</returns>
        public bool EnqueueData(Peer peer, PacketData data)
        {
            if (_packetDataBuffers[peer.ID].IsFull)
            {
                return false;
            }

            _packetDataBuffers[peer.ID].Push(data);
            return true;
        }

        public ushort AddHeaderForPeerId(ushort peerId, BitBuffer data)
        {
            var sequence = _sequences[peerId];
            sequence.SentId    = (ushort) (sequence.SentId + 1);
            _sequences[peerId] = sequence;

            data.AddUShort(sequence.SentId);
            data.AddUShort(sequence.ReceivedId);
            data.AddUInt(sequence.Bitmask);
            return sequence.SentId;
        }

        /// <returns>True if new sequence number is bigger than last one</returns>
        public bool ReadHeaderOfPeerId(ushort peerId, BitBuffer data)
        {
            var peerSentId     = data.ReadUShort();
            var peerReceivedId = data.ReadUShort();
            var peerBitmask    = data.ReadUInt();

            var lastValue      = _sequences[peerId];
            var lastReceivedId = lastValue.ReceivedId;
            var lastBitmask    = lastValue.Bitmask;

            var distance = SequenceDistance(lastReceivedId, peerSentId);

            if (SequenceFirstIsGreater(peerSentId, lastReceivedId))
            {
                lastReceivedId = peerSentId;

                if (distance >= 32)
                {
                    lastBitmask = 0;
                }
                else
                {
                    lastBitmask <<= distance;
                    lastBitmask |=  (uint) (1 << distance - 1);
                }

                lastValue.Bitmask    = lastBitmask;
                lastValue.ReceivedId = lastReceivedId;
                _sequences[peerId]   = lastValue;

                var ring = _packetDataBuffers[peerId];

                while (ring.Count > 0)
                {
                    var packetData    = ring[0];
                    var packetState = GetPacketState(packetData.SequenceId, peerReceivedId, peerBitmask);

                    if (packetState == PacketState.Flying)
                        break;

                    switch (packetState)
                    {
                        case PacketState.Arrived:

                            ring.Pop();

                            if (packetData.Data != new IntPtr())
                                Marshal.FreeHGlobal(packetData.Data);

                            break;
                        case PacketState.ProbablyLost:

                            ring.Pop();

                            _lostPackets.Push(new LostData{PeerId = peerId, Data = packetData});

#if LOSS_DETECTOR_DEBUG
                            var str = "";
                            for (int j = 0; j < 32; j++)
                            {
                                str += (peerBitmask & (1 << j)) == 0 ? '0' : '1';
                            }

                            Logger.I.Log(this, $"{peerReceivedId} {str}");
                            Logger.I.Log(this, $"Lost Seq#: {packetData.SequenceId} for Peer#: {peerId} using ACK data:");
#endif

                            break;
                    }
                }

                return true;
            }

            return false;
        }

        public void ExecuteLostPackets()
        {
            while (_lostPackets.Count>0)
            {
                var lost = _lostPackets.Pop();
                _handler.OnPacketLost(lost.PeerId, lost.Data);
                
                if(lost.Data.Data!=new IntPtr())
                    Marshal.FreeHGlobal(lost.Data.Data);
            }
        }

        public void ClearHeader(BitBuffer data)
        {
            data.ReadUShort();
            data.ReadUShort();
            data.ReadUInt();
        }

#if LOSS_DETECTOR_DEBUG
        public void GetDebugString(StringBuilder builder)
        {
            for (ushort i = 0; i < _maxPeerCount; i++)
            {
                GetDebugString(i, builder);
            }
        }

        public void GetDebugString(ushort peerId, StringBuilder builder)
        {
            var seq = _sequences[peerId];

            builder.Append($"{seq.SentId:00000} {seq.ReceivedId:00000} {_packetDataBuffers[peerId].Count:000} \n");
            for (int j = 0; j < 32; j++)
            {
                builder.Append((seq.Bitmask & (1 << j)) == 0 ? '0' : '1');
            }

            builder.Append('\n');
        }
#endif

        private PacketState GetPacketState(ushort packetId, ushort lastReceivedPacketId, uint bitmask)
        {
            if (SequenceFirstIsGreater(packetId, lastReceivedPacketId))
            {
                return PacketState.Flying;
            }

            if (packetId == lastReceivedPacketId)
            {
                return PacketState.Arrived;
            }

            if (packetId + 32 < lastReceivedPacketId)
            {
                return PacketState.ProbablyLost;
            }

            var bitNumber = SequenceDistance(packetId, lastReceivedPacketId);
            var received  = (bitmask & (1 << bitNumber - 1)) != 0;

            return received ? PacketState.Arrived : PacketState.ProbablyLost;
        }

        private bool SequenceFirstIsGreater(ushort v1, ushort v2)
        {
            return v1 > v2 && v1 - v2 <= 32768 || v1 < v2 && v2 - v1 > 32768;
        }

        private ushort SequenceDistance(ushort v1, ushort v2)
        {
            var greater  = v1 >= v2 ? v1 : v2;
            var smaller  = v1 >= v2 ? v2 : v1;
            var distance = (ushort) (greater - smaller);
            if (distance > 32768)
            {
                return (ushort) (65535 - greater + smaller);
            }

            return distance;
        }

        private struct SequenceValues
        {
            public ushort SentId;
            public ushort ReceivedId;
            public uint   Bitmask;
        }
        
        private struct LostData
        {
            public ushort     PeerId;
            public PacketData Data;
        }

        private enum PacketState
        {
            Flying,
            Arrived,
            ProbablyLost
        }
    }

    public struct PacketData
    {
        public ushort SequenceId;
        public IntPtr Data;
        public int    Length;
    }
}