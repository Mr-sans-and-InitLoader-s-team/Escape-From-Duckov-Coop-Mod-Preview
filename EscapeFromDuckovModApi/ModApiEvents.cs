using ItemStatsSystem;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

/// <summary>
/// Exposed gameplay hooks for other mods. Events are raised by the coop mod and
/// deliver runtime objects so external mods can observe or extend behavior.
/// </summary>
public static class ModApiEvents
{
    /// <summary>
    /// Invoked when a player character is present in the scene (local or remote).
    /// string playerId: network id returned by NetService.GetPlayerId / GetSelfNetworkId.
    /// bool isLocal: true for the local player on this machine.
    /// </summary>
    public static event Action<CharacterMainControl, string, bool> PlayerSpawned;

    /// <summary>
    /// Invoked when an AI replica is created locally (client-side view).
    /// int id: AI entry id from CoopSyncDatabase.AI.
    /// </summary>
    public static event Action<int, CharacterMainControl> AiSpawned;

    /// <summary>
    /// Invoked when a projectile is spawned locally (visual instance).
    /// shooterId: player id string when available; can be null/empty for AI or unknown sources.
    /// </summary>
    public static event Action<Projectile, ProjectileContext, string> ProjectileSpawned;

    /// <summary>
    /// Invoked when an environment destructible (HealthSimpleBase) is registered.
    /// </summary>
    public static event Action<HealthSimpleBase, uint> DestructibleRegistered;

    /// <summary>
    /// Invoked on the host when an AI is registered into the sync database.
    /// Provides both the AI controller and key metadata for the registered entry.
    /// </summary>
    public static event Action<ServerRegisterAiEvent> ServerRegisterAI;

    /// <summary>
    /// Invoked when building an ItemSnapshot so mods can add custom data.
    /// </summary>
    public static event Action<Item, Dictionary<string, string>> ItemSnapshotCustomDataRequested;

    /// <summary>
    /// Invoked after applying an ItemSnapshot so mods can consume custom data.
    /// </summary>
    public static event Action<Item, IReadOnlyDictionary<string, string>> ItemSnapshotCustomDataApplied;

    public static void RaisePlayerSpawned(CharacterMainControl cmc, string playerId, bool isLocal)
    {
        if (!cmc) return;
        PlayerSpawned?.Invoke(cmc, playerId, isLocal);
    }

    public static void RaiseAiSpawned(int id, CharacterMainControl cmc)
    {
        if (!cmc) return;
        AiSpawned?.Invoke(id, cmc);
    }

    public static void RaiseProjectileSpawned(Projectile projectile, ProjectileContext context, string shooterId)
    {
        if (!projectile) return;
        ProjectileSpawned?.Invoke(projectile, context, shooterId);
    }

    public static void RaiseDestructibleRegistered(HealthSimpleBase hs, uint id)
    {
        if (!hs) return;
        DestructibleRegistered?.Invoke(hs, id);
    }

    public static void RaiseServerRegisterAI(AICharacterController controller, AISyncEntry entry)
    {
        if (!controller || entry == null) return;
        var payload = new ServerRegisterAiEvent(
            controller,
            entry.Id,
            entry.SpawnerGuid,
            entry.PositionKey,
            entry.SpawnPosition,
            entry.SpawnRotation,
            entry.ModelName,
            entry.CustomFaceJson,
            entry.CharacterPresetKey,
            entry.HideIfFoundEnemyName,
            entry.Activated,
            entry.ScenePath,
            entry.SceneBuildIndex,
            entry.Team,
            entry.Status,
            entry.MaxHealth,
            entry.CurrentHealth,
            entry.BodyArmor,
            entry.HeadArmor,
            entry.LastKnownPosition,
            entry.LastKnownRotation,
            entry.LastKnownVelocity,
            entry.LastKnownRemoteTime,
            entry.LastStateSentTime,
            entry.LastStateReceivedTime,
            entry.ServerDeathHandled,
            entry.Equipment,
            entry.Weapons,
            entry.WeaponSnapshots,
            entry.Buffs);
        ServerRegisterAI?.Invoke(payload);
    }

    public static Dictionary<string, string> RaiseItemSnapshotCustomDataRequested(Item item)
    {
        if (!item) return null;
        if (ItemSnapshotCustomDataRequested == null) return null;
        var payload = new Dictionary<string, string>(StringComparer.Ordinal);
        ItemSnapshotCustomDataRequested?.Invoke(item, payload);
        return payload.Count > 0 ? payload : null;
    }

    public static void RaiseItemSnapshotCustomDataApplied(Item item, IReadOnlyDictionary<string, string> data)
    {
        if (!item || data == null || data.Count == 0) return;
        ItemSnapshotCustomDataApplied?.Invoke(item, data);
    }
}

public readonly struct ServerRegisterAiEvent
{
    public ServerRegisterAiEvent(
        AICharacterController controller,
        int id,
        int spawnerGuid,
        int positionKey,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        string modelName,
        string customFaceJson,
        string characterPresetKey,
        string hideIfFoundEnemyName,
        bool activated,
        string scenePath,
        int sceneBuildIndex,
        Teams team,
        AIStatus status,
        float maxHealth,
        float currentHealth,
        float bodyArmor,
        float headArmor,
        Vector3 lastKnownPosition,
        Quaternion lastKnownRotation,
        Vector3 lastKnownVelocity,
        double lastKnownRemoteTime,
        float lastStateSentTime,
        float lastStateReceivedTime,
        bool serverDeathHandled,
        Dictionary<string, int> equipment,
        Dictionary<string, int> weapons,
        Dictionary<string, ItemSnapshot> weaponSnapshots,
        List<AIBuffState> buffs)
    {
        Controller = controller;
        EntryId = id;
        SpawnerGuid = spawnerGuid;
        PositionKey = positionKey;
        SpawnPosition = spawnPosition;
        SpawnRotation = spawnRotation;
        ModelName = modelName;
        CustomFaceJson = customFaceJson;
        CharacterPresetKey = characterPresetKey;
        HideIfFoundEnemyName = hideIfFoundEnemyName;
        Activated = activated;
        ScenePath = scenePath;
        SceneBuildIndex = sceneBuildIndex;
        Team = team;
        Status = status;
        MaxHealth = maxHealth;
        CurrentHealth = currentHealth;
        BodyArmor = bodyArmor;
        HeadArmor = headArmor;
        LastKnownPosition = lastKnownPosition;
        LastKnownRotation = lastKnownRotation;
        LastKnownVelocity = lastKnownVelocity;
        LastKnownRemoteTime = lastKnownRemoteTime;
        LastStateSentTime = lastStateSentTime;
        LastStateReceivedTime = lastStateReceivedTime;
        ServerDeathHandled = serverDeathHandled;
        Equipment = equipment != null ? new Dictionary<string, int>(equipment) : new Dictionary<string, int>();
        Weapons = weapons != null ? new Dictionary<string, int>(weapons) : new Dictionary<string, int>();
        WeaponSnapshots = weaponSnapshots != null ? new Dictionary<string, ItemSnapshot>(weaponSnapshots) : new Dictionary<string, ItemSnapshot>();
        Buffs = buffs != null ? new List<AIBuffState>(buffs) : new List<AIBuffState>();
    }

    public AICharacterController Controller { get; }
    public int EntryId { get; }
    public int SpawnerGuid { get; }
    public int PositionKey { get; }
    public string ModelName { get; }
    public string CustomFaceJson { get; }
    public string CharacterPresetKey { get; }
    public string HideIfFoundEnemyName { get; }
    public bool Activated { get; }
    public string ScenePath { get; }
    public int SceneBuildIndex { get; }
    public Teams Team { get; }
    public AIStatus Status { get; }
    public float MaxHealth { get; }
    public float CurrentHealth { get; }
    public float BodyArmor { get; }
    public float HeadArmor { get; }
    public Vector3 SpawnPosition { get; }
    public Quaternion SpawnRotation { get; }
    public Vector3 LastKnownPosition { get; }
    public Quaternion LastKnownRotation { get; }
    public Vector3 LastKnownVelocity { get; }
    public double LastKnownRemoteTime { get; }
    public float LastStateSentTime { get; }
    public float LastStateReceivedTime { get; }
    public bool ServerDeathHandled { get; }
    public IReadOnlyDictionary<string, int> Equipment { get; }
    public IReadOnlyDictionary<string, int> Weapons { get; }
    public IReadOnlyDictionary<string, ItemSnapshot> WeaponSnapshots { get; }
    public IReadOnlyList<AIBuffState> Buffs { get; }
}
