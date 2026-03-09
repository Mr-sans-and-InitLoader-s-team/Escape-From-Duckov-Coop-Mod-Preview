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
using LiteNetLib;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public sealed class ItemNet
{
    [ThreadStatic] public static bool InNetworkDrop;

    private readonly Queue<ItemDropSnapshotEntry> _snapshotQueue = new();
    private bool _snapshotResetPending;
    private bool _snapshotInProgress;

    private readonly Dictionary<uint, uint> _pendingByToken = new();
    private readonly HashSet<uint> _pendingPickups = new();
    private uint _nextDropId = 1;

    public void Server_Update(float deltaTime)
    {
    }

    public void Client_Update(float deltaTime)
    {
        if (!_snapshotInProgress)
            return;

        if (_snapshotResetPending)
        {
            _snapshotResetPending = false;
            _nextDropId = 1;

            foreach (var entry in CoopSyncDatabase.Drops.Entries.ToArray())
            {
                try
                {
                    if (entry.Agent)
                    {
                        UnityEngine.Object.Destroy(entry.Agent);
                    }
                    else if (entry.Item && !entry.Item.InInventory)
                    {
                        entry.Item.DestroyTree();
                    }
                }
                catch
                {
                }
            }

        CoopSyncDatabase.Drops.Clear();
        _pendingPickups.Clear();
    }

        const int maxSpawnsPerFrame = 6;
        var processed = 0;

        while (processed < maxSpawnsPerFrame && _snapshotQueue.Count > 0)
        {
            processed++;
            var entry = _snapshotQueue.Dequeue();

            var item = ItemTool.BuildItemFromSnapshot(entry.Snapshot);
            if (item == null) continue;

            DuckovItemAgent agent = null;
            try
            {
                InNetworkDrop = true;
                agent = item.Drop(entry.Position, entry.CreateRigidbody, entry.Direction, entry.Angle);
            }
            finally
            {
                InNetworkDrop = false;
            }

            ItemTool.AddNetDropTag(agent ? agent.gameObject : item.gameObject, entry.DropId);
            CoopSyncDatabase.Drops.Register(entry.DropId, item, agent ? agent.gameObject : item.gameObject);
            _nextDropId = Math.Max(_nextDropId, entry.DropId + 1);
        }

        if (_snapshotQueue.Count == 0 && !_snapshotResetPending)
            _snapshotInProgress = false;
    }

    public void Reset()
    {
        _snapshotQueue.Clear();
        _snapshotResetPending = false;
        _snapshotInProgress = false;
        _pendingByToken.Clear();
        _nextDropId = 1;
    }

    public void Server_RebuildDropRegistryFromScene()
    {
        if (!NetService.Instance.IsServer || NetService.Instance?.networkStarted != true)
            return;

        var seenDropIds = new HashSet<uint>();
        CoopSyncDatabase.Drops.Clear();

        foreach (var tag in UnityEngine.Object.FindObjectsOfType<NetDropTag>(true))
        {
            if (tag == null) continue;

            if (ItemTool.HasQuestTag(tag.gameObject))
                continue;

            var dropId = tag.id;
            if (dropId == 0) continue;

            if (!seenDropIds.Add(dropId))
                continue;

            var item = ResolveItemFromTag(tag);
            if (item == null) continue;

            CoopSyncDatabase.Drops.Register(dropId, item.Item, tag.gameObject);
            _nextDropId = Math.Max(_nextDropId, dropId + 1);
        }
    }

    public void Client_RequestDropSnapshot(bool includeReset = true)
    {
        var service = NetService.Instance;
        if (service == null || service.IsServer || !service.networkStarted) return;

        var rpc = new ItemDropSnapshotRequestRpc
        {
            Reset = includeReset
        };

        CoopTool.SendRpc(in rpc);
    }

    public void Server_HandleDropRequest(RpcContext context, ItemDropRequestRpc message)
    {
        if (!context.IsServer) return;

        var item = ItemTool.BuildItemFromSnapshot(message.Snapshot);
        if (item == null) return;

        if (ItemTool.HasQuestTag(item))
            return;

        DuckovItemAgent agent = null;
        try
        {
            InNetworkDrop = true;
            agent = item.Drop(message.Position, message.CreateRigidbody, message.Direction, message.Angle);
        }
        finally
        {
            InNetworkDrop = false;
        }

        var dropId = _nextDropId++;
        CoopSyncDatabase.Drops.Register(dropId, item, agent ? agent.gameObject : item.gameObject);
        ItemTool.AddNetDropTag(agent ? agent.gameObject : item.gameObject, dropId);

        var rpc = new ItemSpawnRpc
        {
            Token = message.Token,
            DropId = dropId,
            Position = message.Position,
            Direction = message.Direction,
            Angle = message.Angle,
            CreateRigidbody = message.CreateRigidbody,
            Snapshot = ItemTool.MakeSnapshot(item)
        };

        CoopTool.SendRpc(in rpc);
    }

    public void Client_HandleSpawn(ItemSpawnRpc message)
    {
        if (NetService.Instance?.IsServer == true) return;

        var item = ItemTool.BuildItemFromSnapshot(message.Snapshot);
        if (item == null) return;

        DuckovItemAgent agent = null;
        try
        {
            InNetworkDrop = true;
            agent = item.Drop(message.Position, message.CreateRigidbody, message.Direction, message.Angle);
        }
        finally
        {
            InNetworkDrop = false;
        }

        ItemTool.AddNetDropTag(agent ? agent.gameObject : item.gameObject, message.DropId);
        CoopSyncDatabase.Drops.Register(message.DropId, item, agent ? agent.gameObject : item.gameObject);
    }

    public void Server_RegisterLocalDrop(Item item, DuckovItemAgent agent, Vector3 pos, bool createRigidbody, Vector3 dir, float angle)
    {
        var service = NetService.Instance;
        if (service == null || !service.IsServer || !service.networkStarted || item == null) return;

        var dropGo = agent ? agent.gameObject : item.gameObject;

        // If this world item is already tracked as a network drop, don't allocate a new
        // drop id or broadcast an extra spawn. This can happen when host pickup fails
        // (e.g. backpack full) and gameplay code re-runs drop placement for the same item.
        if (CoopSyncDatabase.Drops.TryGetByItem(item, out var existingByItem) && existingByItem != null)
        {
            CoopSyncDatabase.Drops.Register(existingByItem.DropId, item, dropGo);
            ItemTool.AddNetDropTag(dropGo, existingByItem.DropId);
            return;
        }

        var existingTag = dropGo ? dropGo.GetComponent<NetDropTag>() : null;
        if (existingTag != null && existingTag.id != 0 &&
            CoopSyncDatabase.Drops.TryGetById(existingTag.id, out var existingById) && existingById != null)
        {
            CoopSyncDatabase.Drops.Register(existingById.DropId, item, dropGo);
            return;
        }

        if (ItemTool.HasQuestTag(item) || (agent && ItemTool.HasQuestTag(agent.gameObject)))
            return;

        var dropId = _nextDropId++;
        CoopSyncDatabase.Drops.Register(dropId, item, dropGo);
        ItemTool.AddNetDropTag(dropGo, dropId);

        var spawnRpc = new ItemSpawnRpc
        {
            DropId = dropId,
            Snapshot = ItemTool.MakeSnapshot(item),
            Position = pos,
            Direction = dir,
            Angle = angle,
            CreateRigidbody = createRigidbody
        };

        CoopTool.SendRpc(in spawnRpc);
    }

    public void Server_HandlePickupRequest(RpcContext context, ItemPickupRequestRpc message)
    {
        if (!context.IsServer) return;

        if (!CoopSyncDatabase.Drops.TryGetById(message.DropId, out var entry) || entry == null)
            return;

        CoopSyncDatabase.Drops.Unregister(message.DropId);

        try
        {
            if (entry.Agent)
                UnityEngine.Object.Destroy(entry.Agent);
        }
        catch
        {
        }

        var rpc = new ItemDespawnRpc { DropId = message.DropId };
        CoopTool.SendRpc(in rpc);
    }

    public void Server_HandleLocalPickup(Item item)
    {
        if (item == null) return;
        var service = NetService.Instance;
        if (service == null || !service.IsServer || !service.networkStarted) return;

        if (!CoopSyncDatabase.Drops.TryGetByItem(item, out var entry) || entry == null)
            return;

        CoopSyncDatabase.Drops.Unregister(entry.DropId);

        _pendingPickups.Remove(entry.DropId);

        try
        {
            if (entry.Agent)
                UnityEngine.Object.Destroy(entry.Agent);
        }
        catch
        {
        }

        var rpc = new ItemDespawnRpc { DropId = entry.DropId };
        CoopTool.SendRpc(in rpc);
    }

    public void Client_HandleDespawn(ItemDespawnRpc message)
    {
        if (NetService.Instance?.IsServer == true) return;
        if (!CoopSyncDatabase.Drops.TryGetById(message.DropId, out var entry) || entry == null)
            return;

        var isLocalPickup = _pendingPickups.Remove(message.DropId);

        try
        {
            if (!isLocalPickup)
            {
                if (entry.Agent)
                {
                    UnityEngine.Object.Destroy(entry.Agent);
                }
                else if (entry.Item)
                {
                    // If the item is already in an inventory (e.g., because this client
                    // picked it up locally), don't destroy it—just let the drop entry be
                    // cleared so the pickup persists in the player's backpack.
                    if (!entry.Item.InInventory)
                    {
                        entry.Item.DestroyTree();
                    }
                }
            }
        }
        catch
        {
        }

        CoopSyncDatabase.Drops.Unregister(message.DropId);
    }

    public void Server_HandleDropSnapshotRequest(RpcContext context, ItemDropSnapshotRequestRpc message)
    {
        if (!context.IsServer || context.Sender == null) return;

        Server_SendDropSnapshotTo(context.Sender, message.Reset);
    }

    public void Server_BroadcastDropSnapshot()
    {
        var service = NetService.Instance;
        if (service == null || !service.IsServer || !service.networkStarted) return;

        var peers = service.netManager?.ConnectedPeerList;
        if (peers == null) return;

        foreach (var peer in peers)
        {
            if (peer == null) continue;
            Server_SendDropSnapshotTo(peer, true);
        }
    }

    public void Client_HandleDropSnapshotChunk(ItemDropSnapshotChunkRpc message)
    {
        if (NetService.Instance?.IsServer == true) return;

        var entries = message.Entries;
        if (entries == null) return;

        if (message.Reset)
        {
            _snapshotQueue.Clear();
            _snapshotResetPending = true;
        }

        foreach (var entry in entries)
        {
            _snapshotQueue.Enqueue(entry);
        }

        if (message.IsLast && _snapshotQueue.Count == 0 && _snapshotResetPending)
        {
            // Handle empty snapshots immediately.
            _snapshotInProgress = true;
        }
        else if (_snapshotQueue.Count > 0 || message.Reset)
        {
            _snapshotInProgress = true;
        }
    }

    private void Server_SendDropSnapshotTo(NetPeer target, bool includeReset)
    {
        if (!NetService.Instance.IsServer || target == null) return;

        Server_RebuildDropRegistryFromScene();

        var entries = CoopSyncDatabase.Drops.Entries
            .Where(e => e != null && !ItemTool.HasQuestTag(e.Agent ? e.Agent.gameObject : e.Item ? e.Item.gameObject : null))
            .ToArray();
        const int chunkSize = 12;

        if (entries.Length == 0)
        {
            if (includeReset)
            {
                var empty = new ItemDropSnapshotChunkRpc
                {
                    Version = 1,
                    Reset = true,
                    IsLast = true,
                    Entries = Array.Empty<ItemDropSnapshotEntry>()
                };

                CoopTool.SendRpcTo(target, in empty);
            }

            return;
        }

        for (var i = 0; i < entries.Length; i += chunkSize)
        {
            var len = Mathf.Min(chunkSize, entries.Length - i);
            var chunkEntries = new ItemDropSnapshotEntry[len];
            for (var j = 0; j < len; j++)
            {
                var e = entries[i + j];
                var pos = e.Agent ? e.Agent.transform.position : (e.Item ? e.Item.transform.position : Vector3.zero);
                chunkEntries[j] = new ItemDropSnapshotEntry
                {
                    DropId = e.DropId,
                    Position = pos,
                    Direction = Vector3.zero,
                    Angle = 0f,
                    CreateRigidbody = false,
                    Snapshot = ItemTool.MakeSnapshot(e.Item)
                };
            }

            var chunk = new ItemDropSnapshotChunkRpc
            {
                Version = 1,
                Reset = includeReset && i == 0,
                IsLast = i + len >= entries.Length,
                Entries = chunkEntries
            };

            CoopTool.SendRpcTo(target, in chunk);
        }
    }

    public void Client_RequestDrop(Item item, Vector3 pos, bool createRigidbody, Vector3 dir, float angle, bool allowSlotOwned = false)
    {
        var service = NetService.Instance;
        if (service == null || service.IsServer || !service.networkStarted || item == null) return;

        // Ignore requests that come from world items that aren't actually in the client's
        // inventory; repeatedly "dropping" those would just duplicate the same scene loot
        // on the host if the client keeps interacting while full.
        var inv = item.InInventory ?? item.Slots?.Master?.InInventory;
        var hasOwnership = inv != null || (allowSlotOwned && item.Slots != null);
        if (!hasOwnership)
            return;

        if (!allowSlotOwned && !item.InInventory)
            return;

        var token = (uint)UnityEngine.Random.Range(1, int.MaxValue);
        _pendingByToken[token] = token;

        var rpc = new ItemDropRequestRpc
        {
            Token = token,
            Position = pos,
            Direction = dir,
            Angle = angle,
            CreateRigidbody = createRigidbody,
            Snapshot = ItemTool.MakeSnapshot(item)
        };

        CoopTool.SendRpc(in rpc);

        try
        {
            item.Detach();
            item.DestroyTree();
        }
        catch
        {
        }
    }

    public void Client_RequestPickup(Item item)
    {
        var service = NetService.Instance;
        if (service == null || service.IsServer || !service.networkStarted || item == null) return;

        if (!CoopSyncDatabase.Drops.TryGetByItem(item, out var entry) || entry == null)
            return;

        _pendingPickups.Add(entry.DropId);

        var rpc = new ItemPickupRequestRpc { DropId = entry.DropId };
        CoopTool.SendRpc(in rpc);
    }

    private static DuckovItemAgent ResolveItemFromTag(NetDropTag tag)
    {
        if (tag == null) return null;

        var item = tag.GetComponent<DuckovItemAgent>() ?? tag.GetComponentInChildren<DuckovItemAgent>() ?? tag.GetComponentInParent<DuckovItemAgent>();
        return item;
    }
}
