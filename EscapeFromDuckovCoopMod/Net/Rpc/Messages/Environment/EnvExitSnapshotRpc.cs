using System;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ENV_EXIT_SNAPSHOT, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct EnvExitSnapshotRpc : IRpcMessage
{
    public bool Reset;
    public string SceneId;
    public ExitSnapshotEntry[] Entries;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Reset);
        writer.Put(SceneId ?? string.Empty);

        var count = Entries?.Length ?? 0;
        writer.Put(count);
        for (var i = 0; i < count; i++)
        {
            Entries[i].Serialize(writer);
        }
    }

    public void Deserialize(NetPacketReader reader)
    {
        Reset = reader.GetBool();
        SceneId = reader.GetString();
        var count = reader.GetInt();

        if (count <= 0)
        {
            Entries = Array.Empty<ExitSnapshotEntry>();
            return;
        }

        Entries = new ExitSnapshotEntry[count];
        for (var i = 0; i < count; i++)
        {
            Entries[i].Deserialize(reader);
        }
    }
}

public struct ExitSnapshotEntry
{
    public Vector3 Position;
    public int SceneBuildIndex;

    public void Serialize(NetDataWriter writer)
    {
        writer.PutV3cm(Position);
        writer.Put(SceneBuildIndex);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Position = reader.GetV3cm();
        SceneBuildIndex = reader.GetInt();
    }
}
