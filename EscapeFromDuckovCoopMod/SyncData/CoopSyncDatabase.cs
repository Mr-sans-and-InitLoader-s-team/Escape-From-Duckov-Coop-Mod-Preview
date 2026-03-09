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
using System.Linq;
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

        // If another controller already claimed this stable id (e.g., two nearly identical AI spawned together),
        // salt the id with the unique instance id so both replicas keep distinct sync keys instead of masking
        // each other on the client.
        if (_entries.TryGetValue(id, out var existing) && existing?.Controller != null && existing.Controller != controller)
        {
            unchecked
            {
                var salted = (id * 486187739) ^ controller.GetInstanceID();
                if (salted == 0)
                    salted = controller.GetInstanceID();

                while (_entries.TryGetValue(salted, out var collision) && collision?.Controller != controller)
                    salted++;

                id = salted;
            }
        }

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
            entry.CharacterPresetKey = string.IsNullOrEmpty(cmc.characterPreset.nameKey)
                ? cmc.characterPreset.name
                : cmc.characterPreset.nameKey;

 

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

    public void RemoveEntry(int id)
    {
        if (id == 0)
            return;

        if (!_entries.TryGetValue(id, out var entry) || entry == null)
        {
            _entries.Remove(id);
            return;
        }

        _entries.Remove(id);

        if (entry.Controller)
            _byController.Remove(entry.Controller);

        if (entry.PositionKey != 0 &&
            _byPositionKey.TryGetValue(entry.PositionKey, out var positionEntry) &&
            ReferenceEquals(positionEntry, entry))
        {
            _byPositionKey.Remove(entry.PositionKey);
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

public sealed class LootSyncRegistry
{
    private sealed class InventoryRefEq : IEqualityComparer<Inventory>
    {
        public bool Equals(Inventory x, Inventory y) => ReferenceEquals(x, y);
        public int GetHashCode(Inventory obj) => obj ? obj.GetHashCode() : 0;
    }

    private readonly List<LootSyncEntry> _entries = new();
    private readonly Dictionary<Inventory, LootSyncEntry> _byInventory = new(new InventoryRefEq());
    private readonly Dictionary<int, LootSyncEntry> _byPositionKey = new();
    private readonly Dictionary<int, LootSyncEntry> _byInstanceId = new();
    private readonly Dictionary<int, LootSyncEntry> _byLootUid = new();
    private readonly Dictionary<LootBoxLoader, LootSyncEntry> _byLoader = new();

    public IEnumerable<LootSyncEntry> Entries => _entries;

    public bool ContainsInventory(Inventory inventory)
    {
        return inventory && _byInventory.ContainsKey(inventory);
    }

    public bool TryGetByInventory(Inventory inventory, out LootSyncEntry entry)
    {
        entry = null;
        if (inventory == null) return false;

        if (_byInventory.TryGetValue(inventory, out entry) && entry != null && entry.Inventory)
            return true;

        entry = null;
        return false;
    }

    public bool TryGetByPositionKey(int key, out LootSyncEntry entry)
    {
        entry = null;
        if (key == 0) return false;

        if (_byPositionKey.TryGetValue(key, out entry) && entry != null && entry.Inventory)
            return true;

        entry = null;
        return false;
    }

    public bool TryGetByInstanceId(int instanceId, out LootSyncEntry entry)
    {
        entry = null;
        if (instanceId == 0) return false;

        if (_byInstanceId.TryGetValue(instanceId, out entry) && entry != null && entry.Inventory)
            return true;

        entry = null;
        return false;
    }

    public void Register(InteractableLootbox lootbox, Inventory inventory, int lootUid = -1)
    {
        if (!inventory) return;
        if (LootboxDetectUtil.IsPrivateInventory(inventory)) return;

        var instanceId = lootbox ? lootbox.GetInstanceID() : 0;
        var posKey = 0;
        var sceneIndex = 0;
        var worldPos = Vector3.zero;

        if (lootbox)
        {
            var p = lootbox.transform.position * 10f;
            posKey = new Vector3Int(
                Mathf.RoundToInt(p.x),
                Mathf.RoundToInt(p.y),
                Mathf.RoundToInt(p.z)).GetHashCode();
            sceneIndex = lootbox.gameObject.scene.buildIndex;
            worldPos = lootbox.transform.position;
        }

        var entry = _byInventory.TryGetValue(inventory, out var existing) ? existing : null;
        if (entry == null)
        {
            entry = new LootSyncEntry();
            _entries.Add(entry);
        }

        entry.Inventory = inventory;
        entry.Lootbox = lootbox;
        entry.PositionKey = posKey;
        entry.InstanceId = instanceId;
        entry.SceneIndex = sceneIndex;
        entry.WorldPosition = worldPos;
        entry.IsWorldLoot = !LootboxDetectUtil.IsPrivateInventory(inventory);

        if (lootUid >= 0) entry.LootUid = lootUid;

        if (lootbox)
        {
            if (instanceId != 0) _byInstanceId[instanceId] = entry;
            if (posKey != 0) _byPositionKey[posKey] = entry;
        }

        _byInventory[inventory] = entry;

        if (lootUid >= 0) _byLootUid[lootUid] = entry;

        var loader = lootbox ? lootbox.GetComponent<LootBoxLoader>() : null;
        if (loader) _byLoader[loader] = entry;
    }

    public void RegisterLoader(LootBoxLoader loader)
    {
        if (!loader) return;
        var lootbox = loader.GetComponent<InteractableLootbox>();
        Register(lootbox, lootbox ? lootbox.Inventory : null, -1);
    }

    public void Unregister(InteractableLootbox lootbox)
    {
        if (!lootbox) return;
        var instanceId = lootbox.GetInstanceID();
        if (!_byInstanceId.TryGetValue(instanceId, out var entry) || entry == null)
            return;

        _byInstanceId.Remove(instanceId);
        if (entry.PositionKey != 0 && _byPositionKey.TryGetValue(entry.PositionKey, out var prevPos) &&
            ReferenceEquals(prevPos, entry))
            _byPositionKey.Remove(entry.PositionKey);

        if (entry.LootUid >= 0 && _byLootUid.TryGetValue(entry.LootUid, out var prevUid) && ReferenceEquals(prevUid, entry))
            _byLootUid.Remove(entry.LootUid);

        if (entry.Inventory && _byInventory.TryGetValue(entry.Inventory, out var prevInv) && ReferenceEquals(prevInv, entry))
            _byInventory.Remove(entry.Inventory);

        var loader = lootbox.GetComponent<LootBoxLoader>();
        if (loader && _byLoader.TryGetValue(loader, out var prevLoader) && ReferenceEquals(prevLoader, entry))
            _byLoader.Remove(loader);

        _entries.Remove(entry);
    }

    public void SetLootUid(Inventory inventory, int lootUid)
    {
        if (!inventory || lootUid < 0) return;
        if (!_byInventory.TryGetValue(inventory, out var entry) || entry == null)
            return;

        entry.LootUid = lootUid;
        _byLootUid[lootUid] = entry;
    }

    public bool TryGetByLootUid(int lootUid, out LootSyncEntry entry)
    {
        entry = null;
        if (lootUid < 0) return false;
        if (_byLootUid.TryGetValue(lootUid, out entry) && entry != null && entry.Inventory)
            return true;

        entry = null;
        return false;
    }

    public void Clear()
    {
        _entries.Clear();
        _byInventory.Clear();
        _byPositionKey.Clear();
        _byInstanceId.Clear();
        _byLootUid.Clear();
        _byLoader.Clear();
    }
}

public sealed class DropSyncRegistry
{
    private readonly Dictionary<uint, DropSyncEntry> _entries = new();
    private readonly Dictionary<Item, DropSyncEntry> _byItem = new(new RefEq<Item>());

    public IEnumerable<DropSyncEntry> Entries => _entries.Values;

    public DropSyncEntry Register(uint dropId, Item item, GameObject agent = null)
    {
        if (dropId == 0 || item == null) return null;

        if (!_entries.TryGetValue(dropId, out var entry) || entry == null)
        {
            entry = new DropSyncEntry();
            _entries[dropId] = entry;
        }

        entry.DropId = dropId;
        entry.Item = item;
        entry.Agent = agent;

        _byItem[item] = entry;

        return entry;
    }

    public bool TryGetById(uint dropId, out DropSyncEntry entry)
    {
        entry = null;
        return dropId != 0 && _entries.TryGetValue(dropId, out entry) && entry != null;
    }

    public bool TryGetByItem(Item item, out DropSyncEntry entry)
    {
        entry = null;
        return item && _byItem.TryGetValue(item, out entry) && entry != null;
    }

    public void Unregister(uint dropId)
    {
        if (dropId == 0) return;
        if (!_entries.TryGetValue(dropId, out var entry) || entry == null) return;

        if (entry.Item)
            _byItem.Remove(entry.Item);

        _entries.Remove(dropId);
    }

    public void Unregister(Item item)
    {
        if (!item) return;
        if (_byItem.TryGetValue(item, out var entry) && entry != null)
            Unregister(entry.DropId);
    }

    public void Clear()
    {
        _entries.Clear();
        _byItem.Clear();
    }

    private sealed class RefEq<T> : IEqualityComparer<T> where T : class
    {
        public bool Equals(T x, T y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

public sealed class DropSyncEntry
{
    public uint DropId { get; set; }
    public Item Item { get; set; }
    public GameObject Agent { get; set; }
}

public sealed class LootSyncEntry
{
    public LootBoxLoader Loader { get; set; }
    public bool IsWorldLoot { get; set; }
    public Inventory Inventory { get; set; }
    public InteractableLootbox Lootbox { get; set; }
    public int PositionKey { get; set; }
    public int InstanceId { get; set; }
    public int SceneIndex { get; set; }
    public int LootUid { get; set; } = -1;
    public Vector3 WorldPosition { get; set; }
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
