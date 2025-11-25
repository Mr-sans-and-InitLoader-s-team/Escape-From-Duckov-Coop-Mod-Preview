using System;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ENV_DOOR_CHUNK, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct EnvDoorChunkRpc : IRpcMessage
{
    public bool Reset;
    public int[] Keys;
    public bool[] ClosedStates;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Reset);
        var count = 0;
        if (Keys != null && ClosedStates != null)
            count = Math.Min(Keys.Length, ClosedStates.Length);

        writer.Put((ushort)count);
        for (var i = 0; i < count; i++)
        {
            writer.Put(Keys[i]);
            writer.Put(ClosedStates[i]);
        }
    }

    public void Deserialize(NetPacketReader reader)
    {
        Reset = reader.GetBool();
        var count = reader.GetUShort();
        Keys = new int[count];
        ClosedStates = new bool[count];

        for (var i = 0; i < count; i++)
        {
            Keys[i] = reader.GetInt();
            ClosedStates[i] = reader.GetBool();
        }
    }
}
