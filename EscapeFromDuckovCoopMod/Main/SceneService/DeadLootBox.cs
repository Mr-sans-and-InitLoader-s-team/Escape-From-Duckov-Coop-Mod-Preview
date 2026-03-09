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
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EscapeFromDuckovCoopMod;

public class DeadLootBox : MonoBehaviour
{
    public static DeadLootBox Instance;

    private readonly Dictionary<string, GameObject> _prefabCache = new();

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

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _prefabCache.Clear();
    }

    public void SpawnDeadLootboxAt(int lootUid, Vector3 pos, Quaternion rot, bool useTombPrefab, string prefabName)
    {
        try
        {
            var prefab = ResolveDeadLootPrefab(prefabName, useTombPrefab);
            if (!prefab)
            {
                Debug.LogWarning("[LOOT] DeadLoot prefab not found on client, spawn aborted.");
                return;
            }

            var go = Instantiate(prefab, pos, rot);
            var box = go ? go.GetComponent<InteractableLootbox>() : null;
            if (!box) return;

            var inv = box.Inventory;
            if (!inv)
            {
                Debug.LogWarning("[Client DeadLootBox Spawn] Inventory is null!");
                return;
            }

            WorldLootPrime.PrimeIfClient(box);

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

    public void Server_OnDeadLootboxSpawned(InteractableLootbox box, CharacterMainControl whoDied, bool useTombPrefab = false, string playerId = null)
    {
        if (!IsServer || box == null) return;
        try
        {
            var prefabName = NormalizePrefabName(TryGetPrefabNameFromVictim(whoDied));

            // 生成稳定 ID 并登记
            var lootUid = LootManager.Instance._nextLootUid++;
            var inv = box.Inventory;
            if (inv) LootManager.Instance._srvLootByUid[lootUid] = inv;
            CoopSyncDatabase.Loot.Register(box, inv, lootUid);
            if (inv) CoopSyncDatabase.Loot.SetLootUid(inv, lootUid);

            // >>> 放在 writer.Reset() 之前 <<<
            if (inv != null)
            {
                inv.NeedInspection = true;
                // 尝试把“这个箱子以前被搜过”的标记也清空（有的版本有这个字段）
                try
                {
                    Traverse.Create(inv).Field<bool>("hasBeenInspectedInLootBox").Value = false;
                }
                catch
                {
                }

                // 把当前内容全部标记为“未鉴定”
                for (var i = 0; i < inv.Content.Count; ++i)
                {
                    var it = inv.GetItemAt(i);
                    if (it) it.Inspected = false;
                }
            }


            var rpc = new DeadLootSpawnRpc
            {
                SceneIndex = SceneManager.GetActiveScene().buildIndex,
                LootUid = lootUid,
                Position = box.transform.position,
                Rotation = box.transform.rotation,
                UseTombPrefab = useTombPrefab,
                PlayerId = playerId ?? string.Empty,
                PrefabName = prefabName
            };

            CoopTool.SendRpc(in rpc);

        }
        catch (Exception e)
        {
            Debug.LogError("[LOOT] Server_OnDeadLootboxSpawned failed: " + e);
        }
    }
}