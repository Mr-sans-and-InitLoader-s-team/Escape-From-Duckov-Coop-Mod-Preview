using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public struct PlayerStatusPayload
{
    public string PlayerId;
    public string PlayerName;
    public int Latency;
    public bool IsInGame;
    public Vector3 Position;
    public Quaternion Rotation;
    public string SceneId;
    public string CustomFaceJson;
    public EquipmentSyncData[] Equipment;
    public WeaponSyncData[] Weapons;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(PlayerId ?? string.Empty);
        writer.Put(PlayerName ?? string.Empty);
        writer.Put(Latency);
        writer.Put(IsInGame);
        writer.PutVector3(Position);
        writer.PutQuaternion(Rotation);

        writer.Put(SceneId ?? string.Empty);
        writer.Put(CustomFaceJson ?? string.Empty);

        var equipmentCount = Equipment?.Length ?? 0;
        writer.Put(equipmentCount);
        if (equipmentCount > 0)
        {
            for (var i = 0; i < equipmentCount; i++)
                Equipment[i].Serialize(writer);
        }

        var weaponCount = Weapons?.Length ?? 0;
        writer.Put(weaponCount);
        if (weaponCount > 0)
        {
            for (var i = 0; i < weaponCount; i++)
                Weapons[i].Serialize(writer);
        }
    }

    public void Deserialize(NetPacketReader reader)
    {
        PlayerId = reader.GetString();
        PlayerName = reader.GetString();
        Latency = reader.GetInt();
        IsInGame = reader.GetBool();
        Position = reader.GetVector3();
        Rotation = reader.GetQuaternion();

        SceneId = reader.GetString();
        CustomFaceJson = reader.GetString();

        var equipmentCount = reader.GetInt();
        Equipment = new EquipmentSyncData[equipmentCount];
        for (var i = 0; i < equipmentCount; i++)
            Equipment[i] = EquipmentSyncData.Deserialize(reader);

        var weaponCount = reader.GetInt();
        Weapons = new WeaponSyncData[weaponCount];
        for (var i = 0; i < weaponCount; i++)
            Weapons[i] = WeaponSyncData.Deserialize(reader);
    }
}

[Rpc(Op.PLAYER_STATUS_UPDATE, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct PlayerStatusUpdateRpc : IRpcMessage
{
    public PlayerStatusPayload[] Players;

    public void Serialize(NetDataWriter writer)
    {
        var count = Players?.Length ?? 0;
        writer.Put(count);
        for (var i = 0; i < count; i++)
            Players[i].Serialize(writer);
    }

    public void Deserialize(NetPacketReader reader)
    {
        var count = reader.GetInt();
        Players = new PlayerStatusPayload[count];
        for (var i = 0; i < count; i++)
        {
            Players[i] = new PlayerStatusPayload();
            Players[i].Deserialize(reader);
        }
    }
}

[Rpc(Op.CLIENT_STATUS_UPDATE, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct ClientStatusUpdateRpc : IRpcMessage
{
    public PlayerStatusPayload Player;

    public void Serialize(NetDataWriter writer)
    {
        Player.Serialize(writer);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Player = new PlayerStatusPayload();
        Player.Deserialize(reader);
    }
}
