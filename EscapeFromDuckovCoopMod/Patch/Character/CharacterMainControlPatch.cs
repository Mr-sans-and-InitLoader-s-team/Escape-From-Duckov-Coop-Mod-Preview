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

using EscapeFromDuckovCoopMod.Utils;
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(CharacterMainControl), "OnChangeItemAgentChangedFunc")]
internal static class Patch_CMC_OnChangeHold_AIRebroadcast
{
    private static void Postfix(CharacterMainControl __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return; // 只在主机上发
        if (!__instance || __instance == CharacterMainControl.Main) return; // 排除本地玩家

        var tag = __instance.GetComponent<NetAiTag>();
        if (tag == null || tag.aiId == 0) return;

        // AI 切换/拿起/放下手持后，立即广播一份“装备+武器”快照
        COOPManager.AIHandle.Server_BroadcastAiLoadout(tag.aiId, __instance);
    }
}

[HarmonyPatch(typeof(CharacterMainControl), nameof(CharacterMainControl.SetCharacterModel))]
internal static class Patch_CMC_SetCharacterModel_FaceReapply
{
    private static void Postfix(CharacterMainControl __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || mod.IsServer) return;
        AIName.ReapplyFaceIfKnown(__instance);
    }
}

[HarmonyPatch(typeof(CharacterMainControl), nameof(CharacterMainControl.SetCharacterModel))]
internal static class Patch_CMC_SetCharacterModel_FaceReapply_Client
{
    private static void Postfix(CharacterMainControl __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || mod.IsServer) return; // 只在客户端
        AIName.ReapplyFaceIfKnown(__instance);
    }
}

// 主机：一旦换了模型，立刻二次广播（模型名/图标/脸 最新状态）
[HarmonyPatch(typeof(CharacterMainControl), nameof(CharacterMainControl.SetCharacterModel))]
internal static class Patch_CMC_SetCharacterModel_Rebroadcast_Server
{
    private static void Postfix(CharacterMainControl __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.IsServer) return;

        var aiId = -1;
        foreach (var kv in AITool.aiById)
            if (kv.Value == __instance)
            {
                aiId = kv.Key;
                break;
            }

        if (aiId < 0) return;

        if (ModBehaviourF.LogAiLoadoutDebug)
            Debug.Log($"[AI-REBROADCAST] aiId={aiId} after SetCharacterModel");
        COOPManager.AIHandle.Server_BroadcastAiLoadout(aiId, __instance);
    }
}

// 进入/离开 OnDead 时打/清标记（只关心 AI，不含玩家）
[HarmonyPatch(typeof(CharacterMainControl), "OnDead")]
internal static class Patch_CMC_OnDead_Mark
{
    private static void Prefix(CharacterMainControl __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return;
        if (!__instance) return;

        // 只给 AI 打标记（排除本机玩家）
        if (__instance == CharacterMainControl.Main) return;
        // 【优化】使用 ComponentCache 避免重复 GetComponent
        if (!ComponentCache.IsAI(__instance)) return;

        DeadLootSpawnContext.InOnDead = __instance;
    }

    private static void Finalizer()
    {
        DeadLootSpawnContext.InOnDead = null;
    }
}

[HarmonyPatch(typeof(CharacterMainControl), "OnDead")]
internal static class Patch_Client_OnDead_ReportCorpseTree
{
    private static void Postfix(CharacterMainControl __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return;

        // 仅"客户端 + 本机玩家"分支上报
        if (mod.IsServer) return;
        if (__instance != CharacterMainControl.Main) return;

        // ⭐ 已经上报过（= 主机已经/可以生成过尸体战利品），直接跳过，不再创建/同步
        if (LocalPlayerManager.Instance._cliCorpseTreeReported) return;

        // 【优化】延迟尸体树上报，避免死亡瞬间卡顿
        UniTask.Void(async () =>
        {
            try
            {
                await UniTask.Delay(50); // 延迟 50ms

                // 给客户端的 CreateFromItem 拦截补丁一个"正在死亡路径"的标记，避免本地也生成（双生）
                DeadLootSpawnContext.InOnDead = __instance;

                // 首次上报整棵"尸体装备树"给主机（你已有的方法）
                SendLocalPlayerStatus.Instance.Net_ReportPlayerDeadTree(__instance);

                // ✅ 标记"本轮生命已经上报过尸体树"
                LocalPlayerManager.Instance._cliCorpseTreeReported = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DEATH] ReportCorpseTree failed: {e}");
            }
            finally
            {
                DeadLootSpawnContext.InOnDead = null;
            }
        });
    }
}

[HarmonyPatch(typeof(CharacterMainControl), "OnDead")]
internal static class Patch_Server_OnDead_Host_UsePlayerTree
{
    private static void Postfix(CharacterMainControl __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;

        var lm = LevelManager.Instance;
        if (lm == null || __instance != lm.MainCharacter) return; // 只处理主机自己的本机主角

        COOPManager.Host_Handle.Server_HandleHostDeathViaTree(__instance); // ← 走“客户端同款”的树路径
    }
}

[HarmonyPatch(typeof(CharacterMainControl), "OnDead")]
internal static class Patch_Client_OnDead_MarkAll_ForBlock
{
    private static void Prefix(CharacterMainControl __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return;
        if (mod.IsServer) return; // 只在客户端打标记
        DeadLootSpawnContext.InOnDead = __instance;
    }

    private static void Finalizer()
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return;
        if (mod.IsServer) return;
        DeadLootSpawnContext.InOnDead = null;
    }
}

/// ////防AI挥砍客户端的NRE报错////////////////////////// ////防AI挥砍客户端的NRE报错////////////////////////// ////防AI挥砍客户端的NRE报错///////////////////////
[HarmonyPatch(typeof(CharacterMainControl), "GetHelmatItem")]
internal static class Patch_CMC_GetHelmatItem_NullSafe
{
    // 任何异常都吞掉并当作“没戴头盔”，避免打断 Health.Hurt
    private static Exception Finalizer(Exception __exception, CharacterMainControl __instance, ref Item __result)
    {
        if (__exception != null)
        {
            Debug.LogWarning($"[NET] Suppressed exception in GetHelmatItem() on {__instance?.name}: {__exception}");
            __result = null; // 相当于“无头盔”，正常继续后续伤害结算和 Buff 触发
            return null; // 吞掉异常
        }

        return null;
    }
}

[HarmonyPatch(typeof(CharacterMainControl), "GetArmorItem")]
internal static class Patch_CMC_GetArmorItem_NullSafe
{
    // 任何异常都吞掉并当作“没穿护甲”，让伤害继续
    private static Exception Finalizer(Exception __exception, CharacterMainControl __instance, ref Item __result)
    {
        if (__exception != null)
        {
            Debug.LogWarning($"[NET] Suppressed exception in GetArmorItem() on {__instance?.name}: {__exception}");
            __result = null; // 视为无甲，继续照常计算伤害&流血
            return null; // 吞掉异常
        }

        return null;
    }
}

/// ////防AI挥砍客户端的NRE报错////////////////////////// ////防AI挥砍客户端的NRE报错////////////////////////// ////防AI挥砍客户端的NRE报错///////////////////////
[HarmonyPatch(typeof(CharacterMainControl), nameof(CharacterMainControl.SetCharacterModel))]
internal static class Patch_CMC_SetCharacterModel_RebindNetAiFollower
{
    private static void Postfix(CharacterMainControl __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return;

        try
        {
            if (mod != null && mod.networkStarted && !mod.IsServer)
            {
                var id = -1;
                // 从 aiById 反查当前 CMC 对应的 aiId
                foreach (var kv in AITool.aiById)
                    if (kv.Value == __instance)
                    {
                        id = kv.Key;
                        break;
                    }

                if (id >= 0)
                {
                    var tag = __instance.GetComponent<NetAiTag>() ?? __instance.gameObject.AddComponent<NetAiTag>();
                    if (tag.aiId != id) tag.aiId = id;
                }
            }
        }
        catch
        {
        }

        // 只处理“真 AI”的远端复制体（本地玩家/主机侧不需要）
        // 你已有 IsRealAI(.) 判定；保持一致
        try
        {
            if (!mod.IsServer && AITool.IsRealAI(__instance))
                // 确保有 RemoteReplicaTag（你已用它在 MagicBlend.Update 里早退）
                if (!__instance.GetComponent<RemoteReplicaTag>())
                    __instance.gameObject.AddComponent<RemoteReplicaTag>();
        }
        catch
        {
        }

        // 强制通知 NetAiFollower 重新抓取当前模型的 Animator
        try
        {
            var follower = __instance.GetComponent<NetAiFollower>();
            if (follower) follower.ForceRebindAfterModelSwap();
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(CharacterMainControl), nameof(CharacterMainControl.SetCharacterModel))]
internal static class Patch_CMC_SetCharacterModel_TagAndRebindOnClient
{
    private static void Postfix(CharacterMainControl __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || mod.IsServer) return; // 只在客户端处理

        // 给客户端的 AI 复制体打上标记，并强制重绑 Animator
        // 【优化】使用 ComponentCache 避免重复 GetComponent
        if (ComponentCache.IsAI(__instance))
        {
            if (!__instance.GetComponent<RemoteReplicaTag>())
                __instance.gameObject.AddComponent<RemoteReplicaTag>();

            var follower = __instance.GetComponent<NetAiFollower>();
            if (follower) follower.ForceRebindAfterModelSwap();
        }
    }
}

[HarmonyPatch(typeof(CharacterMainControl), "get_Main")]
internal static class Patch_CMC_Main_OverrideDuringFSM
{
    private static bool Prefix(ref CharacterMainControl __result)
    {
        var ov = NcMainRedirector.Current;
        if (ov != null)
        {
            __result = ov;
            return false;
        }

        return true;
    }
}