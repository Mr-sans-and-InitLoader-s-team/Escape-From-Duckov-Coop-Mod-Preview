// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspicuous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EscapeFromDuckovCoopMod;

public static class CoopSyncDatabase
{
    public static LootSyncRegistry Loot { get; } = new();

    public static DropSyncRegistry Drops { get; } = new();

    public static EnvironmentSyncRegistry Environment { get; } = new();

    public static AISyncRegistry AI { get; } = new();
}

public sealed class EnvironmentSyncRegistry
{
    public DoorSyncRegistry Doors { get; } = new();

    public DestructibleSyncRegistry Destructibles { get; } = new();

    public ExplosiveOilBarrelSyncRegistry ExplosiveOilBarrels { get; } = new();

    public void Clear()
    {
        Doors.Clear();
        Destructibles.Clear();
        ExplosiveOilBarrels.Clear();
    }
}

public sealed class AISyncRegistry
{
    private readonly Dictionary<int, AISyncEntry> _entries = new();
    private readonly Dictionary<AICharacterController, AISyncEntry> _byController = new();
    private readonly Dictionary<int, AISyncEntry> _byPositionKey = new();

    public IEnumerable<AISyncEntry> Entries => _entries.Values;

    public AISyncEntry RegisterController(AICharacterController controller)
    {
        if (!controller) return null;

        var id = ComputeStableId(controller);
        if (id == 0)
            id = controller.GetInstanceID();

        var entry = GetOrCreate(id);
        entry.Controller = controller;
        entry.Buffs.Clear();
        var scene = controller.gameObject.scene;
        entry.SceneBuildIndex = scene.buildIndex;
        entry.ScenePath = scene.path;
        entry.SpawnerGuid = controller.group && controller.group.spawnerRoot != null
            ? controller.group.spawnerRoot.SpawnerGuid
            : 0;

        var cmc = controller.CharacterMainControl;
        if (cmc)
            entry.Team = cmc.Team;
        entry.SpawnPosition = controller.transform.position;
        entry.SpawnRotation = cmc && cmc.characterModel
            ? cmc.characterModel.transform.rotation
            : controller.transform.rotation;
        entry.PositionKey = ComputePositionKey(entry.SpawnPosition, entry.SceneBuildIndex, entry.ScenePath);
        entry.Status = AIStatus.Active;
        if (cmc && cmc.characterPreset)
            entry.CharacterPresetKey = cmc.characterPreset.nameKey;

        _byController[controller] = entry;
        if (entry.PositionKey != 0)
            _byPositionKey[entry.PositionKey] = entry;

        return entry;
    }

    public AISyncEntry GetOrCreate(int id)
    {
        if (!_entries.TryGetValue(id, out var entry) || entry == null)
        {
            entry = new AISyncEntry { Id = id };
            _entries[id] = entry;
        }

        return entry;
    }

    public bool TryGet(int id, out AISyncEntry entry)
    {
        return _entries.TryGetValue(id, out entry) && entry != null;
    }

    public bool TryGet(AICharacterController controller, out AISyncEntry entry)
    {
        entry = null;
        return controller && _byController.TryGetValue(controller, out entry) && entry != null;
    }

    public bool TryGetByPosition(Vector3 worldPosition, out AISyncEntry entry)
    {
        entry = null;
        var scene = SceneManager.GetActiveScene();
        var key = ComputePositionKey(worldPosition, scene.buildIndex, scene.path);
        return key != 0 && _byPositionKey.TryGetValue(key, out entry) && entry != null;
    }

    public void RemoveController(AICharacterController controller)
    {
        if (!controller) return;

        if (_byController.TryGetValue(controller, out var entry) && entry != null)
        {
            if (entry.Controller == controller)
                entry.Controller = null;

            _byController.Remove(controller);
        }
    }

    public void Clear()
    {
        _entries.Clear();
        _byController.Clear();
        _byPositionKey.Clear();
    }

    public static int ComputeStableId(AICharacterController controller)
    {
        if (!controller) return 0;

        var guid = controller.group && controller.group.spawnerRoot != null
            ? controller.group.spawnerRoot.SpawnerGuid
            : 0;

        var sceneKey = ComputeSceneKey(controller.gameObject.scene);

        var pos = controller.transform.position;
        var posKey = ComputePositionKey(pos, sceneKey, controller.gameObject.scene.path);

        if (guid == 0 && posKey == 0)
            return 0;

        unchecked
        {
            var hash = guid;
            hash = (hash * 486187739) ^ posKey;
            hash = (hash * 486187739) ^ sceneKey;
            hash = (hash * 486187739) ^ controller.name.GetHashCode();
            if (hash == 0)
                hash = controller.GetInstanceID();
            return hash;
        }
    }

    public static int ComputePositionKey(Vector3 position, int sceneBuildIndex, string scenePath)
    {
        var scaled = position * 10f;
        var key = new Vector3Int(
            Mathf.RoundToInt(scaled.x),
            Mathf.RoundToInt(scaled.y),
            Mathf.RoundToInt(scaled.z));
        unchecked
        {
            var hash = $"AI_{key}".GetHashCode();
            hash = (hash * 486187739) ^ sceneBuildIndex;
            if (!string.IsNullOrEmpty(scenePath))
                hash = (hash * 486187739) ^ scenePath.GetHashCode();
            return hash;
        }
    }

    private static int ComputeSceneKey(Scene scene)
    {
        try
        {
            if (scene.IsValid())
            {
                if (scene.buildIndex >= 0) return scene.buildIndex;
                if (!string.IsNullOrEmpty(scene.path)) return scene.path.GetHashCode();
            }
        }
        catch
        {
        }

        return 0;
    }
}

public enum AIStatus : byte
{
    Dormant = 0,
    Spawning = 1,
    Active = 2,
    Despawned = 3,
    Dead = 4
}

public struct AIBuffState
{
    public int WeaponTypeId;
    public int BuffId;
}

public sealed class AISyncEntry
{
    public int Id { get; set; }
    public int SpawnerGuid { get; set; }
    public int PositionKey { get; set; }
    public Vector3 SpawnPosition { get; set; }
    public Quaternion SpawnRotation { get; set; } = Quaternion.identity;
    public string ModelName { get; set; }
    public string CustomFaceJson { get; set; }
    public string CharacterPresetKey { get; set; }
    public string HideIfFoundEnemyName { get; set; }
    public bool Activated { get; set; } = true;
    public string ScenePath { get; set; }
    public int SceneBuildIndex { get; set; }
    public Teams Team { get; set; } = Teams.scav;
    public AIStatus Status { get; set; }
    public float MaxHealth { get; set; }
    public float CurrentHealth { get; set; }
    public float BodyArmor { get; set; }
    public float HeadArmor { get; set; }
    public Vector3 LastKnownPosition { get; set; }
    public Quaternion LastKnownRotation { get; set; } = Quaternion.identity;
    public Vector3 LastKnownVelocity { get; set; }
    public double LastKnownRemoteTime { get; set; }
    public float LastStateSentTime { get; set; }
    public float LastStateReceivedTime { get; set; }
    public AICharacterController Controller { get; set; }
    public readonly Dictionary<string, int> Equipment = new(StringComparer.Ordinal);
    public readonly Dictionary<string, int> Weapons = new(StringComparer.Ordinal);
    public readonly List<AIBuffState> Buffs = new();
    public AnimSample LastAnimSample;

    // 记录服务端是否已执行过一次死亡流程（OnDead → 掉落广播），避免重复触发或遗漏
    public bool ServerDeathHandled { get; set; }
}

public sealed class LootSyncRegistry
{
    private readonly Dictionary<int, LootSyncEntry> _byKey = new();
    private readonly Dictionary<int, LootSyncEntry> _byInstanceId = new();
    private readonly Dictionary<int, LootSyncEntry> _byLootUid = new();
    private readonly Dictionary<Inventory, LootSyncEntry> _byInventory =
        new(ReferenceEqualityComparer<Inventory>.Instance);

    public IEnumerable<LootSyncEntry> Entries => _byInventory.Values;

    private static LootBoxLoader ResolveLoader(InteractableLootbox lootbox)
    {
        try
        {
            return lootbox ? lootbox.GetComponent<LootBoxLoader>() : null;
        }
        catch
        {
            return null;
        }
    }

    private LootSyncEntry GetOrCreateByKey(int positionKey)
    {
        if (!_byKey.TryGetValue(positionKey, out var entry) || entry == null)
        {
            entry = new LootSyncEntry();
            _byKey[positionKey] = entry;
        }

        return entry;
    }

    public bool ContainsInventory(Inventory inventory)
    {
        return inventory != null && _byInventory.ContainsKey(inventory);
    }

    public bool TryGetByInventory(Inventory inventory, out LootSyncEntry entry)
    {
        entry = null;
        return inventory != null && _byInventory.TryGetValue(inventory, out entry) && entry != null;
    }

    public bool TryGetByPositionKey(int key, out LootSyncEntry entry)
    {
        return _byKey.TryGetValue(key, out entry) && entry != null;
    }

    public bool TryGetByInstanceId(int instanceId, out LootSyncEntry entry)
    {
        return _byInstanceId.TryGetValue(instanceId, out entry) && entry != null;
    }

    public void Register(InteractableLootbox lootbox, Inventory inventory, int lootUid = -1)
    {
        if (!lootbox || inventory == null) return;

        var positionKey = LootManager.ComputeLootKeyFromPos(lootbox.transform.position);
        var instanceId = lootbox.GetInstanceID();
        var sceneIndex = lootbox.gameObject.scene.buildIndex;
        var worldPosition = lootbox.transform.position;
        var loader = ResolveLoader(lootbox);
        var isWorld = loader != null;

        var tracker = lootbox.GetComponent<LootSyncTracker>();
        if (!tracker)
        {
            tracker = lootbox.gameObject.AddComponent<LootSyncTracker>();
        }
        tracker.Lootbox = lootbox;

        var entry = GetOrCreateByKey(positionKey);
        _byInventory[inventory] = entry;

        var oldKey = entry.PositionKey;
        var oldInstance = entry.InstanceId;
        var oldInventory = entry.Inventory;

        entry.Inventory = inventory;
        entry.Lootbox = lootbox;
        entry.PositionKey = positionKey;
        entry.InstanceId = instanceId;
        entry.SceneIndex = sceneIndex;
        entry.WorldPosition = worldPosition;
        entry.IsWorldLoot = isWorld;
        if (loader)
            entry.Loader = loader;

        if (oldInventory != null && !ReferenceEquals(oldInventory, inventory) &&
            _byInventory.TryGetValue(oldInventory, out var prevInv) && ReferenceEquals(prevInv, entry))
            _byInventory.Remove(oldInventory);

        if (lootUid >= 0)
        {
            var oldUid = entry.LootUid;
            if (oldUid >= 0 && oldUid != lootUid &&
                _byLootUid.TryGetValue(oldUid, out var prevUid) && ReferenceEquals(prevUid, entry))
                _byLootUid.Remove(oldUid);

            entry.LootUid = lootUid;
            _byLootUid[lootUid] = entry;
        }

        if (oldKey != positionKey &&
            _byKey.TryGetValue(oldKey, out var prev) && ReferenceEquals(prev, entry))
            _byKey.Remove(oldKey);

        if (oldInstance != instanceId &&
            _byInstanceId.TryGetValue(oldInstance, out var prevInst) && ReferenceEquals(prevInst, entry))
            _byInstanceId.Remove(oldInstance);

        _byKey[positionKey] = entry;
        _byInstanceId[instanceId] = entry;
    }

    public void RegisterLoader(LootBoxLoader loader)
    {
        if (!loader) return;

        var lootbox = loader.GetComponent<InteractableLootbox>();
        var positionKey = LootManager.ComputeLootKeyFromPos(loader.transform.position);
        var entry = GetOrCreateByKey(positionKey);

        var instanceId = lootbox ? lootbox.GetInstanceID() : loader.GetInstanceID();
        var oldInstance = entry.InstanceId;

        entry.PositionKey = positionKey;
        entry.InstanceId = instanceId;
        entry.SceneIndex = loader.gameObject.scene.buildIndex;
        entry.WorldPosition = loader.transform.position;
        entry.IsWorldLoot = true;
        entry.Loader = loader;

        if (lootbox)
        {
            entry.Lootbox = lootbox;

            var tracker = lootbox.GetComponent<LootSyncTracker>();
            if (!tracker)
            {
                tracker = lootbox.gameObject.AddComponent<LootSyncTracker>();
            }
            tracker.Lootbox = lootbox;

            if (entry.Inventory != null)
                _byInventory[entry.Inventory] = entry;
        }

        if (oldInstance != instanceId &&
            _byInstanceId.TryGetValue(oldInstance, out var prevInst) && ReferenceEquals(prevInst, entry))
            _byInstanceId.Remove(oldInstance);

        _byKey[positionKey] = entry;
        _byInstanceId[instanceId] = entry;
    }

    public void Unregister(InteractableLootbox lootbox)
    {
        if (!lootbox) return;

        var instanceId = lootbox.GetInstanceID();
        if (!_byInstanceId.TryGetValue(instanceId, out var entry) || entry == null) return;

        _byInstanceId.Remove(instanceId);

        if (_byKey.TryGetValue(entry.PositionKey, out var byKey) && ReferenceEquals(byKey, entry))
            _byKey.Remove(entry.PositionKey);

        if (entry.Inventory != null &&
            _byInventory.TryGetValue(entry.Inventory, out var byInv) && ReferenceEquals(byInv, entry))
            _byInventory.Remove(entry.Inventory);

        if (entry.LootUid >= 0 &&
            _byLootUid.TryGetValue(entry.LootUid, out var byUid) && ReferenceEquals(byUid, entry))
            _byLootUid.Remove(entry.LootUid);

        entry.Inventory = null;
        entry.Lootbox = null;
        entry.Loader = null;
    }

    public bool TryGetByLootUid(int lootUid, out LootSyncEntry entry)
    {
        if (lootUid < 0)
        {
            entry = null;
            return false;
        }

        return _byLootUid.TryGetValue(lootUid, out entry) && entry != null;
    }

    public void SetLootUid(Inventory inventory, int lootUid)
    {
        if (inventory == null || lootUid < 0) return;
        if (!_byInventory.TryGetValue(inventory, out var entry) || entry == null) return;

        if (entry.LootUid == lootUid) return;

        if (entry.LootUid >= 0 &&
            _byLootUid.TryGetValue(entry.LootUid, out var prev) && ReferenceEquals(prev, entry))
            _byLootUid.Remove(entry.LootUid);

        entry.LootUid = lootUid;
        _byLootUid[lootUid] = entry;
    }
}

public sealed class DropSyncRegistry
{
    private readonly Dictionary<uint, DropSyncEntry> _byId = new();
    private readonly Dictionary<Item, DropSyncEntry> _byItem =
        new(ReferenceEqualityComparer<Item>.Instance);

    public IEnumerable<DropSyncEntry> Entries => _byId.Values;

    public bool Contains(uint dropId)
    {
        return _byId.ContainsKey(dropId);
    }

    public void Clear()
    {
        _byId.Clear();
        _byItem.Clear();
    }

    public DropSyncEntry Register(uint dropId, Item item)
    {
        if (!_byId.TryGetValue(dropId, out var entry) || entry == null)
        {
            entry = new DropSyncEntry { DropId = dropId };
            _byId[dropId] = entry;
        }

        if (entry.Item != null && entry.Item != item)
            _byItem.Remove(entry.Item);

        entry.Item = item;
        entry.PendingRemoval = false;

        if (item != null)
            _byItem[item] = entry;

        return entry;
    }

    public bool TryGetById(uint dropId, out DropSyncEntry entry)
    {
        return _byId.TryGetValue(dropId, out entry) && entry != null;
    }

    public bool TryGetByItem(Item item, out DropSyncEntry entry)
    {
        entry = null;
        if (item == null)
            return false;

        return _byItem.TryGetValue(item, out entry) && entry != null;
    }

    public void Unregister(uint dropId)
    {
        if (!_byId.TryGetValue(dropId, out var entry) || entry == null)
            return;

        _byId.Remove(dropId);

        if (entry.Item != null)
            _byItem.Remove(entry.Item);
    }
}

public sealed class DropSyncEntry
{
    public uint DropId;
    public Item Item;
    public GameObject Agent;
    public Vector3 Position;
    public Vector3 Direction;
    public float Angle;
    public bool CreateRigidbody;
    public bool PendingRemoval;
}

public sealed class LootSyncEntry
{
    public int PositionKey { get; set; }
    public int InstanceId { get; set; }
    public int SceneIndex { get; set; } = -1;
    public UnityEngine.Vector3 WorldPosition { get; set; }
    public Inventory Inventory { get; set; }
    public InteractableLootbox Lootbox { get; set; }
    public LootBoxLoader Loader { get; set; }
    public bool IsWorldLoot { get; set; }
    public int LootUid { get; set; } = -1;
}

public sealed class DoorSyncRegistry
{
    private readonly Dictionary<int, DoorSyncEntry> _byKey = new();
    private readonly Dictionary<int, DoorSyncEntry> _byInstanceId = new();
    private readonly List<DoorSyncEntry> _entries = new();

    public IReadOnlyList<DoorSyncEntry> Entries => _entries;

    public DoorSyncEntry Register(global::Door door)
    {
        if (!door) return null;

        var instanceId = door.GetInstanceID();
        if (!_byInstanceId.TryGetValue(instanceId, out var entry) || entry == null)
        {
            entry = new DoorSyncEntry();
            _byInstanceId[instanceId] = entry;
            _entries.Add(entry);
        }

        var key = Door.TryGetDoorKey(door);

        if (entry.Key != 0 && entry.Key != key &&
            _byKey.TryGetValue(entry.Key, out var prev) && ReferenceEquals(prev, entry))
            _byKey.Remove(entry.Key);

        entry.Door = door;
        entry.Key = key;
        entry.InstanceId = instanceId;
        entry.SceneIndex = door.gameObject.scene.buildIndex;

        if (key != 0)
            _byKey[key] = entry;

        var tracker = door.GetComponent<DoorSyncTracker>();
        if (!tracker)
        {
            tracker = door.gameObject.AddComponent<DoorSyncTracker>();
        }

        tracker.Door = door;
        tracker.Key = key;

        return entry;
    }

    public bool TryGetDoor(int key, out global::Door door)
    {
        door = null;
        if (!TryGetEntry(key, out var entry)) return false;
        door = entry.Door;
        return door;
    }

    public bool TryGetEntry(int key, out DoorSyncEntry entry)
    {
        entry = null;
        if (key == 0) return false;

        if (_byKey.TryGetValue(key, out entry) && entry != null)
        {
            var door = entry.Door;
            if (door)
                return true;

            _byKey.Remove(key);
            if (entry.InstanceId != 0 &&
                _byInstanceId.TryGetValue(entry.InstanceId, out var prevInst) && ReferenceEquals(prevInst, entry))
                _byInstanceId.Remove(entry.InstanceId);
            _entries.Remove(entry);
        }

        entry = null;
        return false;
    }

    public void Unregister(global::Door door)
    {
        if (!door) return;

        var instanceId = door.GetInstanceID();
        if (!_byInstanceId.TryGetValue(instanceId, out var entry) || entry == null)
            return;

        _byInstanceId.Remove(instanceId);

        if (entry.Key != 0 &&
            _byKey.TryGetValue(entry.Key, out var prev) && ReferenceEquals(prev, entry))
            _byKey.Remove(entry.Key);

        _entries.Remove(entry);
        entry.Door = null;
        entry.Key = 0;
        entry.InstanceId = 0;
        entry.SceneIndex = 0;
    }

    public void Clear()
    {
        _byKey.Clear();
        _byInstanceId.Clear();
        _entries.Clear();
    }
}

public sealed class DoorSyncEntry
{
    public int Key;
    public int InstanceId;
    public int SceneIndex;
    public global::Door Door;
}

[DisallowMultipleComponent]
public sealed class DoorSyncTracker : MonoBehaviour
{
    public global::Door Door;
    public int Key;

    private void Awake()
    {
        if (!Door) Door = GetComponent<global::Door>();
    }

    private void OnDestroy()
    {
        if (Door)
            CoopSyncDatabase.Environment.Doors.Unregister(Door);

        Door = null;
        Key = 0;
    }
}

public sealed class DestructibleSyncRegistry
{
    private readonly Dictionary<uint, DestructibleSyncEntry> _byId = new();
    private readonly Dictionary<int, DestructibleSyncEntry> _byInstanceId = new();
    private readonly List<DestructibleSyncEntry> _entries = new();

    public IReadOnlyList<DestructibleSyncEntry> Entries => _entries;

    public DestructibleSyncEntry Register(uint id, HealthSimpleBase hs)
    {
        if (id == 0 || !hs) return null;

        var instanceId = hs.GetInstanceID();

        if (!_byInstanceId.TryGetValue(instanceId, out var entry) || entry == null)
        {
            if (!_byId.TryGetValue(id, out entry) || entry == null)
            {
                entry = new DestructibleSyncEntry();
                _entries.Add(entry);
            }
        }

        if (entry.Id != id && entry.Id != 0 &&
            _byId.TryGetValue(entry.Id, out var prev) && ReferenceEquals(prev, entry))
            _byId.Remove(entry.Id);

        if (entry.InstanceId != 0 && entry.InstanceId != instanceId &&
            _byInstanceId.TryGetValue(entry.InstanceId, out var prevInst) && ReferenceEquals(prevInst, entry))
            _byInstanceId.Remove(entry.InstanceId);

        entry.Id = id;
        entry.InstanceId = instanceId;
        entry.SceneIndex = hs.gameObject.scene.buildIndex;
        entry.Destructible = hs;

        _byId[id] = entry;
        _byInstanceId[instanceId] = entry;

        var tracker = hs.GetComponent<DestructibleSyncTracker>();
        if (!tracker)
            tracker = hs.gameObject.AddComponent<DestructibleSyncTracker>();

        tracker.Destructible = hs;
        tracker.Id = id;

        return entry;
    }

    public bool TryGet(uint id, out HealthSimpleBase hs)
    {
        hs = null;
        if (!_byId.TryGetValue(id, out var entry) || entry == null)
            return false;

        hs = entry.Destructible;
        if (hs)
            return true;

        _byId.Remove(id);
        if (entry.InstanceId != 0 &&
            _byInstanceId.TryGetValue(entry.InstanceId, out var prevInst) && ReferenceEquals(prevInst, entry))
            _byInstanceId.Remove(entry.InstanceId);
        _entries.Remove(entry);
        return false;
    }

    public void Unregister(uint id, HealthSimpleBase hs)
    {
        if (id == 0) return;
        if (!_byId.TryGetValue(id, out var entry) || entry == null)
            return;

        if (hs != null && entry.Destructible && entry.Destructible != hs)
            return;

        _byId.Remove(id);

        if (entry.InstanceId != 0 &&
            _byInstanceId.TryGetValue(entry.InstanceId, out var prev) && ReferenceEquals(prev, entry))
            _byInstanceId.Remove(entry.InstanceId);

        _entries.Remove(entry);
        entry.Destructible = null;
        entry.InstanceId = 0;
        entry.SceneIndex = 0;
        entry.Id = 0;
    }

    public void Clear()
    {
        _byId.Clear();
        _byInstanceId.Clear();
        _entries.Clear();
    }
}

public sealed class DestructibleSyncEntry
{
    public uint Id;
    public int InstanceId;
    public int SceneIndex;
    public HealthSimpleBase Destructible;
}

public sealed class ExplosiveOilBarrelSyncRegistry
{
    private readonly Dictionary<uint, ExplosiveOilBarrelSyncEntry> _byId = new();
    private readonly List<ExplosiveOilBarrelSyncEntry> _entries = new();

    public IReadOnlyList<ExplosiveOilBarrelSyncEntry> Entries => _entries;

    public ExplosiveOilBarrelSyncEntry Register(GameObject barrel)
    {
        if (!barrel) return null;

        var id = ComputeStableId(barrel);
        if (id == 0) return null;

        if (!_byId.TryGetValue(id, out var entry) || entry == null)
        {
            entry = new ExplosiveOilBarrelSyncEntry();
            _entries.Add(entry);
        }

        entry.Id = id;
        entry.InstanceId = barrel.GetInstanceID();
        entry.SceneIndex = barrel.scene.buildIndex;
        entry.ScenePath = barrel.scene.path;
        entry.Barrel = barrel;

        _byId[id] = entry;

        return entry;
    }

    public bool TryGet(uint id, out ExplosiveOilBarrelSyncEntry entry)
    {
        entry = null;
        if (!_byId.TryGetValue(id, out var existing) || existing == null)
            return false;

        if (!existing.Barrel)
        {
            _byId.Remove(id);
            _entries.Remove(existing);
            return false;
        }

        entry = existing;
        return true;
    }

    public void Clear()
    {
        _byId.Clear();
        _entries.Clear();
    }

    private static uint ComputeStableId(GameObject go)
    {
        if (!go) return 0;

        var name = go.name;
        if (string.IsNullOrEmpty(name)) return 0;

        var idx = name.LastIndexOf('_');
        if (idx >= 0 && idx < name.Length - 1)
        {
            var suffix = name.Substring(idx + 1);
            if (uint.TryParse(suffix, out var parsed) && parsed != 0)
                return parsed;
        }

        var pos = go.transform.position * 100f;
        var rounded = new Vector3Int(
            Mathf.RoundToInt(pos.x),
            Mathf.RoundToInt(pos.y),
            Mathf.RoundToInt(pos.z));

        unchecked
        {
            var hash = 17u;
            hash = hash * 31u + (uint)rounded.x;
            hash = hash * 31u + (uint)rounded.y;
            hash = hash * 31u + (uint)rounded.z;
            hash = hash * 31u + (uint)go.scene.buildIndex;
            return hash;
        }
    }
}

public sealed class ExplosiveOilBarrelSyncEntry
{
    public uint Id;
    public int InstanceId;
    public int SceneIndex;
    public string ScenePath;
    public GameObject Barrel;
}

[DisallowMultipleComponent]
public sealed class DestructibleSyncTracker : MonoBehaviour
{
    public HealthSimpleBase Destructible;
    public uint Id;

    private void Awake()
    {
        if (!Destructible) Destructible = GetComponent<HealthSimpleBase>();
    }

    private void OnDestroy()
    {
        if (Id != 0)
            CoopSyncDatabase.Environment.Destructibles.Unregister(Id, Destructible);

        Destructible = null;
        Id = 0;
    }
}

public sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
{
    public static ReferenceEqualityComparer<T> Instance { get; } = new();

    public bool Equals(T x, T y)
    {
        return ReferenceEquals(x, y);
    }

    public int GetHashCode(T obj)
    {
        return obj != null ? RuntimeHelpers.GetHashCode(obj) : 0;
    }
}

[DisallowMultipleComponent]
public sealed class LootSyncTracker : MonoBehaviour
{
    public InteractableLootbox Lootbox;

    private void Awake()
    {
        if (!Lootbox) Lootbox = GetComponent<InteractableLootbox>();
    }

    private void OnDestroy()
    {
        if (Lootbox) CoopSyncDatabase.Loot.Unregister(Lootbox);
        Lootbox = null;
    }
}
