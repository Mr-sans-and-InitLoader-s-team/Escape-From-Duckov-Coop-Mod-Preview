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
using System.Reflection;
using Duckov.Scenes;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(CharacterSpawnerRoot), "StartSpawn")]
internal static class Patch_Root_StartSpawn
{
    private static readonly HashSet<int> _waiting = new();
    private static readonly Stack<Random.State> _rngStack = new();

    private static readonly MethodInfo _miStartSpawn =
        AccessTools.Method(typeof(CharacterSpawnerRoot), "StartSpawn");

    private static bool Prefix(CharacterSpawnerRoot __instance)
    {
        try
        {
            var mod = ModBehaviourF.Instance;
            var rootId = AITool.StableRootId(__instance);

            // 核心科技:) 种子未到 → 阻止原版生成，并排队等待；到种子后再反射调用 StartSpawn()
            if (!mod.IsServer && !COOPManager.AIHandle.aiRootSeeds.ContainsKey(rootId))
            {
                if (_waiting.Add(rootId))
                    __instance.StartCoroutine(WaitSeedAndSpawn(__instance, rootId));
                return false;
            }

            // 进入“随机数种子作用域”
            var useSeed = mod.IsServer ? AITool.DeriveSeed(COOPManager.AIHandle.sceneSeed, rootId) : COOPManager.AIHandle.aiRootSeeds[rootId];
            _rngStack.Push(Random.state);
            Random.InitState(useSeed);
            return true;
        }
        catch
        {
            return true;
        }
    }

    private static void ForceActivateHierarchy(Transform t)
    {
        while (t)
        {
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            t = t.parent;
        }
    }

    private static IEnumerator WaitSeedAndSpawn(CharacterSpawnerRoot inst, int rootId)
    {
        var mod = ModBehaviourF.Instance;
        while (mod && !COOPManager.AIHandle.aiRootSeeds.ContainsKey(rootId)) yield return null;

        _waiting.Remove(rootId);

        if (inst)
        {
            // 先把刷怪根及父链强制激活，防止在非激活层级里生成失败
            ForceActivateHierarchy(inst.transform);

            if (_miStartSpawn != null)
                _miStartSpawn.Invoke(inst, null); // 反射调用 private StartSpawn()
        }
    }

    private static void Postfix(CharacterSpawnerRoot __instance)
    {
        try
        {
            if (_rngStack.Count > 0) Random.state = _rngStack.Pop();

            // 你原有的“给 AI 打标签 / 注册 / 主机广播负载”逻辑保留
            var list = Traverse.Create(__instance)
                .Field<List<CharacterMainControl>>("createdCharacters")
                .Value;

            if (list != null && COOPManager.AIHandle.freezeAI)
                foreach (var c in list)
                    AITool.TryFreezeAI(c);

            if (list != null)
            {
                var mod = ModBehaviourF.Instance;
                var rootId = AITool.StableRootId(__instance);

                // 按“名称 + 量化坐标 + InstanceID”稳定排序，避免回调时序导致乱序
                var ordered = new List<CharacterMainControl>(list);
                ordered.RemoveAll(c => !c);
                ordered.Sort((a, b) =>
                {
                    var n = string.Compare(a.name, b.name, StringComparison.Ordinal);
                    if (n != 0) return n;
                    var pa = a.transform.position;
                    var pb = b.transform.position;
                    int ax = Mathf.RoundToInt(pa.x * 100f), az = Mathf.RoundToInt(pa.z * 100f), ay = Mathf.RoundToInt(pa.y * 100f);
                    int bx = Mathf.RoundToInt(pb.x * 100f), bz = Mathf.RoundToInt(pb.z * 100f), by = Mathf.RoundToInt(pb.y * 100f);
                    if (ax != bx) return ax.CompareTo(bx);
                    if (az != bz) return az.CompareTo(bz);
                    if (ay != by) return ay.CompareTo(by);
                    return a.GetInstanceID().CompareTo(b.GetInstanceID());
                });

                for (var i = 0; i < ordered.Count; i++)
                {
                    var cmc = ordered[i];
                    if (!cmc || !AITool.IsRealAI(cmc)) continue;

                    var aiId = AITool.DeriveSeed(rootId, i + 1);
                    var tag = cmc.GetComponent<NetAiTag>() ?? cmc.gameObject.AddComponent<NetAiTag>();

                    // 主机赋 id + 登记 + 广播；客户端保持 tag.aiId=0 等待绑定（见修复 A）
                    if (mod.IsServer)
                    {
                        tag.aiId = aiId;
                        COOPManager.AIHandle.RegisterAi(aiId, cmc);
                        COOPManager.AIHandle.Server_BroadcastAiLoadout(aiId, cmc);
                    }
                }


                // 主机在本 root 刷完后即刻发一帧位置快照，收敛初始误差
                if (mod.IsServer) COOPManager.AIHandle.Server_BroadcastAiTransforms();
            }
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(CharacterSpawnerRoot), "Init")]
internal static class Patch_Root_Init_FixContain
{
    private static bool Prefix(CharacterSpawnerRoot __instance)
    {
        try
        {
            var msc = MultiSceneCore.Instance;

            // 仅在 SpawnerGuid != 0 时才做“重复过滤”
            if (msc != null && __instance.SpawnerGuid != 0 &&
                msc.usedCreatorIds.Contains(__instance.SpawnerGuid))
                return true; // 放行原版 → 它会销毁重复体

            var tr = Traverse.Create(__instance);
            tr.Field("inited").SetValue(true);

            var spComp = tr.Field<CharacterSpawnerComponentBase>("spawnerComponent").Value;
            if (spComp != null) spComp.Init(__instance);

            var buildIndex = SceneManager.GetActiveScene().buildIndex;
            tr.Field("relatedScene").SetValue(buildIndex);

            __instance.transform.SetParent(null);
            if (msc != null)
            {
                MultiSceneCore.MoveToMainScene(__instance.gameObject);
                // 仅在 Guid 非 0 时登记，避免把“0”当成全场唯一
                if (__instance.SpawnerGuid != 0)
                    msc.usedCreatorIds.Add(__instance.SpawnerGuid);
            }

            var mod = ModBehaviourF.Instance;
            if (mod != null && mod.IsServer) AIRequest.Instance.Server_SendRootSeedDelta(__instance);


            return false; // 跳过原始 Init（避免误删）
        }
        catch (Exception e)
        {
            Debug.LogWarning("[AI-SEED] Patch_Root_Init_FixContain failed: " + e);
            return true;
        }
    }
}

[HarmonyPatch(typeof(CharacterSpawnerRoot), "Update")]
internal static class Patch_Root_Update_ClientAutoSpawn
{
    private static readonly MethodInfo _miStartSpawn =
        AccessTools.Method(typeof(CharacterSpawnerRoot), "StartSpawn");

    private static readonly MethodInfo _miCheckTiming =
        AccessTools.Method(typeof(CharacterSpawnerRoot), "CheckTiming");

    private static void Postfix(CharacterSpawnerRoot __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || mod.IsServer) return;

        var tr = Traverse.Create(__instance);
        var inited = tr.Field<bool>("inited").Value;
        var created = tr.Field<bool>("created").Value;
        if (!inited || created) return;

        var rootId = AITool.StableRootId(__instance);

        // 没种子 → 兼容一次 AltId 映射（你已有）
        if (!COOPManager.AIHandle.aiRootSeeds.ContainsKey(rootId))
        {
            var altId = AITool.StableRootId_Alt(__instance);
            if (COOPManager.AIHandle.aiRootSeeds.TryGetValue(altId, out var seed))
                COOPManager.AIHandle.aiRootSeeds[rootId] = seed;
            else
                return; // 种子确实没到，别刷
        }

        // 关键：尊重原版判断（时间/天气/触发器）
        var ok = false;
        try
        {
            ok = (bool)_miCheckTiming.Invoke(__instance, null);
        }
        catch
        {
        }

        if (!ok) return;

        // 与原逻辑一致：确保层级激活再刷
        ForceActivateHierarchy(__instance.transform);
        try
        {
            _miStartSpawn?.Invoke(__instance, null);
        }
        catch
        {
        }
    }

    private static void ForceActivateHierarchy(Transform t)
    {
        while (t)
        {
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            t = t.parent;
        }
    }
}

[HarmonyPatch(typeof(CharacterSpawnerGroup), "Awake")]
internal static class Patch_Group_Awake
{
    private static void Postfix(CharacterSpawnerGroup __instance)
    {
        try
        {
            var mod = ModBehaviourF.Instance;

            // 用“场景种子 + 该 Group 的 Transform 路径哈希”派生随机
            var gid = AITool.StableHash(AITool.TransformPath(__instance.transform));
            var seed = AITool.DeriveSeed(COOPManager.AIHandle.sceneSeed, gid);

            var rng = new System.Random(seed);
            if (__instance.hasLeader)
            {
                // 与原版相同的比较方式：保留队长的概率 = hasLeaderChance
                var keep = rng.NextDouble() <= __instance.hasLeaderChance;
                __instance.hasLeader = keep;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AI-SEED] Group.Awake Postfix 出错: {e.Message}");
        }
    }
}