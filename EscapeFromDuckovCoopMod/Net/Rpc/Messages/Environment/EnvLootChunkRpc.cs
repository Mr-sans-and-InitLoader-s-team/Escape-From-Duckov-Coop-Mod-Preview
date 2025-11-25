using System;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ENV_LOOT_CHUNK, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct EnvLootChunkRpc : IRpcMessage
{
    public bool Reset;
    public int[] Keys;
    public bool[] States;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Reset);
        var count = 0;
        if (Keys != null && States != null)
            count = Math.Min(Keys.Length, States.Length);

        writer.Put((ushort)count);
        for (var i = 0; i < count; i++)
        {
            writer.Put(Keys[i]);
            writer.Put(States[i]);
        }
    }

    public void Deserialize(NetPacketReader reader)
    {
        Reset = reader.GetBool();
        var count = reader.GetUShort();
        Keys = new int[count];
        States = new bool[count];

        for (var i = 0; i < count; i++)
        {
            Keys[i] = reader.GetInt();
            States[i] = reader.GetBool();
        }
    }
}
