namespace LossDetection
{
    public interface ILossHandler
    {
        void OnPacketLost(ushort peerId, PacketData data);
    }
}