using System;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.AI_SNAPSHOT_CHUNK, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct AISnapshotChunkRpc : IRpcMessage
{
    public bool Reset;
    public AISnapshotEntry[] Entries;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Reset);
        var count = Entries?.Length ?? 0;
        writer.Put((ushort)count);
        for (var i = 0; i < count; i++)
            Entries[i].Serialize(writer);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Reset = reader.GetBool();
        var count = reader.GetUShort();
        Entries = count > 0 ? new AISnapshotEntry[count] : Array.Empty<AISnapshotEntry>();
        for (var i = 0; i < count; i++)
        {
            Entries[i].Deserialize(reader);
        }
    }
}

public struct AISnapshotEntry
{
    public int Id;
    public int SceneBuildIndex;
    public string ScenePath;
    public Vector3 SpawnPosition;
    public Quaternion SpawnRotation;
    public string ModelName;
    public string CustomFaceJson;
    public string CharacterPresetKey;
    public string HideIfFoundEnemyName;
    public AIStatus Status;
    public bool Activated;
    public Teams Team;
    public float MaxHealth;
    public float CurrentHealth;
    public float BodyArmor;
    public float HeadArmor;
    public string[] EquipmentSlots;
    public int[] EquipmentItemTypeIds;
    public string[] WeaponSlots;
    public int[] WeaponItemTypeIds;
    public int[] BuffWeaponTypeIds;
    public int[] BuffIds;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Id);
        writer.Put(SceneBuildIndex);
        writer.Put(ScenePath ?? string.Empty);
        writer.PutVector3(SpawnPosition);
        writer.PutQuaternion(SpawnRotation);
        writer.Put(ModelName ?? string.Empty);
        writer.Put(CustomFaceJson ?? string.Empty);
        writer.Put(CharacterPresetKey ?? string.Empty);
        writer.Put(HideIfFoundEnemyName ?? string.Empty);
        writer.Put((byte)Status);
        writer.Put(Activated);
        writer.Put((byte)Team);
        writer.Put(MaxHealth);
        writer.Put(CurrentHealth);
        writer.Put(BodyArmor);
        writer.Put(HeadArmor);

        WriteArray(writer, EquipmentSlots, EquipmentItemTypeIds);
        WriteArray(writer, WeaponSlots, WeaponItemTypeIds);
        WriteIntArray(writer, BuffWeaponTypeIds, BuffIds);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id = reader.GetInt();
        SceneBuildIndex = reader.GetInt();
        ScenePath = reader.GetString();
        SpawnPosition = reader.GetVector3();
        SpawnRotation = reader.GetQuaternion();
        ModelName = reader.GetString();
        CustomFaceJson = reader.GetString();
        CharacterPresetKey = reader.GetString();
        HideIfFoundEnemyName = reader.GetString();
        Status = (AIStatus)reader.GetByte();
        Activated = reader.GetBool();
        Team = (Teams)reader.GetByte();
        MaxHealth = reader.GetFloat();
        CurrentHealth = reader.GetFloat();
        BodyArmor = reader.GetFloat();
        HeadArmor = reader.GetFloat();

        ReadArray(reader, out EquipmentSlots, out EquipmentItemTypeIds);
        ReadArray(reader, out WeaponSlots, out WeaponItemTypeIds);
        ReadIntArray(reader, out BuffWeaponTypeIds, out BuffIds);
    }

    private static void WriteArray(NetDataWriter writer, string[] slots, int[] values)
    {
        var count = 0;
        if (slots != null && values != null)
            count = Math.Min(slots.Length, values.Length);
        writer.Put((ushort)count);
        for (var i = 0; i < count; i++)
        {
            writer.Put(slots[i] ?? string.Empty);
            writer.Put(values[i]);
        }
    }

    private static void ReadArray(NetPacketReader reader, out string[] slots, out int[] values)
    {
        var count = reader.GetUShort();
        slots = count > 0 ? new string[count] : Array.Empty<string>();
        values = count > 0 ? new int[count] : Array.Empty<int>();
        for (var i = 0; i < count; i++)
        {
            slots[i] = reader.GetString();
            values[i] = reader.GetInt();
        }
    }

    private static void WriteIntArray(NetDataWriter writer, int[] left, int[] right)
    {
        var count = 0;
        if (left != null && right != null)
            count = Math.Min(left.Length, right.Length);
        writer.Put((ushort)count);
        for (var i = 0; i < count; i++)
        {
            writer.Put(left[i]);
            writer.Put(right[i]);
        }
    }

    private static void ReadIntArray(NetPacketReader reader, out int[] left, out int[] right)
    {
        var count = reader.GetUShort();
        left = count > 0 ? new int[count] : Array.Empty<int>();
        right = count > 0 ? new int[count] : Array.Empty<int>();
        for (var i = 0; i < count; i++)
        {
            left[i] = reader.GetInt();
            right[i] = reader.GetInt();
        }
    }
}
