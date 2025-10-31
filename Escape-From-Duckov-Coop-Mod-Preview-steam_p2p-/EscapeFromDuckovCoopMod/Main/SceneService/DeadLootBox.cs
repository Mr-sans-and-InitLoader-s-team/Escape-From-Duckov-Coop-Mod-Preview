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

using System.Collections;
using ItemStatsSystem;
using UnityEngine.SceneManagement;

namespace EscapeFromDuckovCoopMod;

public class DeadLootBox : MonoBehaviour
{
    public const bool EAGER_BROADCAST_LOOT_STATE_ON_SPAWN = false;
    public static DeadLootBox Instance;

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
    }

    public void SpawnDeadLootboxAt(int aiId, int lootUid, Vector3 pos, Quaternion rot)
    {
        try
        {
            AITool.TryClientRemoveNearestAICorpse(pos, 3.0f);

            var prefab = GetDeadLootPrefabOnClient(aiId);
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
            if (lootUid >= 0) LootManager.Instance._cliLootByUid[lootUid] = inv;

            // 若快照先到，这里优先吃缓存
            if (lootUid >= 0 && LootManager.Instance._pendingLootStatesByUid.TryGetValue(lootUid, out var pack))
            {
                LootManager.Instance._pendingLootStatesByUid.Remove(lootUid);

                COOPManager.LootNet._applyingLootState = true;
                try
                {
                    var cap = Mathf.Clamp(pack.capacity, 1, 128);
                    inv.Loading = true; // ★ 进入批量
                    inv.SetCapacity(cap);

                    for (var i = inv.Content.Count - 1; i >= 0; --i)
                    {
                        Item removed;
                        inv.RemoveAt(i, out removed);
                        try
                        {
                            if (removed) Destroy(removed.gameObject);
                        }
                        catch
                        {
                        }
                    }

                    foreach (var (p, snap) in pack.Item2)
                    {
                        var item = ItemTool.BuildItemFromSnapshot(snap);
                        if (item) inv.AddAt(item, p);
                    }
                }
                finally
                {
                    inv.Loading = false; // ★ 结束批量
                    COOPManager.LootNet._applyingLootState = false;
                }

                WorldLootPrime.PrimeIfClient(box);
                return; // 吃完缓存就不再发请求
            }

            // 正常路径：请求一次状态 + 超时兜底
            COOPManager.LootNet.Client_RequestLootState(inv);
            StartCoroutine(LootManager.Instance.ClearLootLoadingTimeout(inv, 1.5f));
        }
        catch (Exception e)
        {
            Debug.LogError("[LOOT] SpawnDeadLootboxAt failed: " + e);
        }
    }


    private GameObject GetDeadLootPrefabOnClient(int aiId)
    {
        // 1) 首选：死亡 CMC 上的 private deadLootBoxPrefab
        try
        {
            if (aiId > 0 && AITool.aiById.TryGetValue(aiId, out var cmc) && cmc)
            {
                Debug.LogWarning($"[SpawnDeadloot] AiID:{cmc.GetComponent<NetAiTag>().aiId}");
                if (cmc.deadLootBoxPrefab.gameObject == null) Debug.LogWarning("[SPawnDead] deadLootBoxPrefab.gameObject null!");


                if (cmc != null)
                {
                    var obj = cmc.deadLootBoxPrefab.gameObject;
                    if (obj) return obj;
                }
                else
                {
                    Debug.LogWarning("[SPawnDead] cmc is null!");
                }
            }
        }
        catch
        {
        }

        // 2) 兜底：沿用你现有逻辑（Main 或任意 CMC）
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
            var any = FindObjectOfType<CharacterMainControl>();
            if (any)
            {
                var obj = any.deadLootBoxPrefab.gameObject;
                if (obj) return obj;
            }
        }
        catch
        {
        }

        return null;
    }

    public void Server_OnDeadLootboxSpawned(InteractableLootbox box, CharacterMainControl whoDied)
    {
        if (!IsServer || box == null) return;
        try
        {
            // 生成稳定 ID 并登记
            var lootUid = LootManager.Instance._nextLootUid++;
            var inv = box.Inventory;
            if (inv) LootManager.Instance._srvLootByUid[lootUid] = inv;

            var aiId = 0;
            if (whoDied)
            {
                var tag = whoDied.GetComponent<NetAiTag>();
                if (tag != null) aiId = tag.aiId;
                if (aiId == 0)
                    foreach (var kv in AITool.aiById)
                        if (kv.Value == whoDied)
                        {
                            aiId = kv.Key;
                            break;
                        }
            }

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


            // 稳定 ID
            writer.Reset();
            writer.Put((byte)Op.DEAD_LOOT_SPAWN);
            writer.Put(SceneManager.GetActiveScene().buildIndex);
            writer.Put(aiId);
            writer.Put(lootUid); // 稳定 ID
            writer.PutV3cm(box.transform.position);
            writer.PutQuaternion(box.transform.rotation);
            netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);

            if (EAGER_BROADCAST_LOOT_STATE_ON_SPAWN)
                StartCoroutine(RebroadcastDeadLootStateAfterFill(box));
        }
        catch (Exception e)
        {
            Debug.LogError("[LOOT] Server_OnDeadLootboxSpawned failed: " + e);
        }
    }

    public IEnumerator RebroadcastDeadLootStateAfterFill(InteractableLootbox box)
    {
        if (!EAGER_BROADCAST_LOOT_STATE_ON_SPAWN) yield break;

        yield return null; // 给原版填充时间
        yield return null;
        if (box && box.Inventory) COOPManager.LootNet.Server_SendLootboxState(null, box.Inventory);
    }


    public void Server_OnDeadLootboxSpawned(InteractableLootbox box)
    {
        if (!IsServer || box == null) return;
        try
        {
            var lootUid = LootManager.Instance._nextLootUid++;
            var inv = box.Inventory;
            if (inv) LootManager.Instance._srvLootByUid[lootUid] = inv;

            // ★ 新增：抑制“填充期间”的 AddItem 广播
            if (inv) LootManager.Instance.Server_MuteLoot(inv, 2.0f);

            writer.Reset();
            writer.Put((byte)Op.DEAD_LOOT_SPAWN);
            writer.Put(SceneManager.GetActiveScene().buildIndex);
            writer.PutV3cm(box.transform.position);
            writer.PutQuaternion(box.transform.rotation);
            netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);

            // 2) 可选：是否立刻广播整箱内容（默认不广播，等客户端真正打开时再按需请求）
            if (EAGER_BROADCAST_LOOT_STATE_ON_SPAWN) COOPManager.LootNet.Server_SendLootboxState(null, box.Inventory); // 如需老行为，打开上面的开关即可
        }
        catch (Exception e)
        {
            Debug.LogError("[LOOT] Server_OnDeadLootboxSpawned failed: " + e);
        }
    }
}