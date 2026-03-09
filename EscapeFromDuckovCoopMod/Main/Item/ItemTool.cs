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
using ItemStatsSystem;
using ItemStatsSystem.Items;
using LiteNetLib.Utils;
using Duckov.Utilities;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public static class ItemTool
{
    private const int DurabilityLossScale = 10000;

    private static int EncodeInventoryCapacity(int capacity, float durabilityLoss)
    {
        if (durabilityLoss <= 0f)
            return capacity;

        var lossInt = Mathf.Clamp(Mathf.RoundToInt(durabilityLoss * DurabilityLossScale), 0, 0x7FFF);
        var capInt = Mathf.Clamp(capacity, 0, 0xFFFF);
        return unchecked((int)(0x80000000u | ((uint)lossInt << 16) | (uint)capInt));
    }

    private static void DecodeInventoryCapacity(int encoded, out int capacity, out float durabilityLoss)
    {
        if ((encoded & unchecked((int)0x80000000)) != 0)
        {
            var data = unchecked((uint)encoded);
            var lossInt = (int)((data >> 16) & 0x7FFF);
            capacity = (int)(data & 0xFFFF);
            durabilityLoss = lossInt / (float)DurabilityLossScale;
        }
        else
        {
            capacity = encoded;
            durabilityLoss = 0f;
        }
    }
        private const StringComparison QuestTagComparison = StringComparison.OrdinalIgnoreCase;

        public static bool ContainsQuestItem(Item item, HashSet<Item> visited = null)
        {
            if (!item) return false;

            visited ??= new HashSet<Item>();
            if (visited.Contains(item)) return false;
            visited.Add(item);

            if (HasQuestTag(item))
                return true;

            try
            {
                var inv = item.Inventory;
                var cap = inv?.Capacity ?? 0;
                for (var i = 0; i < cap; i++)
                {
                    var child = inv.GetItemAt(i);
                    if (child != null && ContainsQuestItem(child, visited))
                        return true;
                }
            }
            catch
            {
            }

            try
            {
                var slots = item.Slots;
                if (slots != null)
                {
                    foreach (var slot in slots)
                    {
                        if (slot == null) continue;
                        var child = slot.Content;
                        if (child != null && ContainsQuestItem(child, visited))
                            return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public static bool InventoryHasQuestItem(Inventory inv)
        {
            if (inv == null) return false;

            try
            {
                var capacity = inv.Capacity;
                for (var i = 0; i < capacity; i++)
                {
                    var item = inv.GetItemAt(i);
                    if (item != null && ContainsQuestItem(item))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        public static bool HasQuestTag(GameObject go)
        {
            if (!go) return false;

            try
            {
                foreach (var component in go.GetComponents<Component>())
                {
                    if (component == null) continue;
                    if (HasQuestTag(component))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        public static bool HasQuestTag(Item item)
        {
            if (!item) return false;

            try
            {
                if (HasQuestTag(item.Tags))
                    return true;
            }
            catch
            {
            }

            try
            {
                if (HasQuestTag(item.gameObject))
                    return true;
            }
            catch
            {
            }

            try
            {
                var agent = item.ActiveAgent ?? item.AgentUtilities?.ActiveAgent;
                if (agent && HasQuestTag(agent.gameObject))
                    return true;
            }
            catch
            {
            }

            return false;
        }

        private static bool HasQuestTag(object tagHolder)
        {
            if (tagHolder == null) return false;

            try
            {
                var tagsProperty = tagHolder.GetType().GetProperty("Tags");
                if (tagsProperty?.GetValue(tagHolder) is TagCollection tags && HasQuestTag(tags))
                    return true;
            }
            catch
            {
            }

            return false;
        }

        private static bool HasQuestTag(TagCollection tags)
        {
            if (tags == null) return false;

            try
            {
                foreach (var tag in tags)
                {
                    var name = tag?.name;
                    if (!string.IsNullOrEmpty(name) && name.IndexOf("Quest", QuestTagComparison) >= 0)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        public static void AddNetDropTag(GameObject go, uint id)
        {
            if (!go) return;
            var tag = go.GetComponent<NetDropTag>() ?? go.AddComponent<NetDropTag>();
            tag.id = id;
    }

    public static void AddNetDropTag(Item item, uint id)
    {
        try
        {
            if (!item) return;
            var ag = item.ActiveAgent ?? item.AgentUtilities?.ActiveAgent;
            if (ag && ag.gameObject) AddNetDropTag(ag.gameObject, id);
        }
        catch
        {
        }
    }

    public static ItemSnapshot MakeSnapshot(Item item)
    {
        if (!item) return default;

        var hasDurability = item.UseDurability;
        if (!hasDurability)
        {
            try
            {
                hasDurability = item.MaxDurability > 0f || item.Durability > 0f;
            }
            catch
            {
            }
        }

        var snap = new ItemSnapshot
        {
            TypeId = item.TypeID,
            Stack = Mathf.Max(1, item.StackCount),
            HasDurability = hasDurability,
            Durability = hasDurability ? item.Durability : 0f
        };

        var inv = item.Inventory;
        if (inv != null)
        {
            snap.InventoryCapacity = inv.Capacity;
            if (inv.GetItemCount() > 0)
            {
                var list = new List<ItemInventoryEntrySnapshot>(inv.Capacity);
                for (var i = 0; i < inv.Capacity; i++)
                {
                    var child = inv.GetItemAt(i);
                    if (child == null) continue;
                    list.Add(new ItemInventoryEntrySnapshot
                    {
                        Slot = i,
                        Item = MakeSnapshot(child)
                    });
                }

                if (list.Count > 0)
                    snap.Inventory = list.ToArray();
            }
        }

        var slots = item.Slots;
        if (slots != null && slots.Count > 0)
        {
            var list = new List<ItemSlotSnapshot>(slots.Count);
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                var child = slot.Content;
                var childSnap = new ItemSlotSnapshot
                {
                    Key = slot.Key ?? string.Empty,
                    HasItem = child != null,
                    Item = child != null ? MakeSnapshot(child) : default
                };
                list.Add(childSnap);
            }

            snap.Slots = list.ToArray();
        }

        if (hasDurability)
        {
            try
            {
                var loss = item.DurabilityLoss;
                if (loss > 0f)
                    snap.InventoryCapacity = EncodeInventoryCapacity(snap.InventoryCapacity, loss);
            }
            catch
            {
            }
        }

        var customData = ModApiEvents.RaiseItemSnapshotCustomDataRequested(item);
        if (customData != null)
        {
            var keys = new List<string>(customData.Count);
            var values = new List<string>(customData.Count);
            foreach (var kvp in customData)
            {
                if (string.IsNullOrEmpty(kvp.Key)) continue;
                keys.Add(kvp.Key);
                values.Add(kvp.Value ?? string.Empty);
            }

            if (keys.Count > 0)
            {
                snap.CustomDataKeys = keys.ToArray();
                snap.CustomDataValues = values.ToArray();
            }
        }

        return snap;
    }

    public static Item BuildItemFromSnapshot(ItemSnapshot snapshot)
    {
        if (snapshot.TypeId == 0) return null;

        var item = ItemAssetsCollection.InstantiateAsync(snapshot.TypeId).GetAwaiter().GetResult();
        if (!item) return null;

        item.StackCount = Mathf.Max(1, snapshot.Stack);
        if (snapshot.HasDurability)
        {
            try { item.Durability = snapshot.Durability; } catch { }
        }

        var inv = item.Inventory;
        DecodeInventoryCapacity(snapshot.InventoryCapacity, out var decodedCapacity, out var decodedLoss);
        if (decodedLoss > 0f)
        {
            try { item.DurabilityLoss = decodedLoss; } catch { }
        }
        var invSnaps = snapshot.Inventory ?? Array.Empty<ItemInventoryEntrySnapshot>();
        if (inv != null)
        {
            var desiredCap = decodedCapacity > 0 ? decodedCapacity : inv.Capacity;
            if (desiredCap != inv.Capacity)
            {
                try { inv.SetCapacity(desiredCap); } catch { }
            }

            var desiredSlots = new HashSet<int>();
            foreach (var entry in invSnaps)
                desiredSlots.Add(entry.Slot);

            for (var i = 0; i < inv.Capacity; i++)
            {
                if (desiredSlots.Contains(i)) continue;
                var existing = inv.GetItemAt(i);
                if (!existing) continue;
                inv.RemoveAt(i, out _);
                try { UnityEngine.Object.Destroy(existing.gameObject); } catch { }
            }

            foreach (var entry in invSnaps)
            {
                var snapItem = entry.Item;
                if (snapItem.TypeId == 0) continue;

                var existing = inv.GetItemAt(entry.Slot);
                if (existing && existing.TypeID == snapItem.TypeId)
                {
                    ApplySnapshot(existing, snapItem);
                    continue;
                }

                if (existing)
                {
                    inv.RemoveAt(entry.Slot, out _);
                    try { UnityEngine.Object.Destroy(existing.gameObject); } catch { }
                }

                var rebuilt = BuildItemFromSnapshot(snapItem);
                if (!rebuilt) continue;

                if (!inv.AddAt(rebuilt, entry.Slot))
                {
                    try { UnityEngine.Object.Destroy(rebuilt.gameObject); } catch { }
                }
            }
        }

        if (snapshot.Slots != null && snapshot.Slots.Length > 0)
        {
            foreach (var slotSnap in snapshot.Slots)
            {
                if (!slotSnap.HasItem) continue;
                var child = BuildItemFromSnapshot(slotSnap.Item);
                if (child == null) continue;

                try
                {
                    var slot = item.Slots?.GetSlot(slotSnap.Key);
                    slot?.Plug(child, out _);
                }
                catch
                {
                }
            }
        }

        ApplyCustomDataSnapshot(item, snapshot);
        return item;
    }

    public static void WriteItemSnapshot(NetDataWriter writer, ItemSnapshot snapshot)
    {
        writer.Put(snapshot.TypeId);
        if (snapshot.TypeId == 0) return;

        writer.Put(snapshot.Stack);
        writer.Put(snapshot.HasDurability);
        if (snapshot.HasDurability)
            writer.Put(snapshot.Durability);

        writer.Put(snapshot.InventoryCapacity);

        var invSnaps = snapshot.Inventory;
        var invCount = invSnaps?.Length ?? 0;
        writer.Put((ushort)invCount);
        if (invCount > 0)
        {
            for (var i = 0; i < invCount; i++)
            {
                var entry = invSnaps[i];
                writer.Put(entry.Slot);
                WriteItemSnapshot(writer, entry.Item);
            }
        }

        var slots = snapshot.Slots;
        var count = slots?.Length ?? 0;
        writer.Put((ushort)count);
        if (count > 0)
        {
            for (var i = 0; i < count; i++)
            {
                var s = slots[i];
                writer.Put(s.Key ?? string.Empty);
                writer.Put(s.HasItem);
                if (s.HasItem)
                    WriteItemSnapshot(writer, s.Item);
            }
        }

        WriteCustomData(writer, snapshot);
    }

    public static ItemSnapshot ReadItemSnapshot(NetPacketReader reader)
    {
        var typeId = reader.GetInt();
        if (typeId == 0) return default;

        var snapshot = new ItemSnapshot
        {
            TypeId = typeId,
            Stack = reader.GetInt()
        };

        snapshot.HasDurability = reader.GetBool();
        if (snapshot.HasDurability)
            snapshot.Durability = reader.GetFloat();

        snapshot.InventoryCapacity = reader.GetInt();
        var invCount = reader.GetUShort();
        if (invCount > 0)
        {
            var list = new ItemInventoryEntrySnapshot[invCount];
            for (var i = 0; i < invCount; i++)
            {
                list[i].Slot = reader.GetInt();
                list[i].Item = ReadItemSnapshot(reader);
            }
            snapshot.Inventory = list;
        }

        var slotCount = reader.GetUShort();
        if (slotCount > 0)
        {
            var slots = new ItemSlotSnapshot[slotCount];
            for (var i = 0; i < slotCount; i++)
            {
                slots[i].Key = reader.GetString();
                slots[i].HasItem = reader.GetBool();
                if (slots[i].HasItem)
                    slots[i].Item = ReadItemSnapshot(reader);
            }

            snapshot.Slots = slots;
        }

        ReadCustomData(reader, ref snapshot);
        return snapshot;
    }

    public static bool SnapshotMatches(Item item, ItemSnapshot snapshot)
    {
        if (!item) return false;
        if (snapshot.TypeId == 0 || item.TypeID != snapshot.TypeId) return false;
        if (Mathf.Max(1, item.StackCount) != Mathf.Max(1, snapshot.Stack)) return false;
        if (snapshot.HasDurability)
        {
            var a = snapshot.Durability;
            var b = item.Durability;
            if (Mathf.Abs(a - b) > 0.01f) return false;
        }
        DecodeInventoryCapacity(snapshot.InventoryCapacity, out var snapshotCapacity, out var snapshotLoss);
        if (snapshotLoss > 0f && Mathf.Abs(snapshotLoss - item.DurabilityLoss) > 0.0001f) return false;

        var inv = item.Inventory;
        var invSnaps = snapshot.Inventory ?? Array.Empty<ItemInventoryEntrySnapshot>();
        if (inv != null)
        {
            if (snapshot.InventoryCapacity > 0 && inv.Capacity != snapshot.InventoryCapacity)
                return false;

            var invSnapMap = new Dictionary<int, ItemInventoryEntrySnapshot>();
            foreach (var entry in invSnaps)
                invSnapMap[entry.Slot] = entry;

            for (var i = 0; i < inv.Capacity; i++)
            {
                var child = inv.GetItemAt(i);
                if (invSnapMap.TryGetValue(i, out var entry))
                {
                    if (entry.Item.TypeId == 0)
                    {
                        if (child) return false;
                        invSnapMap.Remove(i);
                        continue;
                    }

                    if (!child || !SnapshotMatches(child, entry.Item))
                        return false;

                    invSnapMap.Remove(i);
                }
                else if (child)
                {
                    return false;
                }
            }

            foreach (var entry in invSnapMap.Values)
            {
                if (entry.Item.TypeId != 0)
                    return false;
            }
        }
        else if (invSnaps.Length > 0)
        {
            return false;
        }

        var slots = item.Slots;
        var snapSlots = snapshot.Slots ?? Array.Empty<ItemSlotSnapshot>();

        if (!CustomDataMatches(snapshot, item))
            return false;

        if (slots == null || slots.Count == 0)
        {
            foreach (var s in snapSlots)
            {
                if (s.HasItem || s.Item.TypeId != 0)
                    return false;
            }

            return true;
        }

        var slotSnapMap = new Dictionary<string, ItemSlotSnapshot>(StringComparer.Ordinal);
        foreach (var s in snapSlots)
        {
            var key = s.Key ?? string.Empty;
            slotSnapMap[key] = s;
        }

        foreach (var slot in slots)
        {
            if (slot == null) continue;

            var key = slot.Key ?? string.Empty;
            slotSnapMap.TryGetValue(key, out var s);

            var hasSnapItem = s.HasItem || s.Item.TypeId != 0;
            var child = slot.Content;

            if (hasSnapItem)
            {
                if (!child) return false;

                var childSnap = s.Item;
                if (childSnap.TypeId == 0)
                    childSnap.TypeId = child.TypeID;
                if (childSnap.Stack == 0)
                    childSnap.Stack = child.StackCount;

                if (!SnapshotMatches(child, childSnap))
                    return false;
            }
            else if (child)
            {
                return false;
            }

            slotSnapMap.Remove(key);
        }

        foreach (var kv in slotSnapMap.Values)
        {
            if (kv.HasItem || kv.Item.TypeId != 0)
                return false;
        }

        return true;
    }

    public static bool ApplySnapshot(Item item, ItemSnapshot snapshot)
    {
        if (!item || snapshot.TypeId == 0 || item.TypeID != snapshot.TypeId)
            return false;

        item.StackCount = Mathf.Max(1, snapshot.Stack);
        if (snapshot.HasDurability)
        {
            try { item.Durability = snapshot.Durability; } catch { }
        }

        var inv = item.Inventory;
        DecodeInventoryCapacity(snapshot.InventoryCapacity, out var applyCapacity, out var applyLoss);
        if (applyLoss > 0f)
        {
            try { item.DurabilityLoss = applyLoss; } catch { }
        }
        var invSnaps = snapshot.Inventory ?? Array.Empty<ItemInventoryEntrySnapshot>();
        if (inv != null)
        {
            var desiredCap = applyCapacity > 0 ? applyCapacity : inv.Capacity;
            if (desiredCap != inv.Capacity)
            {
                try { inv.SetCapacity(desiredCap); } catch { }
            }

            var desiredSlots = new HashSet<int>();
            foreach (var entry in invSnaps)
                desiredSlots.Add(entry.Slot);

            for (var i = 0; i < inv.Capacity; i++)
            {
                if (desiredSlots.Contains(i)) continue;
                var existing = inv.GetItemAt(i);
                if (!existing) continue;
                inv.RemoveAt(i, out _);
                try { UnityEngine.Object.Destroy(existing.gameObject); } catch { }
            }

            foreach (var entry in invSnaps)
            {
                var snapItem = entry.Item;
                if (snapItem.TypeId == 0) continue;

                var existing = inv.GetItemAt(entry.Slot);
                if (existing && existing.TypeID == snapItem.TypeId)
                {
                    ApplySnapshot(existing, snapItem);
                    continue;
                }

                if (existing)
                {
                    inv.RemoveAt(entry.Slot, out _);
                    try { UnityEngine.Object.Destroy(existing.gameObject); } catch { }
                }

                var rebuilt = BuildItemFromSnapshot(snapItem);
                if (!rebuilt) continue;

                if (!inv.AddAt(rebuilt, entry.Slot))
                {
                    try { UnityEngine.Object.Destroy(rebuilt.gameObject); } catch { }
                }
            }
        }

        var slots = item.Slots;
        var snapSlots = snapshot.Slots ?? Array.Empty<ItemSlotSnapshot>();

        if (slots == null || slots.Count == 0)
            return true;

        var snapMap = new Dictionary<string, ItemSlotSnapshot>(StringComparer.Ordinal);
        foreach (var s in snapSlots)
        {
            var key = s.Key ?? string.Empty;
            snapMap[key] = s;
        }

        foreach (var slot in slots)
        {
            if (slot == null) continue;

            var key = slot.Key ?? string.Empty;
            snapMap.TryGetValue(key, out var s);

            var hasSnapItem = s.HasItem || s.Item.TypeId != 0;
            var child = slot.Content;

            if (!hasSnapItem)
            {
                if (child)
                {
                    slot.Unplug();
                    try { UnityEngine.Object.Destroy(child.gameObject); } catch { }
                }

                continue;
            }

            var snapItem = s.Item;
            Item newChild = null;

            if (child && child.TypeID == snapItem.TypeId)
            {
                ApplySnapshot(child, snapItem);
                newChild = child;
            }
            else
            {
                if (child)
                {
                    slot.Unplug();
                    try { UnityEngine.Object.Destroy(child.gameObject); } catch { }
                }

                newChild = BuildItemFromSnapshot(snapItem);
            }

            if (!newChild) continue;

            Item unplugged = null;
            if (slot.Content != newChild)
                slot.Plug(newChild, out unplugged);

            if (slot.Content != newChild && newChild)
            {
                try { UnityEngine.Object.Destroy(newChild.gameObject); } catch { }
                if (unplugged && unplugged != newChild)
                {
                    try { UnityEngine.Object.Destroy(unplugged.gameObject); } catch { }
                }
            }
        }

        foreach (var kv in snapMap)
        {
            if (string.IsNullOrEmpty(kv.Key))
                continue;
            // already applied in loop above; map only used for lookup
        }

        ApplyCustomDataSnapshot(item, snapshot);
        return true;
    }

    public static int ComputeSnapshotHash(ItemSnapshot snapshot)
    {
        var hash = new HashCode();
        hash.Add(snapshot.TypeId);
        hash.Add(snapshot.Stack);
        hash.Add(snapshot.HasDurability);
        if (snapshot.HasDurability)
            hash.Add(snapshot.Durability);
        DecodeInventoryCapacity(snapshot.InventoryCapacity, out var hashCapacity, out var hashLoss);
        hash.Add(hashCapacity);
        if (hashLoss > 0f)
            hash.Add(hashLoss);

        if (snapshot.Inventory != null)
        {
            foreach (var entry in snapshot.Inventory)
            {
                hash.Add(entry.Slot);
                hash.Add(ComputeSnapshotHash(entry.Item));
            }
        }

        if (snapshot.Slots != null)
        {
            foreach (var slot in snapshot.Slots)
            {
                hash.Add(slot.Key ?? string.Empty);
                hash.Add(slot.HasItem);
                if (slot.HasItem)
                    hash.Add(ComputeSnapshotHash(slot.Item));
            }
        }

        if (snapshot.CustomDataKeys != null && snapshot.CustomDataValues != null)
        {
            var length = Math.Min(snapshot.CustomDataKeys.Length, snapshot.CustomDataValues.Length);
            for (var i = 0; i < length; i++)
            {
                hash.Add(snapshot.CustomDataKeys[i] ?? string.Empty);
                hash.Add(snapshot.CustomDataValues[i] ?? string.Empty);
            }
        }

        return hash.ToHashCode();
    }

    private static void WriteCustomData(NetDataWriter writer, ItemSnapshot snapshot)
    {
        var keys = snapshot.CustomDataKeys;
        var values = snapshot.CustomDataValues;
        var count = keys != null && values != null ? Math.Min(keys.Length, values.Length) : 0;
        writer.Put((ushort)count);
        for (var i = 0; i < count; i++)
        {
            writer.Put(keys[i] ?? string.Empty);
            writer.Put(values[i] ?? string.Empty);
        }
    }

    private static void ReadCustomData(NetPacketReader reader, ref ItemSnapshot snapshot)
    {
        if (reader.AvailableBytes <= 0)
            return;

        var count = reader.GetUShort();
        if (count == 0)
        {
            snapshot.CustomDataKeys = Array.Empty<string>();
            snapshot.CustomDataValues = Array.Empty<string>();
            return;
        }

        var keys = new string[count];
        var values = new string[count];
        for (var i = 0; i < count; i++)
        {
            keys[i] = reader.GetString();
            values[i] = reader.GetString();
        }

        snapshot.CustomDataKeys = keys;
        snapshot.CustomDataValues = values;
    }

    private static void ApplyCustomDataSnapshot(Item item, ItemSnapshot snapshot)
    {
        if (!item) return;
        var keys = snapshot.CustomDataKeys;
        var values = snapshot.CustomDataValues;
        if (keys == null || values == null) return;

        var count = Math.Min(keys.Length, values.Length);
        if (count <= 0) return;

        var map = new Dictionary<string, string>(count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            if (string.IsNullOrEmpty(keys[i])) continue;
            map[keys[i]] = values[i] ?? string.Empty;
        }

        if (map.Count > 0)
            ModApiEvents.RaiseItemSnapshotCustomDataApplied(item, map);
    }

    private static bool CustomDataMatches(ItemSnapshot snapshot, Item item)
    {
        var keys = snapshot.CustomDataKeys;
        var values = snapshot.CustomDataValues;
        if (keys == null || values == null || keys.Length == 0 || values.Length == 0)
            return true;

        var count = Math.Min(keys.Length, values.Length);
        if (count == 0) return true;

        var map = new Dictionary<string, string>(count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            if (string.IsNullOrEmpty(keys[i])) continue;
            map[keys[i]] = values[i] ?? string.Empty;
        }

        if (map.Count == 0) return true;

        var expected = ModApiEvents.RaiseItemSnapshotCustomDataRequested(item);
        if (expected == null || expected.Count == 0)
            return false;

        if (expected.Count != map.Count) return false;
        foreach (var kvp in expected)
        {
            if (!map.TryGetValue(kvp.Key, out var value)) return false;
            if (!string.Equals(value ?? string.Empty, kvp.Value ?? string.Empty, StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
