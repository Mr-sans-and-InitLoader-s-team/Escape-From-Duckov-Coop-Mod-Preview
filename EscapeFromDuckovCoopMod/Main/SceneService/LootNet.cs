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
using Cysharp.Threading.Tasks;
using System.Linq;
using LiteNetLib;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.Scenes;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

    public class LootNet
    {
    public readonly Dictionary<uint, Item> _cliPendingPut = new();
    public readonly Dictionary<Item, (Item newItem, Inventory destInv, int destPos, Slot destSlot)> _cliSwapByVictim = new();
    public bool _applyingLootState;
    public uint _nextLootToken = 1;
    public bool _serverApplyingLoot;
    private readonly HashSet<Inventory> _cliActiveLoot = new(new InventoryRefEq());
    private readonly HashSet<Item> _srvPlayerPlaced = new();
    private readonly Dictionary<int, bool> _cliLootVisibility = new();
    private readonly Dictionary<Item, string> _cliSlotOrigins = new();

    private sealed class InventoryRefEq : IEqualityComparer<Inventory>
    {
        public bool Equals(Inventory x, Inventory y) => ReferenceEquals(x, y);
        public int GetHashCode(Inventory obj) => obj ? obj.GetHashCode() : 0;
    }

    private readonly Dictionary<Inventory, int> _cliVersions = new(new InventoryRefEq());
    private readonly Dictionary<Inventory, int> _srvVersions = new(new InventoryRefEq());
    private readonly Dictionary<Inventory, HashSet<NetPeer>> _srvViewers = new(new InventoryRefEq());
    private readonly HashSet<Inventory> _srvHookedInventories = new(new InventoryRefEq());
    private readonly HashSet<Item> _srvHookedItems = new();

    private NetService Service => NetService.Instance;
    private LootManager LootManagerInstance => EscapeFromDuckovCoopMod.LootManager.Instance;
    public bool IsServer => Service != null && Service.IsServer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private bool networkStarted => Service != null && Service.networkStarted;

    public bool ApplyingLootState => _applyingLootState;

    private static bool IsQuestTagged(Item item) => ItemTool.HasQuestTag(item);

    public void Client_RecordSlotOrigin(Item item, string slotKey)
    {
        if (item == null || string.IsNullOrEmpty(slotKey)) return;
        _cliSlotOrigins[item] = slotKey;
    }

    private string Client_ConsumeSlotOrigin(Item item)
    {
        if (item != null && _cliSlotOrigins.TryGetValue(item, out var key))
        {
            _cliSlotOrigins.Remove(item);
            return key;
        }

        return string.Empty;
    }

    public void Reset()
    {
        _applyingLootState = false;
        _serverApplyingLoot = false;
        _nextLootToken = 1;

        _cliPendingPut.Clear();
        _cliSwapByVictim.Clear();
        _cliActiveLoot.Clear();
        _srvPlayerPlaced.Clear();
        _cliLootVisibility.Clear();
        _cliSlotOrigins.Clear();

        _cliVersions.Clear();
        _srvVersions.Clear();
        _srvViewers.Clear();

        foreach (var inv in _srvHookedInventories.ToArray())
        {
            if (inv)
                inv.onContentChanged -= Server_OnInventoryContentChanged;
        }

        _srvHookedInventories.Clear();

        foreach (var item in _srvHookedItems.ToArray())
        {
            if (!item) continue;
            try
            {
                var slots = item.Slots;
                if (slots == null) continue;
                foreach (var slot in slots)
                {
                    if (slot == null) continue;
                    slot.onSlotContentChanged -= Server_OnItemSlotContentChanged;
                }
            }
            catch
            {
            }
        }

        _srvHookedItems.Clear();
    }

    public void Server_HandlePlayerDeathWithInventory(NetPacketReader reader)
    {
        var pos = reader.GetV3cm();

        var itemCount = reader.GetInt();
        var itemSnapshots = new List<ItemSnapshot>();
        
        for (int i = 0; i < itemCount; i++)
        {
            try
            {
                var snap = ItemTool.ReadItemSnapshot(reader);
                itemSnapshots.Add(snap);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DEATH] Error reading item {i}: {ex}");
            }
        }
        
        Debug.Log($"[DEATH] Read {itemSnapshots.Count} item snapshots from client");
        
        // NOW spawn with the pre-read data
        Server_SpawnDeadPlayerLoot(pos, itemSnapshots);
    }

    private async void Server_SpawnDeadPlayerLoot(Vector3 position, List<ItemSnapshot> itemSnapshots)
    {
        try
        {            
            // Get the loot box prefab
            var prefab = LootManager.Instance.ResolveDeadLootPrefabOnServer();
            if (!prefab)
            {
                Debug.LogError("[DEATH] Cannot find loot box prefab!");
                return;
            }
            
            // Spawn the loot container
            var lootObj = GameObject.Instantiate(prefab.gameObject, position, Quaternion.identity);

            var lootBox = lootObj.GetComponent<InteractableLootbox>();
            if (!lootBox)
            {
                Debug.LogError("[DEATH] Spawned object doesn't have InteractableLootbox!");
                GameObject.Destroy(lootObj);
                return;
            }
            
            var inventory = lootBox.Inventory;
            if (!inventory)
            {
                Debug.LogError("[DEATH] Loot box doesn't have inventory!");
                GameObject.Destroy(lootObj);
                return;
            }

            var itemCount = itemSnapshots.Count;
            Debug.Log($"[DEATH] Spawning loot at {position} with {itemCount} items");
            
            inventory.SetCapacity(Mathf.Max(itemCount, 10));
            
            for (int i = 0; i < itemCount; i++)
            {
                try
                {
                    var snap = itemSnapshots[i];
                    var tmpItem = await ItemAssetsCollection.InstantiateAsync(snap.TypeId);
                    ItemTool.ApplySnapshot(tmpItem, snap);
                    inventory.AddItem(tmpItem);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[DEATH] Error creating item {i}: {ex}");
                }
            }
            
            // Register the loot box
            var posKey = LootManager.Instance.ComputeLootKey(lootObj.transform);
            Debug.Log($"[DEATH] Computed position key: {posKey}");
            
            if (posKey != 0)
            {
                CoopSyncDatabase.Loot.Register(lootBox, inventory);
                Debug.Log($"[DEATH] Registered in CoopSyncDatabase");
            }
            
            // Also register in InteractableLootbox.Inventories
            try
            {
                var dict = InteractableLootbox.Inventories;
                if (dict != null && posKey != 0)
                {
                    dict[posKey] = inventory;
                    Debug.Log($"[DEATH] Registered in InteractableLootbox.Inventories");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DEATH] Could not register in InteractableLootbox.Inventories: {ex}");
            }
            
            Debug.Log($"[DEATH] Loot container spawned successfully!");
            
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DEATH] FATAL Error spawning loot: {ex}");
        }
    }    

    private int NextVersion(Inventory inv)
    {
        if (IsServer)
        {
            var v = _srvVersions.TryGetValue(inv, out var cur) ? cur : 0;
            v++;
            _srvVersions[inv] = v;
            return v;
        }

        var cliV = _cliVersions.TryGetValue(inv, out var curCli) ? curCli : 0;
        cliV++;
        _cliVersions[inv] = cliV;
        return cliV;
    }

    private int GetCurrentVersion(Inventory inv)
    {
        if (IsServer) return _srvVersions.TryGetValue(inv, out var v) ? v : 0;
        return _cliVersions.TryGetValue(inv, out var cv) ? cv : 0;
    }

    public void Client_SetLootActive(Inventory inv, bool active)
    {
        if (IsServer || inv == null) return;
        if (active)
            _cliActiveLoot.Add(inv);
        else
            _cliActiveLoot.Remove(inv);
    }

    public bool Client_ShouldSendLootChange(Inventory inv)
    {
        if (IsServer || inv == null) return false;
        return _cliActiveLoot.Contains(inv);
    }

    public void Server_OnInventorySlotChanged(Inventory inv, int slot)
    {
        if (!IsServer || inv == null) return;
        if (LootManagerInstance != null && LootManagerInstance.Server_IsLootMuted(inv)) return;

        var item = inv.GetItemAt(slot);
        if (item != null && IsQuestTagged(item))
            return;
        var cleared = item == null;
        var typeId = cleared ? -1 : item.TypeID;
        var stack = cleared ? 0 : item.StackCount;

        BroadcastLootDelta(inv, slot, typeId, stack, cleared);
    }

    public void Server_BroadcastHostSlotChange(Inventory inv, int slot)
    {
        if (!IsServer || inv == null) return;

        var lm = LootManagerInstance;
        if (lm == null) return;
        if (!LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv)) return;

        var item = inv.GetItemAt(slot);
        if (item != null && IsQuestTagged(item))
            return;
        if (item != null)
        {
            var placed = _srvPlayerPlaced.Contains(item);
            ResetInspectionFlags(inv, item, placed);
        }
        var cleared = item == null;
        var typeId = cleared ? -1 : item.TypeID;
        var stack = cleared ? 0 : item.StackCount;

        BroadcastLootDelta(inv, slot, typeId, stack, cleared);
    }

    private static void ResetInspectionFlags(Inventory inv, Item item, bool placedByPlayer = false)
    {
        if (inv == null || item == null) return;

        var need = false;
        var inspected = false;
        try
        {
            need = inv.NeedInspection;
        }
        catch
        {
        }

        try
        {
            inspected = inv.hasBeenInspectedInLootBox;
        }
        catch
        {
        }

        if (placedByPlayer)
        {
            item.Inspected = true;
            item.Inspecting = false;
            return;
        }

        if (need && !inspected)
        {
            // 保留已经鉴定过的物品，未鉴定的保持迷雾，只是停止检查动画
            item.Inspecting = false;
            if (!item.Inspected)
            {
                item.Inspected = false;
            }
        }
        else
        {
            item.Inspected = true;
            item.Inspecting = false;
        }
    }

    public void Client_RequestLootState(Inventory lootInv, bool forceSnapshot = false)
    {
        if (IsServer || connectedPeer == null || lootInv == null || !networkStarted) return;
        if (!LootboxDetectUtil.IsLootboxInventory(lootInv)) return;

        var lm = LootManagerInstance;
        if (lm == null) return;

        var rpc = new LootOpenRequestRpc
        {
            Id = lm.BuildLootIdentifier(lootInv),
            PositionHint = lootInv.transform.position,
            ForceSnapshot = forceSnapshot
        };

        CoopTool.SendRpc(in rpc);
    }

    public void Server_HandleLootOpenRequest(RpcContext context, LootOpenRequestRpc rpc)
    {
        var lm = LootManagerInstance;
        if (!context.IsServer || lm == null) return;

        var inv = lm.ResolveLootInv(rpc.Id.Scene, rpc.Id.PositionKey, rpc.Id.InstanceId, rpc.Id.LootUid);
        if (inv == null && rpc.PositionHint != Vector3.zero)
            lm.Server_TryResolveLootAggressive(rpc.Id.Scene, rpc.Id.PositionKey, rpc.Id.InstanceId, rpc.PositionHint,
                out inv);

        if (!LootboxDetectUtil.IsLootboxInventory(inv) || inv == null)
        {
            Server_SendLootDeny(context.Sender, "no_inv");
            return;
        }

        Server_SendLootboxState(context.Sender, inv);
    }

    public void Server_SendLootboxState(NetPeer toPeer, Inventory inv)
    {
        if (!IsServer || toPeer == null || inv == null) return;
        var lm = LootManagerInstance;
        if (lm == null) return;

        Server_EnsureInventoryHook(inv);
        Server_AddViewer(inv, toPeer);

        var slots = new LootSlotSnapshot[inv.Capacity];
        for (var i = 0; i < inv.Capacity; i++)
        {
            var item = inv.GetItemAt(i);
            slots[i] = item == null || IsQuestTagged(item)
                ? new LootSlotSnapshot { HasItem = false }
                : new LootSlotSnapshot
                {
                    HasItem = true,
                    TypeId = item.TypeID,
                    Stack = item.StackCount,
                    Item = ItemTool.MakeSnapshot(item)
                };
        }

        var rpc = new LootStateRpc
        {
            Id = lm.BuildLootIdentifier(inv),
            IsSnapshot = true,
            Version = NextVersion(inv),
            Capacity = inv.Capacity,
            Snapshot = slots
        };

        CoopTool.SendRpcTo(toPeer, in rpc);
    }

    private void Server_AddViewer(Inventory inv, NetPeer peer)
    {
        if (!IsServer || inv == null || peer == null) return;
        if (!_srvViewers.TryGetValue(inv, out var viewers))
        {
            viewers = new HashSet<NetPeer>();
            _srvViewers[inv] = viewers;
        }

        viewers.Add(peer);
    }

    private void Server_EnsureInventoryHook(Inventory inv)
    {
        if (!IsServer || inv == null) return;
        if (!LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv)) return;
        Server_HookInventory(inv);
    }

    private void Server_HookInventory(Inventory inv)
    {
        if (!IsServer || inv == null) return;
        if (_srvHookedInventories.Contains(inv)) return;

        inv.onContentChanged += Server_OnInventoryContentChanged;
        _srvHookedInventories.Add(inv);

        try
        {
            for (var i = 0; i < inv.Capacity; i++)
            {
                var item = inv.GetItemAt(i);
                Server_HookItemSlots(item);
                Server_HookItemInventory(item);
            }
        }
        catch
        {
        }
    }

    private void Server_OnInventoryContentChanged(Inventory inv, int slot)
    {
        if (!IsServer || inv == null) return;
        if (_serverApplyingLoot) return;

        if (!LootboxDetectUtil.TryResolveLootOwner(inv, out var lootInv, out var ownerSlot, out var master))
            return;
        if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return;

        var targetSlot = ownerSlot >= 0 ? ownerSlot : slot;
        if (master == null && ownerSlot < 0)
            master = lootInv.GetItemAt(targetSlot);

        if (master)
        {
            var placed = _srvPlayerPlaced.Contains(master);
            ResetInspectionFlags(lootInv, master, placed);
            Server_HookItemSlots(master);
            Server_HookItemInventory(master);
        }

        Server_BroadcastHostSlotChange(lootInv, targetSlot);
    }

    private void Server_HookItemSlots(Item item)
    {
        if (!IsServer || item == null) return;
        if (_srvHookedItems.Contains(item)) return;

        try
        {
            Server_HookItemInventory(item);

            var slots = item.Slots;
            if (slots == null || slots.Count == 0) return;
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                slot.onSlotContentChanged += Server_OnItemSlotContentChanged;

                var child = slot.Content;
                if (child != null)
                    Server_HookItemSlots(child);
            }
            _srvHookedItems.Add(item);
        }
        catch
        {
        }
    }

    private void Server_HookItemInventory(Item item)
    {
        if (!IsServer || item == null) return;

        try
        {
            var inv = item.Inventory;
            if (inv == null) return;
            Server_HookInventory(inv);
        }
        catch
        {
        }
    }

    private void Server_OnItemSlotContentChanged(Slot slot)
    {
        if (!IsServer || slot == null) return;
        if (_serverApplyingLoot) return;

        var master = slot.Master;
        var inv = master ? master.InInventory : null;
        if (inv == null || LootboxDetectUtil.IsPrivateInventory(inv) || !LootboxDetectUtil.IsLootboxInventory(inv)) return;

        var pos = inv.GetIndex(master);
        if (pos < 0) return;

        if (master != null && IsQuestTagged(master))
            return;

        ResetInspectionFlags(inv, master, _srvPlayerPlaced.Contains(master));
        BroadcastLootDelta(inv, pos, master.TypeID, master.StackCount, false);
    }

    internal void Server_MarkPlayerPlaced(Item item)
    {
        if (!IsServer || item == null) return;
        _srvPlayerPlaced.Add(item);
    }

    private void Server_MarkItemTreePlayerPlaced(Item root)
    {
        if (!IsServer || root == null) return;

        var stack = new Stack<Item>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (item == null) continue;

            _srvPlayerPlaced.Add(item);

            try
            {
                var slots = item.Slots;
                if (slots == null) continue;
                foreach (var slot in slots)
                {
                    var child = slot == null ? null : slot.Content;
                    if (child != null)
                        stack.Push(child);
                }
            }
            catch
            {
            }
        }
    }

    public void Server_RemoveViewer(NetPeer peer)
    {
        if (!IsServer || peer == null) return;

        foreach (var kvp in _srvViewers)
            kvp.Value.Remove(peer);
    }

    private static void Client_ForceSearchMode(Inventory inv)
    {
        if (!inv) return;

        if (!LootSearchWorldGate.IsWorldLootByInventory(inv))
            return;

        var needInspection = LootSearchWorldGate.GetNeedInspection(inv);
        var shouldSearch = needInspection;

        try
        {
            if (CoopSyncDatabase.Loot.TryGetByInventory(inv, out var entry) && entry?.Lootbox)
                shouldSearch |= entry.Lootbox.needInspect;
        }
        catch
        {
        }

        if (!shouldSearch)
            return;

        var inspected = false;
        try
        {
            inspected = inv.hasBeenInspectedInLootBox;
        }
        catch
        {
        }

        // 已经搜索完成：保持已完成状态，避免再次加迷雾
        if (inspected)
        {
            if (needInspection)
                LootSearchWorldGate.TrySetNeedInspection(inv, false);
            return;
        }

        LootSearchWorldGate.TrySetNeedInspection(inv, true);
        LootSearchWorldGate.ForceTopLevelUninspected(inv);

        try
        {
            inv.hasBeenInspectedInLootBox = false;
        }
        catch
        {
        }

        try
        {
            if (CoopSyncDatabase.Loot.TryGetByInventory(inv, out var entry) && entry?.Lootbox)
                entry.Lootbox.needInspect = true;
        }
        catch
        {
        }
    }

    public void Client_ApplyLootboxState(LootStateRpc rpc)
    {
        var lm = LootManagerInstance;
        if (IsServer || lm == null) return;

        var inv = lm.ResolveLootInv(rpc.Id.Scene, rpc.Id.PositionKey, rpc.Id.InstanceId, rpc.Id.LootUid);
        if (!inv) return;

        Client_ForceSearchMode(inv);

        var curVersion = GetCurrentVersion(inv);
        if (!rpc.IsSnapshot && rpc.Version != curVersion + 1)
        {
            Client_RequestLootState(inv, true);
            return;
        }

        _cliVersions[inv] = Mathf.Max(rpc.Version, curVersion);

        _applyingLootState = true;
        try
        {
            if (rpc.IsSnapshot)
            {
                if (rpc.Capacity > 0 && inv.Capacity != rpc.Capacity)
                    inv.SetCapacity(rpc.Capacity);

                for (var i = 0; i < rpc.Capacity && i < rpc.Snapshot.Length; i++)
                {
                    var slot = rpc.Snapshot[i];
                    if (!slot.HasItem)
                    {
                        var existing = inv.GetItemAt(i);
                        if (existing != null && IsQuestTagged(existing))
                            continue;

                        inv.RemoveAt(i, out _);
                        continue;
                    }

                    var targetSnap = slot.Item.TypeId == 0
                        ? new ItemSnapshot { TypeId = slot.TypeId, Stack = slot.Stack }
                        : slot.Item;
                    if (targetSnap.Stack == 0)
                        targetSnap.Stack = Mathf.Max(1, slot.Stack);

                    var exist = inv.GetItemAt(i);
                    if (exist != null && IsQuestTagged(exist))
                        continue;

                    if (exist != null && exist.TypeID == targetSnap.TypeId && ItemTool.ApplySnapshot(exist, targetSnap))
                    {
                        ResetInspectionFlags(inv, exist);
                    }
                    else
                    {
                        inv.RemoveAt(i, out _);
                        try
                        {
                            var item = ItemTool.BuildItemFromSnapshot(targetSnap);
                            if (item)
                            {
                                inv.AddAt(item, i);
                                ResetInspectionFlags(inv, item);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[LOOT] instantiate snapshot item failed: {ex.Message}");
                        }
                    }
                }
            }
            else
            {
                if (Client_LootDeltaAlreadyApplied(inv, rpc))
                    return;

                var delta = rpc.Delta;
                if (!delta.HasItem)
                {
                    var existing = inv.GetItemAt(rpc.Slot);
                    if (existing != null && IsQuestTagged(existing))
                        return;

                    inv.RemoveAt(rpc.Slot, out _);
                }
                else
                {
                    var targetSnap = delta.Item.TypeId == 0
                        ? new ItemSnapshot { TypeId = delta.TypeId, Stack = delta.Stack }
                        : delta.Item;
                    if (targetSnap.Stack == 0)
                        targetSnap.Stack = Mathf.Max(1, delta.Stack);

                    var exist = inv.GetItemAt(rpc.Slot);
                    if (exist != null && IsQuestTagged(exist))
                        return;

                    if (exist != null && exist.TypeID == targetSnap.TypeId && ItemTool.ApplySnapshot(exist, targetSnap))
                    {
                        ResetInspectionFlags(inv, exist);
                    }
                    else
                    {
                        inv.RemoveAt(rpc.Slot, out _);
                        try
                        {
                            var item = ItemTool.BuildItemFromSnapshot(targetSnap);
                            if (item)
                            {
                                inv.AddAt(item, rpc.Slot);
                                ResetInspectionFlags(inv, item);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[LOOT] instantiate delta item failed: {ex.Message}");
                        }
                    }
                }
            }
        }
        finally
        {
            _applyingLootState = false;
        }
    }

    public void Client_SendLootPutRequest(Inventory lootInv, Item item, int preferPos)
    {
        if (IsServer || lootInv == null || item == null || connectedPeer == null) return;
        var lm = LootManagerInstance;
        if (lm == null) return;
        if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return;

        var token = _nextLootToken++;
        _cliPendingPut[token] = item;

        var rpc = new LootPutRequestRpc
        {
            Id = lm.BuildLootIdentifier(lootInv),
            Token = token,
            PreferPos = preferPos,
            TypeId = item.TypeID,
            Count = item.StackCount,
            Item = ItemTool.MakeSnapshot(item)
        };

        CoopTool.SendRpc(in rpc);
    }

    public void Client_SendLootTakeRequest(Inventory lootInv, int position)
    {
        Client_SendLootTakeRequest(lootInv, position, null, -1, null);
    }

    public uint Client_SendLootTakeRequest(Inventory lootInv, int position, Inventory destInv, int destPos, Slot destSlot)
    {
        var lm = LootManagerInstance;
        if (IsServer || lootInv == null || connectedPeer == null || lm == null) return 0;
        if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return 0;

        var token = _nextLootToken++;

        var rpc = new LootTakeRequestRpc
        {
            Id = lm.BuildLootIdentifier(lootInv),
            Token = token,
            Position = position,
            PreferDest = destPos
        };

        CoopTool.SendRpc(in rpc);

        return token;
    }

    public void Server_HandleLootPutRequest(RpcContext context, LootPutRequestRpc rpc)
    {
        var lm = LootManagerInstance;
        var peer = context.Sender;
        if (!context.IsServer || peer == null || lm == null) return;

        var inv = lm.ResolveLootInv(rpc.Id.Scene, rpc.Id.PositionKey, rpc.Id.InstanceId, rpc.Id.LootUid);
        if (!LootboxDetectUtil.IsLootboxInventory(inv) || inv == null)
        {
            Server_SendLootDeny(peer, "no_inv");
            return;
        }

        try
        {
            _serverApplyingLoot = true;
            var preferSlot = rpc.PreferPos;
            var existing = preferSlot >= 0 ? inv.GetItemAt(preferSlot) : null;
            if (existing != null && existing.TypeID != rpc.TypeId)
            {
                // The hinted slot is occupied by another item; ignore the hint to avoid AddAt errors.
                preferSlot = -1;
                existing = null;
            }

            if (existing != null && IsQuestTagged(existing))
            {
                Server_SendLootDeny(peer, "quest_item");
                return;
            }

            if (existing != null && existing.TypeID == rpc.TypeId && existing.Stackable)
            {
                var space = Mathf.Max(0, existing.MaxStackCount - existing.StackCount);
                var add = Mathf.Min(space, Mathf.Max(1, rpc.Count));
                if (add > 0)
                {
                    existing.StackCount += add;
                    Server_HookItemSlots(existing);
                    _srvPlayerPlaced.Add(existing);
                    ResetInspectionFlags(inv, existing, true);
                    BroadcastLootDelta(inv, preferSlot, existing.TypeID, existing.StackCount, false);
                    SendLootPutOk(peer, rpc.Token, inv, preferSlot, existing.StackCount);
                    var remain = Mathf.Max(0, rpc.Count - add);
                    if (remain <= 0)
                        return;
                    rpc.Count = remain;
                }
            }

            var item = ItemTool.BuildItemFromSnapshot(rpc.Item.TypeId == 0
                ? new ItemSnapshot { TypeId = rpc.TypeId, Stack = rpc.Count }
                : rpc.Item);
            if (item == null)
            {
                Server_SendLootDeny(peer, "bad_snapshot");
                return;
            }

            if (IsQuestTagged(item))
            {
                UnityEngine.Object.Destroy(item.gameObject);
                Server_SendLootDeny(peer, "quest_item");
                return;
            }

            item.StackCount = Mathf.Max(1, rpc.Count);
            _srvPlayerPlaced.Add(item);
            Server_HookItemSlots(item);
            ResetInspectionFlags(inv, item, true);

            if (preferSlot < 0)
            {
                for (var i = 0; i < inv.Capacity; i++)
                {
                    var candidate = inv.GetItemAt(i);
                    if (candidate == null || candidate == item) continue;
                    if (candidate.TypeID != item.TypeID || !candidate.Stackable) continue;
                    var space = Mathf.Max(0, candidate.MaxStackCount - candidate.StackCount);
                    if (space <= 0) continue;
                    var add = Mathf.Min(space, item.StackCount);
                    candidate.StackCount += add;
                    Server_HookItemSlots(candidate);
                    BroadcastLootDelta(inv, i, candidate.TypeID, candidate.StackCount, false);
                    SendLootPutOk(peer, rpc.Token, inv, i, candidate.StackCount);
                    item.StackCount -= add;
                    if (item.StackCount <= 0)
                    {
                        UnityEngine.Object.Destroy(item.gameObject);
                        return;
                    }
                }
            }

            var ok = preferSlot >= 0 ? inv.AddAt(item, preferSlot) : inv.AddItem(item);
            if (!ok)
            {
                UnityEngine.Object.Destroy(item.gameObject);
                Server_SendLootDeny(peer, "full");
                return;
            }

            var slot = preferSlot >= 0 ? preferSlot : inv.GetLastItemPosition();
            BroadcastLootDelta(inv, slot, item.TypeID, item.StackCount, false);
            SendLootPutOk(peer, rpc.Token, inv, slot, item.StackCount);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LOOT] put request failed: {ex.Message}");
            Server_SendLootDeny(peer, "rm_fail");
        }
        finally
        {
            _serverApplyingLoot = false;
        }
    }

    public void Server_HandleLootTakeRequest(RpcContext context, LootTakeRequestRpc rpc)
    {
        var lm = LootManagerInstance;
        var peer = context.Sender;
        if (!context.IsServer || peer == null || lm == null) return;

        var inv = lm.ResolveLootInv(rpc.Id.Scene, rpc.Id.PositionKey, rpc.Id.InstanceId, rpc.Id.LootUid);
        if (!LootboxDetectUtil.IsLootboxInventory(inv) || inv == null)
        {
            Server_SendLootDeny(peer, "no_inv");
            return;
        }

        _serverApplyingLoot = true;
        try
        {
            var item = inv.GetItemAt(rpc.Position);
            if (item == null)
            {
                Server_SendLootDeny(peer, "no_item");
                return;
            }

            if (IsQuestTagged(item))
            {
                Server_SendLootDeny(peer, "quest_item");
                return;
            }

            var typeId = item.TypeID;
            var stack = item.StackCount;

            // Always treat client takes as full-slot transfers; stacking/splitting will be handled by explicit split RPCs later.
            inv.RemoveAt(rpc.Position, out _);

            BroadcastLootDelta(inv, rpc.Position, -1, 0, true);
            SendLootTakeOk(peer, rpc.Token, inv, rpc.Position, typeId, stack);
        }
        finally
        {
            _serverApplyingLoot = false;
        }
    }

    private void BroadcastLootDelta(Inventory inv, int slot, int typeId, int count, bool cleared)
    {
        var lm = LootManagerInstance;
        if (!IsServer || inv == null || lm == null) return;

        Item item = null;
        try
        {
            item = !cleared ? inv.GetItemAt(slot) : null;
        }
        catch
        {
        }

        if (item != null && IsQuestTagged(item))
            return;

        var rpc = new LootStateRpc
        {
            Id = lm.BuildLootIdentifier(inv),
            IsSnapshot = false,
            Version = NextVersion(inv),
            Slot = slot,
            Delta = new LootSlotSnapshot
            {
                HasItem = !cleared,
                TypeId = typeId,
                Stack = count,
                Item = item ? ItemTool.MakeSnapshot(item) : default
            }
        };

        if (_srvViewers.TryGetValue(inv, out var viewers) && viewers.Count > 0)
        {
            foreach (var viewer in viewers)
                CoopTool.SendRpcTo(viewer, in rpc);
        }
        else
        {
            CoopTool.SendRpc(in rpc);
        }
    }

    public void Server_SendLootDeny(NetPeer peer, string reason)
    {
        if (!IsServer || peer == null) return;
        var rpc = new LootDenyRpc { Reason = reason ?? string.Empty };
        CoopTool.SendRpcTo(peer, in rpc);
    }

    private static ItemSnapshot NormalizeDeltaSnapshot(in LootSlotSnapshot slot)
    {
        var snap = slot.Item;
        if (snap.TypeId == 0)
            snap.TypeId = slot.TypeId;
        if (snap.Stack == 0)
            snap.Stack = Mathf.Max(1, slot.Stack);
        if (slot.Item.HasDurability && !snap.HasDurability)
        {
            snap.HasDurability = true;
            snap.Durability = slot.Item.Durability;
        }
        return snap;
    }

    private bool Client_LootDeltaAlreadyApplied(Inventory inv, LootStateRpc rpc)
    {
        if (inv == null || rpc.IsSnapshot) return false;

        var delta = rpc.Delta;
        if (!delta.HasItem)
            return inv.GetItemAt(rpc.Slot) == null;

        var existing = inv.GetItemAt(rpc.Slot);
        if (existing == null) return false;

        var snap = NormalizeDeltaSnapshot(in delta);
        return ItemTool.SnapshotMatches(existing, snap);
    }

    private void SendLootPutOk(NetPeer peer, uint token, Inventory inv, int slot, int stack)
    {
        var lm = LootManagerInstance;
        if (!IsServer || peer == null || lm == null) return;
        var rpc = new LootPutOkRpc
        {
            Id = lm.BuildLootIdentifier(inv),
            Token = token,
            Slot = slot,
            Stack = stack
        };

        CoopTool.SendRpcTo(peer, in rpc);
    }

    private void SendLootTakeOk(NetPeer peer, uint token, Inventory inv, int slot, int typeId, int stack)
    {
        var lm = LootManagerInstance;
        if (!IsServer || peer == null || lm == null) return;
        var rpc = new LootTakeOkRpc
        {
            Id = lm.BuildLootIdentifier(inv),
            Token = token,
            Slot = slot,
            TypeId = typeId,
            Stack = stack
        };

        CoopTool.SendRpcTo(peer, in rpc);
    }

    public void Client_OnLootPutOk(LootPutOkRpc rpc)
    {
        var lm = LootManagerInstance;
        if (IsServer || lm == null) return;
        var inv = lm.ResolveLootInv(rpc.Id.Scene, rpc.Id.PositionKey, rpc.Id.InstanceId, rpc.Id.LootUid);
        if (inv && _cliVersions.TryGetValue(inv, out var v)) _cliVersions[inv] = v; // keep version aligned
        _cliPendingPut.Remove(rpc.Token);

        if (!inv) return;
        var item = inv.GetItemAt(rpc.Slot);
        if (item) item.StackCount = Mathf.Max(1, rpc.Stack);
    }

    public void Client_OnLootTakeOk(LootTakeOkRpc rpc)
    {
        var lm = LootManagerInstance;
        if (IsServer || lm == null) return;
        var inv = lm.ResolveLootInv(rpc.Id.Scene, rpc.Id.PositionKey, rpc.Id.InstanceId, rpc.Id.LootUid);
        if (inv && _cliVersions.TryGetValue(inv, out var v)) _cliVersions[inv] = v;

        if (inv)
        {
            var item = inv.GetItemAt(rpc.Slot);
            if (rpc.TypeId < 0)
                inv.RemoveAt(rpc.Slot, out _);
            else if (item != null && item.TypeID == rpc.TypeId)
                item.StackCount = Mathf.Max(1, rpc.Stack);
        }

        lm._cliPendingTake.Remove(rpc.Token);
    }

    private static void ApplyNestedSlots(Item target, ItemSnapshot snapshot)
    {
        if (target == null || snapshot.TypeId == 0) return;

        try
        {
            var slots = target.Slots;
            if (slots == null || slots.Count == 0) return;

            foreach (var slot in slots)
            {
                if (slot == null) continue;

                ItemSlotSnapshot snap = default;
                var hasSnap = snapshot.Slots != null;
                if (hasSnap)
                {
                    snap = snapshot.Slots.FirstOrDefault(s => s.Key == slot.Key);
                    hasSnap = !string.IsNullOrEmpty(snap.Key) || snap.HasItem || snap.Item.TypeId != 0;
                }

                if (hasSnap && snap.HasItem)
                {
                    var child = slot.Content;
                    if (child != null && child.TypeID == snap.Item.TypeId)
                    {
                        ItemTool.ApplySnapshot(child, snap.Item);
                    }
                    else
                    {
                        if (child != null) slot.Unplug();
                        var newChild = ItemTool.BuildItemFromSnapshot(snap.Item);
                        if (newChild != null)
                            slot.Plug(newChild, out _);
                    }
                }
                else if (slot.Content != null)
                {
                    slot.Unplug();
                }
            }
        }
        catch
        {
        }
    }

    public static void Client_ApplyLootVisibility(Dictionary<int, bool> vis) { }
    public static void Client_ApplyLootVisibilityChunk(int[] keys, bool[] states, bool reset)
    {
        var lootNet = COOPManager.LootNet;
        if (lootNet == null || lootNet.IsServer) return;
        lootNet.Client_ApplyLootVisibilityInternal(keys, states, reset);
    }

    private void Client_ApplyLootVisibilityInternal(int[] keys, bool[] states, bool reset)
    {
        if (IsServer) return;

        if (reset)
            _cliLootVisibility.Clear();

        var count = Math.Min(keys?.Length ?? 0, states?.Length ?? 0);
        if (count <= 0) return;

        var core = Duckov.Scenes.MultiSceneCore.Instance;

        for (var i = 0; i < count; i++)
        {
            var key = keys[i];
            var active = states[i];
            if (key == 0) continue;

            _cliLootVisibility[key] = active;

            try
            {
                if (core?.inLevelData != null)
                    core.inLevelData[key] = active;
            }
            catch
            {
            }

            try
            {
                if (CoopSyncDatabase.Loot.TryGetByPositionKey(key, out var entry) && entry != null)
                {
                    if (ItemTool.InventoryHasQuestItem(entry.Inventory))
                        continue;

                    var go = entry.Loader ? entry.Loader.gameObject : entry.Lootbox ? entry.Lootbox.gameObject : null;
                    if (go && go.activeSelf != active)
                        go.SetActive(active);
                }
            }
            catch
            {
            }
        }
    }

    internal bool Client_TryGetCachedVisibility(int key, out bool active)
    {
        active = false;
        if (IsServer || key == 0) return false;
        return _cliLootVisibility.TryGetValue(key, out active);
    }

    public void Client_SendLootSplitRequest(Inventory lootInv, int srcPos, int newStack)
    {
        var service = Service;
        if (service == null || service.IsServer || !service.networkStarted) return;
        if (lootInv == null || !LootboxDetectUtil.IsLootboxInventory(lootInv) || LootboxDetectUtil.IsPrivateInventory(lootInv))
            return;

        var item = lootInv.GetItemAt(srcPos);
        var lm = LootManagerInstance;
        if (lm == null || item == null) return;

        var rpc = new LootStackRequestRpc
        {
            Id = lm.BuildLootIdentifier(lootInv),
            Slot = srcPos,
            Stack = Mathf.Max(1, newStack),
            TypeId = item.TypeID
        };

        CoopTool.SendRpc(in rpc);
    }

    public void Server_HandleLootSplitRequest(RpcContext context, LootStackRequestRpc rpc)
    {
        var lm = LootManagerInstance;
        if (!context.IsServer || lm == null) return;

        var inv = lm.ResolveLootInv(rpc.Id.Scene, rpc.Id.PositionKey, rpc.Id.InstanceId, rpc.Id.LootUid);
        if (inv == null || !LootboxDetectUtil.IsLootboxInventory(inv)) return;

        _serverApplyingLoot = true;
        try
        {
            var item = inv.GetItemAt(rpc.Slot);
            if (item == null || item.TypeID != rpc.TypeId) return;

            if (IsQuestTagged(item))
                return;

            item.StackCount = Mathf.Max(1, rpc.Stack);
            BroadcastLootDelta(inv, rpc.Slot, item.TypeID, item.StackCount, false);
        }
        finally
        {
            _serverApplyingLoot = false;
        }
    }

    public void Client_RequestLootSlotPlug(Inventory inv, Item master, string slotKey, Item child)
    {
        if (IsServer || inv == null || master == null || child == null || string.IsNullOrEmpty(slotKey)) return;
        if (!networkStarted || connectedPeer == null) return;
        if (!LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv)) return;
        if (!Client_ShouldSendLootChange(inv)) return;

        var lm = LootManagerInstance;
        if (lm == null) return;

        var pos = inv.GetIndex(master);
        if (pos < 0) return;

        var sourceSlotKey = child.PluggedIntoSlot != null ? child.PluggedIntoSlot.Key : string.Empty;
        if (string.IsNullOrEmpty(sourceSlotKey))
            sourceSlotKey = Client_ConsumeSlotOrigin(child);

        var rpc = new LootSlotPlugRequestRpc
        {
            Id = lm.BuildLootIdentifier(inv),
            ParentSlot = pos,
            SlotKey = slotKey,
            SourceSlotKey = sourceSlotKey,
            Child = ItemTool.MakeSnapshot(child)
        };

        CoopTool.SendRpc(in rpc);
    }

    public void Client_RequestLootSlotUnplug(Inventory inv, Item master, string slotKey)
    {
        if (IsServer || inv == null || master == null || string.IsNullOrEmpty(slotKey)) return;
        if (!networkStarted || connectedPeer == null) return;
        if (!LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv)) return;
        if (!Client_ShouldSendLootChange(inv)) return;

        var lm = LootManagerInstance;
        if (lm == null) return;

        var pos = inv.GetIndex(master);
        if (pos < 0) return;

        var rpc = new LootSlotUnplugRequestRpc
        {
            Id = lm.BuildLootIdentifier(inv),
            ParentSlot = pos,
            SlotKey = slotKey
        };

        CoopTool.SendRpc(in rpc);
    }

    public void Client_RequestLootSlotSnapshot(Inventory inv, Item master)
    {
        if (IsServer || inv == null || master == null) return;
        if (!networkStarted || connectedPeer == null) return;
        if (!LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv)) return;
        if (!Client_ShouldSendLootChange(inv)) return;

        var lm = LootManagerInstance;
        if (lm == null) return;

        var pos = inv.GetIndex(master);
        if (pos < 0) return;

        var rpc = new LootSlotSnapshotRpc
        {
            Id = lm.BuildLootIdentifier(inv),
            ParentSlot = pos,
            Snapshot = ItemTool.MakeSnapshot(master)
        };

        CoopTool.SendRpc(in rpc);
    }

    public void Server_HandleLootSlotPlugRequest(RpcContext context, LootSlotPlugRequestRpc rpc)
    {
        var lm = LootManagerInstance;
        if (!context.IsServer || lm == null) return;

        var inv = lm.ResolveLootInv(rpc.Id.Scene, rpc.Id.PositionKey, rpc.Id.InstanceId, rpc.Id.LootUid);
        if (inv == null || !LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv)) return;

        _serverApplyingLoot = true;
        try
        {
            var master = inv.GetItemAt(rpc.ParentSlot);
            if (master == null) return;

            if (IsQuestTagged(master))
                return;

            Server_HookItemSlots(master);

            var slot = master.Slots?.GetSlot(rpc.SlotKey);
            if (slot == null) return;

            Slot srcSlot = null;
            Item moving = null;
            Item originalSrcContent = null;
            Item originalTargetContent = slot.Content;

            if (!string.IsNullOrEmpty(rpc.SourceSlotKey) && master.Slots != null)
            {
                srcSlot = master.Slots.GetSlot(rpc.SourceSlotKey);
                if (srcSlot != null && srcSlot != slot)
                {
                    originalSrcContent = srcSlot.Content;
                    moving = originalSrcContent;
                    if (moving)
                        srcSlot.Unplug();
                }
            }

            moving = moving ?? ItemTool.BuildItemFromSnapshot(rpc.Child);
            if (moving == null) return;

            if (IsQuestTagged(moving))
                return;

            _srvPlayerPlaced.Add(moving);
            Server_HookItemSlots(moving);

            var plugged = slot.Plug(moving, out var unplugged);

            if (!plugged)
            {
                if (srcSlot != null && srcSlot != slot && srcSlot.Content == null && originalSrcContent != null)
                    srcSlot.Plug(originalSrcContent, out _);

                return;
            }

            if (unplugged != null && unplugged != moving)
            {
                var placed = false;

                if (srcSlot != null && srcSlot != slot)
                {
                    if (srcSlot.Plug(unplugged, out var swappedBack))
                    {
                        placed = swappedBack == null;
                        if (swappedBack != null && swappedBack != unplugged && swappedBack != moving)
                        {
                            try { UnityEngine.Object.Destroy(swappedBack.gameObject); } catch { }
                        }
                    }
                }

                if (!placed)
                {
                    Slot fallback = null;
                    try
                    {
                        var slots = master.Slots;
                        if (slots != null)
                        {
                            foreach (var candidate in slots)
                            {
                                if (candidate == null || candidate == slot || candidate == srcSlot) continue;
                                if (candidate.Content == null)
                                {
                                    fallback = candidate;
                                    break;
                                }
                            }
                        }
                    }
                    catch
                    {
                    }

                    if (fallback != null && fallback.Plug(unplugged, out var swapped))
                    {
                        placed = swapped == null;
                        if (swapped != null && swapped != unplugged && swapped != moving)
                        {
                            try { UnityEngine.Object.Destroy(swapped.gameObject); } catch { }
                        }
                    }
                }

                if (!placed)
                {
                    // Revert to the pre-plug state to avoid swallowing the displaced item.
                    slot.Unplug();

                    if (originalTargetContent != null && slot.Content == null)
                        slot.Plug(originalTargetContent, out _);

                    if (srcSlot != null && srcSlot != slot && srcSlot.Content == null && originalSrcContent != null)
                        srcSlot.Plug(originalSrcContent, out _);

                    return;
                }
            }

            ResetInspectionFlags(inv, master, true);
            BroadcastLootDelta(inv, rpc.ParentSlot, master.TypeID, master.StackCount, false);
        }
        finally
        {
            _serverApplyingLoot = false;
        }
    }

    public void Server_HandleLootSlotSnapshot(RpcContext context, LootSlotSnapshotRpc rpc)
    {
        var lm = LootManagerInstance;
        if (!context.IsServer || lm == null) return;

        var inv = lm.ResolveLootInv(rpc.Id.Scene, rpc.Id.PositionKey, rpc.Id.InstanceId, rpc.Id.LootUid);
        if (inv == null || !LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv)) return;

        _serverApplyingLoot = true;
        try
        {
            var master = inv.GetItemAt(rpc.ParentSlot);
            if (master == null)
                return;

            if (IsQuestTagged(master))
                return;

            Server_HookItemSlots(master);

            if (master.TypeID != rpc.Snapshot.TypeId)
            {
                inv.RemoveAt(rpc.ParentSlot, out _);
                var rebuilt = ItemTool.BuildItemFromSnapshot(rpc.Snapshot);
                if (rebuilt == null) return;

                if (IsQuestTagged(rebuilt))
                    return;

                Server_MarkItemTreePlayerPlaced(rebuilt);
                Server_HookItemSlots(rebuilt);
                if (!inv.AddAt(rebuilt, rpc.ParentSlot))
                {
                    try { UnityEngine.Object.Destroy(rebuilt.gameObject); } catch { }
                    return;
                }

                master = rebuilt;
            }
            else
            {
                if (!ItemTool.ApplySnapshot(master, rpc.Snapshot))
                    return;

                Server_MarkItemTreePlayerPlaced(master);
            }

            ResetInspectionFlags(inv, master, true);
            BroadcastLootDelta(inv, rpc.ParentSlot, master.TypeID, master.StackCount, false);
        }
        finally
        {
            _serverApplyingLoot = false;
        }
    }

    public void Server_HandleLootSlotUnplugRequest(RpcContext context, LootSlotUnplugRequestRpc rpc)
    {
        var lm = LootManagerInstance;
        if (!context.IsServer || lm == null) return;

        var inv = lm.ResolveLootInv(rpc.Id.Scene, rpc.Id.PositionKey, rpc.Id.InstanceId, rpc.Id.LootUid);
        if (inv == null || !LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv)) return;

        _serverApplyingLoot = true;
        try
        {
            var master = inv.GetItemAt(rpc.ParentSlot);
            if (master == null) return;

            var slot = master.Slots?.GetSlot(rpc.SlotKey);
            if (slot == null) return;

            var unplugged = slot.Unplug();
            if (unplugged == null) return;

            ResetInspectionFlags(inv, master, true);
            BroadcastLootDelta(inv, rpc.ParentSlot, master.TypeID, master.StackCount, false);
        }
        finally
        {
            _serverApplyingLoot = false;
        }
    }

    public struct PendingTakeDest
    {
        public Inventory inv;
        public int pos;
        public Slot slot;
        public Inventory srcLoot;
        public int srcPos;
    }
}
