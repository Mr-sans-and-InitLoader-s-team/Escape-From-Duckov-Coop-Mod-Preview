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
using EscapeFromDuckovCoopMod.Net;  // 引入智能发送扩展方法

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
        // 【优化】改为协程分帧处理，避免瞬间卡顿
        StartCoroutine(SpawnDeadLootboxAtAsync(aiId, lootUid, pos, rot));
    }

    // 【优化】异步协程版本，分帧处理避免卡顿
    private IEnumerator SpawnDeadLootboxAtAsync(int aiId, int lootUid, Vector3 pos, Quaternion rot)
    {
        // 【修复】移除外层 try-catch，因为协程中不能在 try-catch 块内使用 yield
        // 第一帧：移除尸体
        // 【优化】物理查询半径从 3.0f 降至 2.5f
        AITool.TryClientRemoveNearestAICorpse(pos, 2.5f);
        yield return null;

        // 第二帧：获取预制体并实例化
        var prefab = GetDeadLootPrefabOnClient(aiId);
        if (!prefab) yield break;

        var go = Instantiate(prefab, pos, rot);
        var box = go ? go.GetComponent<InteractableLootbox>() : null;
        if (!box) yield break;

        var inv = box.Inventory;
        if (!inv) yield break;

        WorldLootPrime.PrimeIfClient(box);
        yield return null;

        // 第三帧：字典操作和ID注册
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

        if (lootUid >= 0) LootManager.Instance._cliLootByUid[lootUid] = inv;
        yield return null;

        // 第四帧：处理缓存的物品数据
        if (lootUid >= 0 && LootManager.Instance._pendingLootStatesByUid.TryGetValue(lootUid, out var pack))
        {
            LootManager.Instance._pendingLootStatesByUid.Remove(lootUid);

            COOPManager.LootNet._applyingLootState = true;
            try
            {
                var cap = Mathf.Clamp(pack.capacity, 1, 128);
                inv.Loading = true;
                inv.SetCapacity(cap);

                for (var i = inv.Content.Count - 1; i >= 0; --i)
                {
                    Item removed;
                    inv.RemoveAt(i, out removed);
                    if (removed != null)
                    {
                        try { Destroy(removed.gameObject); }
                        catch { /* 忽略销毁错误 */ }
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
                inv.Loading = false;
                COOPManager.LootNet._applyingLootState = false;
            }

            WorldLootPrime.PrimeIfClient(box);
            yield break;
        }
        yield return null;

        // 第五帧：网络请求
        COOPManager.LootNet.Client_RequestLootState(inv);
        StartCoroutine(LootManager.Instance.ClearLootLoadingTimeout(inv, 1.5f));
    }


    private GameObject GetDeadLootPrefabOnClient(int aiId)
    {
        // 1) 首选：死亡 CMC 上的 private deadLootBoxPrefab
        try
        {
            if (aiId > 0 && AITool.aiById.TryGetValue(aiId, out var cmc) && cmc)
            {
                // 【优化】移除 Debug.LogWarning，减少日志开销
                // Debug.LogWarning($"[SpawnDeadloot] AiID:{cmc.GetComponent<NetAiTag>().aiId}");
                // if (cmc.deadLootBoxPrefab.gameObject == null) Debug.LogWarning("[SPawnDead] deadLootBoxPrefab.gameObject null!");

                if (cmc != null)
                {
                    var obj = cmc.deadLootBoxPrefab.gameObject;
                    if (obj) return obj;
                }
                // 【优化】移除 Debug.LogWarning
                // else { Debug.LogWarning("[SPawnDead] cmc is null!"); }
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
            netManager.SendSmart(writer, Op.DEAD_LOOT_SPAWN);

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
            netManager.SendSmart(writer, Op.DEAD_LOOT_SPAWN);

            // 2) 可选：是否立刻广播整箱内容（默认不广播，等客户端真正打开时再按需请求）
            if (EAGER_BROADCAST_LOOT_STATE_ON_SPAWN) COOPManager.LootNet.Server_SendLootboxState(null, box.Inventory); // 如需老行为，打开上面的开关即可
        }
        catch (Exception e)
        {
            Debug.LogError("[LOOT] Server_OnDeadLootboxSpawned failed: " + e);
        }
    }
}