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
using Duckov.Utilities;
using ItemStatsSystem;
using LiteNetLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

public sealed class ItemNet
{
    private const float PICK_RADIUS = 2.5f;
    private const QueryTriggerInteraction PICK_QUERY = QueryTriggerInteraction.Collide;
    private const int PICK_LAYER_MASK = ~0;
    private const int DropSnapshotChunkSize = 24;
    private const float DropSnapshotInterval = 15f;

    private readonly DropSyncRegistry _registry = CoopSyncDatabase.Drops;

    private readonly HashSet<Item> _clientSpawnedFromServer =
        new(ReferenceEqualityComparer<Item>.Instance);

    private readonly HashSet<Item> _serverSpawnedFromClient =
        new(ReferenceEqualityComparer<Item>.Instance);

    private readonly HashSet<uint> _pendingDropTokens = new();
    private readonly Dictionary<uint, Item> _pendingTokenItems = new();
    private readonly List<ItemDropSnapshotEntry> _dropSnapshotBuffer = new();
    private readonly HashSet<uint> _clientSnapshotPendingRemoval = new();
    private readonly List<uint> _clientSnapshotRemovalBuffer = new();

    private readonly Collider[] _overlapBuffer = new Collider[64];

    private uint _nextDropId = 1;
    private uint _nextLocalDropToken = 1;
    private float _serverDropSnapshotTimer;
    private int _dropSnapshotVersionCounter = 1;
    private bool _clientSnapshotActive;
    private int _clientSnapshotVersion;
    private string _clientDropSnapshotSceneId;

    private NetService Service => NetService.Instance;
    private bool IsServer => Service != null && Service.IsServer;
    private bool NetworkStarted => Service != null && Service.networkStarted;
    private NetManager NetManager => Service?.netManager;

    public bool Client_ShouldSuppressLocalDrop(Item item)
    {
        if (item == null) return true;
        if (_clientSpawnedFromServer.Remove(item)) return true;
        return false;
    }

    public bool Server_ShouldSuppressLocalDrop(Item item)
    {
        if (item == null) return true;
        if (_serverSpawnedFromClient.Remove(item)) return true;
        return false;
    }

    public uint Client_NextDropToken()
    {
        var token = ++_nextLocalDropToken;
        if (token == 0) token = ++_nextLocalDropToken;
        return token;
    }

    public void Client_RecordPendingDrop(uint token, Item item)
    {
        if (token == 0 || item == null) return;
        _pendingDropTokens.Add(token);
        _pendingTokenItems[token] = item;
    }

    public void Client_SendDropRequest(uint token, Item item, Vector3 pos, bool createRigidbody,
        Vector3 dropDirection, float randomAngle)
    {
        if (item == null) return;

        var request = new ItemDropRequestRpc
        {
            Token = token,
            Position = pos,
            Direction = dropDirection,
            Angle = randomAngle,
            CreateRigidbody = createRigidbody,
            Snapshot = ItemTool.MakeSnapshot(item)
        };

        CoopTool.SendRpc(in request);
    }

    public void Server_HandleDropRequest(RpcContext context, ItemDropRequestRpc message)
    {
        if (!context.IsServer) return;

        var item = ItemTool.BuildItemFromSnapshot(message.Snapshot);
        if (item == null) return;

        _serverSpawnedFromClient.Add(item);

        DuckovItemAgent agent = null;
        try
        {
            agent = item.Drop(message.Position, message.CreateRigidbody, message.Direction, message.Angle);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ITEM][DROP] server spawn failed: {ex}");
        }

        Server_RegisterDrop(item, agent, message.Position, message.Direction, message.Angle,
            message.CreateRigidbody, message.Token);
    }

    public void Server_RegisterDrop(Item item, DuckovItemAgent agent, Vector3 position, Vector3 direction,
        float angle, bool createRigidbody, uint token)
    {
        if (item == null) return;

        var dropId = AllocateDropId();
        RegisterDropEntry(dropId, item, agent ? agent.gameObject : null, position, direction, angle, createRigidbody);

        var spawn = new ItemSpawnRpc
        {
            Token = token,
            DropId = dropId,
            Position = position,
            Direction = direction,
            Angle = angle,
            CreateRigidbody = createRigidbody,
            Snapshot = ItemTool.MakeSnapshot(item)
        };

        CoopTool.SendRpc(in spawn);
    }

    public void Client_HandleSpawn(ItemSpawnRpc message)
    {
        Item item = null;
        if (message.Token != 0 && TryConsumePendingToken(message.Token, out var pending) && pending != null)
        {
            item = pending;
        }
        else
        {
            item = ItemTool.BuildItemFromSnapshot(message.Snapshot);
            if (item == null) return;

            _clientSpawnedFromServer.Add(item);

            try
            {
                item.Drop(message.Position, message.CreateRigidbody, message.Direction, message.Angle);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ITEM][SPAWN] client drop failed: {ex}");
            }
        }

        if (item == null) return;

        var agent = item.ActiveAgent;
        RegisterDropEntry(message.DropId, item, agent ? agent.gameObject : null, message.Position,
            message.Direction, message.Angle, message.CreateRigidbody);
    }

    public void Client_HandleDespawn(ItemDespawnRpc message)
    {
        if (!_registry.TryGetById(message.DropId, out var entry))
            return;

        DestroyAgent(entry.Item);
        if (entry.Agent)
            Object.Destroy(entry.Agent);

        _registry.Unregister(message.DropId);
    }

    public void Server_HandleDropSnapshotRequest(RpcContext context, ItemDropSnapshotRequestRpc message)
    {
        if (!context.IsServer || context.Sender == null)
            return;

        SendDropSnapshot(context.Sender);
    }

    public void Client_HandleDropSnapshotChunk(ItemDropSnapshotChunkRpc message)
    {
        if (IsServer || !NetworkStarted)
            return;

        if (_clientSnapshotActive && message.Version < _clientSnapshotVersion)
            return;

        if (!_clientSnapshotActive || message.Reset || message.Version != _clientSnapshotVersion)
            Client_BeginDropSnapshot(message.Version);

        var entries = message.Entries;
        if (entries != null)
        {
            for (var i = 0; i < entries.Length; i++)
                Client_EnsureDropFromSnapshot(in entries[i]);
        }

        if (message.IsLast)
            Client_FinalizeDropSnapshot();
    }

    private void Client_RequestDropSnapshot()
    {
        if (IsServer || !NetworkStarted)
            return;

        var request = new ItemDropSnapshotRequestRpc();
        CoopTool.SendRpc(in request);
    }

    private void Client_BeginDropSnapshot(int version)
    {
        _clientSnapshotActive = true;
        _clientSnapshotVersion = version;
        _clientSnapshotPendingRemoval.Clear();
        _clientSnapshotRemovalBuffer.Clear();

        foreach (var entry in _registry.Entries)
        {
            if (entry == null) continue;
            var dropId = entry.DropId;
            if (dropId == 0) continue;
            _clientSnapshotPendingRemoval.Add(dropId);
        }
    }

    private void Client_FinalizeDropSnapshot()
    {
        if (!_clientSnapshotActive)
            return;

        _clientSnapshotRemovalBuffer.Clear();
        foreach (var dropId in _clientSnapshotPendingRemoval)
            _clientSnapshotRemovalBuffer.Add(dropId);

        for (var i = 0; i < _clientSnapshotRemovalBuffer.Count; i++)
            Client_HandleDespawn(new ItemDespawnRpc { DropId = _clientSnapshotRemovalBuffer[i] });

        _clientSnapshotPendingRemoval.Clear();
        _clientSnapshotRemovalBuffer.Clear();
        _clientSnapshotActive = false;
    }

    private void Client_EnsureDropFromSnapshot(in ItemDropSnapshotEntry snapshot)
    {
        if (snapshot.DropId == 0)
            return;

        if (_registry.TryGetById(snapshot.DropId, out var entry) && entry != null && entry.Item)
        {
            entry.Position = snapshot.Position;
            entry.Direction = snapshot.Direction;
            entry.Angle = snapshot.Angle;
            entry.CreateRigidbody = snapshot.CreateRigidbody;
            entry.PendingRemoval = false;
            if (_clientSnapshotActive)
                _clientSnapshotPendingRemoval.Remove(snapshot.DropId);
            return;
        }

        var item = ItemTool.BuildItemFromSnapshot(snapshot.Snapshot);
        if (item == null)
            return;

        _clientSpawnedFromServer.Add(item);
        try
        {
            item.Drop(snapshot.Position, snapshot.CreateRigidbody, snapshot.Direction, snapshot.Angle);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ITEM][SNAPSHOT] client spawn failed: {ex}");
        }

        var agent = item.ActiveAgent;
        RegisterDropEntry(snapshot.DropId, item, agent ? agent.gameObject : null, snapshot.Position,
            snapshot.Direction, snapshot.Angle, snapshot.CreateRigidbody);
        if (_clientSnapshotActive)
            _clientSnapshotPendingRemoval.Remove(snapshot.DropId);
    }

    private void SendDropSnapshot(NetPeer target)
    {
        if (!IsServer || !NetworkStarted)
            return;

        if (target == null && !HasConnectedClients())
            return;

        _dropSnapshotBuffer.Clear();
        foreach (var entry in _registry.Entries)
        {
            if (entry == null) continue;
            var item = entry.Item;
            if (!item) continue;

            ItemDropSnapshotEntry snapshot;
            try
            {
                snapshot = new ItemDropSnapshotEntry
                {
                    DropId = entry.DropId,
                    Position = entry.Position != Vector3.zero ? entry.Position : GetItemPosition(item),
                    Direction = entry.Direction,
                    Angle = entry.Angle,
                    CreateRigidbody = entry.CreateRigidbody,
                    Snapshot = ItemTool.MakeSnapshot(item)
                };
            }
            catch
            {
                continue;
            }

            if (snapshot.Direction == Vector3.zero)
                snapshot.Direction = Vector3.up;

            _dropSnapshotBuffer.Add(snapshot);
        }

        var version = _dropSnapshotVersionCounter++;
        if (_dropSnapshotVersionCounter == 0)
            _dropSnapshotVersionCounter = 1;

        if (_dropSnapshotBuffer.Count == 0)
        {
            var empty = new ItemDropSnapshotChunkRpc
            {
                Version = version,
                Reset = true,
                IsLast = true,
                Entries = Array.Empty<ItemDropSnapshotEntry>()
            };
            SendDropSnapshotChunk(target, in empty);
            return;
        }

        for (var offset = 0; offset < _dropSnapshotBuffer.Count; offset += DropSnapshotChunkSize)
        {
            var count = Math.Min(DropSnapshotChunkSize, _dropSnapshotBuffer.Count - offset);
            var chunkEntries = new ItemDropSnapshotEntry[count];
            for (var i = 0; i < count; i++)
                chunkEntries[i] = _dropSnapshotBuffer[offset + i];

            var rpc = new ItemDropSnapshotChunkRpc
            {
                Version = version,
                Reset = offset == 0,
                IsLast = offset + count >= _dropSnapshotBuffer.Count,
                Entries = chunkEntries
            };
            SendDropSnapshotChunk(target, in rpc);
        }
    }

    private void SendDropSnapshotChunk(NetPeer target, in ItemDropSnapshotChunkRpc message)
    {
        if (target != null)
            CoopTool.SendRpcTo(target, in message);
        else
            CoopTool.SendRpc(in message);
    }

    public void Server_HandlePickupRequest(RpcContext context, ItemPickupRequestRpc message)
    {
        if (!context.IsServer) return;
        if (!_registry.TryGetById(message.DropId, out var entry)) return;

        RemoveDrop(message.DropId, entry);

        var despawn = new ItemDespawnRpc
        {
            DropId = message.DropId
        };

        CoopTool.SendRpc(in despawn);
    }

    public void HandleInventoryItemAdded(Inventory inventory, Item item)
    {
        var service = NetService.Instance;
        if (service == null || item == null) return;

        if (service.IsServer)
            Server_TryHandlePickup(item, null);
        else
            Client_TryHandlePickup(item, null);
    }

    public void HandleSlotItemEquipped(Item item)
    {
        var service = NetService.Instance;
        if (service == null || item == null) return;

        var center = GetItemPosition(item);

        if (service.IsServer)
            Server_TryHandlePickup(item, center);
        else
            Client_TryHandlePickup(item, center);
    }

    public void Server_HandleHostPickup(Item item)
    {
        Server_TryHandlePickup(item, null);
    }

    public void Server_RegisterHostDrop(Item item, DuckovItemAgent agent, Vector3 position,
        Vector3 direction, float angle, bool createRigidbody)
    {
        Server_RegisterDrop(item, agent, position, direction, angle, createRigidbody, 0);
    }

    public void Server_Update(float deltaTime)
    {
        if (!IsServer || !NetworkStarted)
            return;

        if (!HasConnectedClients())
        {
            _serverDropSnapshotTimer = 0f;
            return;
        }

        _serverDropSnapshotTimer += deltaTime;
        if (_serverDropSnapshotTimer >= DropSnapshotInterval)
        {
            _serverDropSnapshotTimer = 0f;
            SendDropSnapshot(null);
        }
    }

    public void Client_Update(float deltaTime)
    {
        if (IsServer || !NetworkStarted)
            return;

        var sceneId = SceneNet.Instance?._sceneReadySidSent;
        if (string.IsNullOrEmpty(sceneId))
        {
            _clientDropSnapshotSceneId = null;
            return;
        }

        if (!string.Equals(_clientDropSnapshotSceneId, sceneId, StringComparison.Ordinal))
        {
            _clientDropSnapshotSceneId = sceneId;
            Client_RequestDropSnapshot();
        }
    }

    private uint AllocateDropId()
    {
        var id = _nextDropId++;
        if (id == 0) id = _nextDropId++;
        while (_registry.Contains(id))
            id = _nextDropId++;
        return id;
    }

    private bool TryConsumePendingToken(uint token, out Item item)
    {
        item = null;
        if (token == 0 || !_pendingDropTokens.Remove(token))
            return false;

        _pendingTokenItems.TryGetValue(token, out item);
        _pendingTokenItems.Remove(token);
        return item != null;
    }

    private void RegisterDropEntry(uint dropId, Item item, GameObject agentGo, Vector3 position,
        Vector3 direction, float angle, bool createRigidbody)
    {
        var entry = _registry.Register(dropId, item);
        entry.Position = position;
        entry.Direction = direction;
        entry.Angle = angle;
        entry.CreateRigidbody = createRigidbody;
        entry.PendingRemoval = false;

        if (agentGo)
        {
            var tag = agentGo.GetComponent<NetDropTag>() ?? agentGo.AddComponent<NetDropTag>();
            tag.id = dropId;
            entry.Agent = tag.gameObject;
        }
        else if (item != null)
        {
            try
            {
                ItemTool.AddNetDropTag(item, dropId);
                var agent = item.ActiveAgent;
                if (agent && agent.gameObject)
                    entry.Agent = agent.gameObject;
            }
            catch
            {
            }
        }
    }

    private void Client_TryHandlePickup(Item item, Vector3? fallbackCenter)
    {
        if (item == null) return;

        if (!TryResolveDropId(item, fallbackCenter, out var dropId))
            return;

        if (!_registry.TryGetById(dropId, out var entry))
            entry = _registry.Register(dropId, item);

        if (entry.PendingRemoval)
            return;

        entry.PendingRemoval = true;

        DestroyAgent(entry.Item);
        if (entry.Agent)
            Object.Destroy(entry.Agent);

        var request = new ItemPickupRequestRpc
        {
            DropId = dropId
        };

        CoopTool.SendRpc(in request);
    }

    private void Server_TryHandlePickup(Item item, Vector3? fallbackCenter)
    {
        if (item == null) return;

        if (!TryResolveDropId(item, fallbackCenter, out var dropId))
            return;

        if (!_registry.TryGetById(dropId, out var entry))
            entry = _registry.Register(dropId, item);

        if (entry.PendingRemoval)
            return;

        entry.PendingRemoval = true;
        RemoveDrop(dropId, entry);

        var despawn = new ItemDespawnRpc
        {
            DropId = dropId
        };

        CoopTool.SendRpc(in despawn);
    }

    private void RemoveDrop(uint dropId, DropSyncEntry entry)
    {
        DestroyAgent(entry.Item);
        if (entry.Agent)
        {
            try
            {
                Object.Destroy(entry.Agent);
            }
            catch
            {
            }
        }

        _registry.Unregister(dropId);
    }

    public void Reset()
    {
        _serverDropSnapshotTimer = 0f;
        _dropSnapshotVersionCounter = 1;
        _clientSnapshotActive = false;
        _clientSnapshotVersion = 0;
        _clientDropSnapshotSceneId = null;
        _clientSnapshotPendingRemoval.Clear();
        _clientSnapshotRemovalBuffer.Clear();
        _dropSnapshotBuffer.Clear();
        _clientSpawnedFromServer.Clear();
        _serverSpawnedFromClient.Clear();
        _pendingDropTokens.Clear();
        _pendingTokenItems.Clear();
    }

    private bool HasConnectedClients()
    {
        var manager = NetManager;
        return manager != null && manager.ConnectedPeersCount > 0;
    }

    private bool TryResolveDropId(Item item, Vector3? fallbackCenter, out uint dropId)
    {
        dropId = 0;

        if (item != null && _registry.TryGetByItem(item, out var entry) && entry != null)
        {
            dropId = entry.DropId;
            return dropId != 0;
        }

        var agent = item?.ActiveAgent;
        if (agent && agent.TryGetComponent<NetDropTag>(out var tag) && tag.id != 0)
        {
            dropId = tag.id;
            return true;
        }

        var center = fallbackCenter ?? GetItemPosition(item);
        if (TryFindNearestDropId(center, out dropId))
            return true;

        return false;
    }

    private bool TryFindNearestDropId(Vector3 center, out uint dropId)
    {
        dropId = 0;
        var count = Physics.OverlapSphereNonAlloc(center, PICK_RADIUS, _overlapBuffer, PICK_LAYER_MASK, PICK_QUERY);
        var best = float.MaxValue;
        for (var i = 0; i < count; i++)
        {
            var col = _overlapBuffer[i];
            if (!col) continue;

            var tag = col.GetComponentInParent<NetDropTag>() ?? col.GetComponent<NetDropTag>();
            if (tag == null || tag.id == 0) continue;

            var d2 = (tag.transform.position - center).sqrMagnitude;
            if (d2 >= best) continue;

            best = d2;
            dropId = tag.id;
        }

        return dropId != 0;
    }

    private static Vector3 GetItemPosition(Item item)
    {
        if (item == null) return Vector3.zero;
        try
        {
            var agent = item.ActiveAgent;
            if (agent && agent.transform)
                return agent.transform.position;
        }
        catch
        {
        }

        return item.transform?.position ?? Vector3.zero;
    }

    private static void DestroyAgent(Item item)
    {
        try
        {
            var agent = item?.ActiveAgent;
            if (agent && agent.gameObject)
                Object.Destroy(agent.gameObject);
        }
        catch
        {
        }
    }
}
