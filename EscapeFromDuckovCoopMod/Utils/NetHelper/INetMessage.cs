namespace EscapeFromDuckovCoopMod.Utils.NetHelper
{
    public interface INetMessage
    {
        void PutToWriter(NetDataWriter writer);

        void GetFromReader(NetDataReader reader);
    }
}
