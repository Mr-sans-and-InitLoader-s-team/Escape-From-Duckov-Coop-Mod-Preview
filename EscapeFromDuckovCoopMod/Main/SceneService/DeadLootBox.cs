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
using Duckov.Scenes;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EscapeFromDuckovCoopMod;

public partial class DeadLootBox : MonoBehaviour
{
    public static DeadLootBox Instance;

    private readonly Dictionary<string, GameObject> _prefabCache = new();
    private readonly Dictionary<InteractableLootbox, DeadLootSpawnRpc> _serverSpawns = new();

    private NetService Service => NetService.Instance;
    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;


    public void Init()
    {
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Update() => CorpseRecovery.Tick();

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _prefabCache.Clear();
    }

    public void SpawnDeadLootboxAt(int lootUid, Vector3 pos, Quaternion rot, bool useTombPrefab, string prefabName, string playerId = null)
    {
        if (CoopSyncDatabase.Loot.TryGetByLootUid(lootUid, out var existing) && existing.Inventory)
        {
            if (useTombPrefab && existing.Lootbox && existing.Lootbox.needInspect && Service.IsSelfId(playerId))
                CorpseInspection.Initialize(existing.Lootbox, true, playerId);
            return;
        }
        try
        {
            var prefab = ResolveDeadLootPrefab(prefabName, useTombPrefab);
            if (!prefab)
            {
                Debug.LogWarning("[LOOT] DeadLoot prefab not found on client, spawn aborted.");
                return;
            }

            var go = Instantiate(prefab, pos, rot);
            MultiSceneCore.MoveToActiveWithScene(go, SceneManager.GetActiveScene().buildIndex);
            var box = go ? go.GetComponent<InteractableLootbox>() : null;
            if (!box) return;

            AccessTools.Method(typeof(InteractableLootbox), "CreateLocalInventory").Invoke(box, null);

            var inv = box.Inventory;
            if (!inv)
            {
                Debug.LogWarning("[Client DeadLootBox Spawn] Inventory is null!");
                return;
            }

            _clientSpawns[lootUid] = box;

            WorldLootPrime.PrimeIfClient(box);
            CorpseInspection.Initialize(box, useTombPrefab, playerId);

            CoopSyncDatabase.Loot.Register(box, inv, lootUid);

            // 用主机广播的 pos 注册 posKey → inv（旧兜底仍保留）
            var dict = InteractableLootbox.Inventories;
            if (dict != null)
            {
                var correctKey = LootManager.ComputeLootKeyFromPos(pos);
                var wrongKey = -1;
                foreach (var kv in dict)
                    if (kv.Value == inv && kv.Key != correctKey)
                    {
                        wrongKey = kv.Key;
                        break;
                    }

                if (wrongKey != -1) dict.Remove(wrongKey);
                dict[correctKey] = inv;
            }

            //稳定 ID → inv
            if (lootUid >= 0)
            {
                LootManager.Instance._cliLootByUid[lootUid] = inv;
                CoopSyncDatabase.Loot.SetLootUid(inv, lootUid);
            }

        }
        catch (Exception e)
        {
            Debug.LogError("[LOOT] SpawnDeadLootboxAt failed: " + e);
        }
    }


    private GameObject ResolveDeadLootPrefab(string prefabName, bool useTombPrefab)
    {
        if (useTombPrefab)
            return GetDeadLootTombPrefabOnClient();

        var normalized = NormalizePrefabName(prefabName);
        if (!string.IsNullOrEmpty(normalized) && _prefabCache.TryGetValue(normalized, out var cached) && cached)
            return cached;

        GameObject resolved = null;
        if (!string.IsNullOrEmpty(normalized))
        {
            resolved = FindDeadLootPrefabInScene(normalized) ?? FindDeadLootPrefabInResources(normalized);
            if (resolved)
                _prefabCache[normalized] = resolved;
        }

        return resolved ? resolved : GetDeadLootPrefabOnClient();
    }

    private static string NormalizePrefabName(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName))
            return string.Empty;

        return prefabName.Replace("(Clone)", string.Empty, StringComparison.Ordinal).Trim();
    }

    private GameObject FindDeadLootPrefabInScene(string normalizedName)
    {
        try
        {
            var boxes = FindObjectsOfType<InteractableLootbox>(true);
            foreach (var box in boxes)
            {
                var go = box ? box.gameObject : null;
                if (go && string.Equals(NormalizePrefabName(go.name), normalizedName, StringComparison.Ordinal))
                    return go;
            }
        }
        catch
        {
        }

        return null;
    }

    private GameObject FindDeadLootPrefabInResources(string normalizedName)
    {
        try
        {
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in all)
            {
                if (go && string.Equals(NormalizePrefabName(go.name), normalizedName, StringComparison.Ordinal))
                    return go;
            }
        }
        catch
        {
        }

        try
        {
            var loaded = Resources.Load<GameObject>(normalizedName);
            if (loaded)
                return loaded;
        }
        catch
        {
        }

        return null;
    }

    private GameObject GetDeadLootPrefabOnClient()
    {
        // 沿用现有逻辑（Main 或任意 CMC）
        try
        {
            var main = CharacterMainControl.Main;
            if (main)
            {
                var obj = main.deadLootBoxPrefab.gameObject;
                if (obj) return obj;
            }
        }
        catch
        {
        }

        try
        {
            return GameplayDataSettings.Prefabs.LootBoxPrefab.gameObject;
        }
        catch
        {
        }

        return null;
    }

    private GameObject GetDeadLootTombPrefabOnClient()
    {
        try
        {
            var any = GameplayDataSettings.Prefabs;
            if (any != null && any.LootBoxPrefab_Tomb != null)
                return any.LootBoxPrefab_Tomb.gameObject;
        }
        catch
        {
        }

        return GetDeadLootPrefabOnClient();
    }

    private string TryGetPrefabNameFromVictim(CharacterMainControl whoDied)
    {
        try
        {
            var prefab = whoDied ? whoDied.deadLootBoxPrefab : null;
            var go = prefab ? prefab.gameObject : null;
            return go ? go.name : string.Empty;
        }
        catch
        {
        }

        return string.Empty;
    }

    public void Server_OnDeadLootboxSpawned(InteractableLootbox box, CharacterMainControl whoDied, bool useTombPrefab = false, string playerId = null, string ownerId = null)
    {
        if (!IsServer || box == null) return;
        try
        {
            var prefabName = NormalizePrefabName(TryGetPrefabNameFromVictim(whoDied));
            var corpseOwnerId = useTombPrefab ? ownerId ?? playerId ?? Service.GetPlayerId(null) : string.Empty;

            // 生成稳定 ID 并登记
            var lootUid = LootManager.Instance._nextLootUid++;
            var inv = box.Inventory;
            if (inv) LootManager.Instance._srvLootByUid[lootUid] = inv;
            CoopSyncDatabase.Loot.Register(box, inv, lootUid);
            if (inv) CoopSyncDatabase.Loot.SetLootUid(inv, lootUid);

            CorpseInspection.Initialize(box, useTombPrefab, corpseOwnerId);


            var rpc = new DeadLootSpawnRpc
            {
                SceneIndex = SceneManager.GetActiveScene().buildIndex,
                LootUid = lootUid,
                Position = box.transform.position,
                Rotation = box.transform.rotation,
                UseTombPrefab = useTombPrefab,
                PlayerId = playerId ?? string.Empty,
                PrefabName = prefabName,
                OwnerId = corpseOwnerId
            };

            foreach (var stale in _serverSpawns.Keys.Where(b => !b).ToArray()) _serverSpawns.Remove(stale);
            _serverSpawns[box] = rpc;
            CoopTool.SendRpc(in rpc);

        }
        catch (Exception e)
        {
            Debug.LogError("[LOOT] Server_OnDeadLootboxSpawned failed: " + e);
        }
    }

    internal void Server_ReplayToPeer(NetPeer peer)
    {
        foreach (var pair in _serverSpawns.ToArray())
        {
            if (!pair.Key) { _serverSpawns.Remove(pair.Key); continue; }
            if (!pair.Key.gameObject.activeSelf) continue;
            var message = pair.Value;
            message.Position = pair.Key.transform.position;
            message.Rotation = pair.Key.transform.rotation;
            message.PlayerId = string.Empty;
            CoopTool.SendRpcTo(peer, in message);
        }
    }
}
