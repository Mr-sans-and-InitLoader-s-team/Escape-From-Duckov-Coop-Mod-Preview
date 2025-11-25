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

using System.Collections.Generic;
using LiteNetLib;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public class ExplosiveOilBarrel
{
    private readonly Dictionary<uint, GameObject> _byId = new();
    private bool _indexedOnce;
    private bool _broadcastedOnce;

    private NetService Service => NetService.Instance;

    private bool IsServer => Service != null && Service.IsServer;

    private NetManager NetManager => Service?.netManager;

    public void BuildIndex()
    {
        _byId.Clear();
        _indexedOnce = false;
        _broadcastedOnce = false;
        var registry = CoopSyncDatabase.Environment.ExplosiveOilBarrels;
        registry.Clear();

        var all = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
        foreach (var go in all)
        {
            if (go == null) continue;
            if (go.name == null) continue;
            if (!go.name.Contains("Explosive_OilBarrel")) continue;

            var entry = registry.Register(go);
            if (entry == null || entry.Id == 0) continue;

            _byId[entry.Id] = go;
        }

        _indexedOnce = true;

        if (IsServer && !_broadcastedOnce && NetManager != null && NetManager.ConnectedPeersCount > 0)
        {
            _broadcastedOnce = true;
            Server_BroadcastSnapshot();
        }
    }

    public void Server_BroadcastSnapshot(NetPeer target = null)
    {
        if (!IsServer) return;

        var registry = CoopSyncDatabase.Environment.ExplosiveOilBarrels;
        var entries = registry.Entries;
        var count = entries.Count;

        var ids = new uint[count];
        var actives = new bool[count];

        for (var i = 0; i < count; i++)
        {
            var entry = entries[i];
            ids[i] = entry.Id;
            actives[i] = entry.Barrel && entry.Barrel.activeSelf;
        }

        var rpc = new EnvExplosiveOilBarrelStateRpc
        {
            Reset = true,
            Ids = ids,
            ActiveStates = actives
        };

        if (target != null)
            CoopTool.SendRpcTo(target, in rpc);
        else
            CoopTool.SendRpc(in rpc);
    }

    public void Client_ApplySnapshot(EnvExplosiveOilBarrelStateRpc message)
    {
        if (IsServer) return;

        if (!_indexedOnce)
            BuildIndex();

        var len = message.Ids?.Length ?? 0;
        var states = message.ActiveStates;

        for (var i = 0; i < len; i++)
        {
            var id = message.Ids[i];
            var active = states != null && i < states.Length && states[i];

            if (!_byId.TryGetValue(id, out var go) || !go)
            {
                if (CoopSyncDatabase.Environment.ExplosiveOilBarrels.TryGet(id, out var entry) && entry != null)
                    go = entry.Barrel;
            }

            if (!go) continue;

            if (go.activeSelf != active)
                go.SetActive(active);
        }
    }

    public void Reset()
    {
        _byId.Clear();
        _indexedOnce = false;
        _broadcastedOnce = false;
    }
}
