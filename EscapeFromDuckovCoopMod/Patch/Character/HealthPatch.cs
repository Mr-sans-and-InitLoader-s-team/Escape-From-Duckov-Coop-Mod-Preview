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

using Duckov.Scenes;
using Duckov.UI;
using Duckov.Utilities;
using System.Reflection.Emit;
using TMPro;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(HealthSimpleBase), "Awake")]
public static class Patch_HSB_Awake_TagRegister
{
    private static void Postfix(HealthSimpleBase __instance)
    {
        if (!__instance) return;

        var tag = __instance.GetComponent<NetDestructibleTag>();
        if (!tag) return; // 你已标注在墙/油桶上了，这里不再 AddComponent

        // —— BreakableWall：用墙根节点来计算稳定ID，避免主客机层级差导致错位 —— //
        var wallRoot = FindBreakableWallRoot(__instance.transform);
        if (wallRoot != null)
            try
            {
                var computed = NetDestructibleTag.ComputeStableId(wallRoot.gameObject);
                if (tag.id != computed) tag.id = computed;
            }
            catch
            {
            }

        // —— 幂等注册 —— //
        var mod = ModBehaviourF.Instance;
        if (mod != null) COOPManager.destructible.RegisterDestructible(tag.id, __instance);
    }

    // 向上找名字含“BreakableWall”的祖先（不区分大小写）
    private static Transform FindBreakableWallRoot(Transform t)
    {
        var p = t;
        while (p != null)
        {
            var nm = p.name;
            if (!string.IsNullOrEmpty(nm) &&
                nm.IndexOf("BreakableWall", StringComparison.OrdinalIgnoreCase) >= 0)
                return p;
            p = p.parent;
        }

        return null;
    }
}

// 客户端：阻断本地扣血，改为请求主机结算；
// 主机：照常结算（原方法运行），并在 Postfix 广播受击
[HarmonyPatch(typeof(HealthSimpleBase), "OnHurt")]
public static class Patch_HSB_OnHurt_RedirectNet
{
    private static bool Prefix(HealthSimpleBase __instance, DamageInfo dmgInfo)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;

        if (!mod.IsServer)
        {
            // 本地 UI：子弹/爆炸统一点亮 Hit；若你能在此处判断“必死”，可传 true 亮 Kill
            LocalHitKillFx.ClientPlayForDestructible(__instance, dmgInfo, false);

            var tag = __instance.GetComponent<NetDestructibleTag>();
            if (!tag) tag = __instance.gameObject.AddComponent<NetDestructibleTag>();
            COOPManager.HurtM.Client_RequestDestructibleHurt(tag.id, dmgInfo);
            return false;
        }

        return true;
    }

    private static void Postfix(HealthSimpleBase __instance, DamageInfo dmgInfo)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;

        var tag = __instance.GetComponent<NetDestructibleTag>();
        if (!tag) return;
        COOPManager.destructible.Server_BroadcastDestructibleHurt(tag.id, __instance.HealthValue, dmgInfo);
    }
}

// 主机在死亡后广播；客户端收到“死亡广播”时只做视觉切换
[HarmonyPatch(typeof(HealthSimpleBase), "Dead")]
public static class Patch_HSB_Dead_Broadcast
{
    private static void Postfix(HealthSimpleBase __instance, DamageInfo dmgInfo)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;

        var tag = __instance.GetComponent<NetDestructibleTag>();
        if (!tag) return;
        COOPManager.destructible.Server_BroadcastDestructibleDead(tag.id, dmgInfo);
    }
}

[HarmonyPatch(typeof(HealthSimpleBase), "OnHurt")]
internal static class Patch_HealthSimpleBase_OnHurt_RedirectNet
{
    private static bool Prefix(HealthSimpleBase __instance, ref DamageInfo __0)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;

        // 必须是本机玩家的命中才拦截；防止 AI 打障碍物也触发 UI
        var from = __0.fromCharacter;
        var fromLocalMain = from == CharacterMainControl.Main;

        if (!mod.IsServer && fromLocalMain)
        {
            // 预测是否致死（简单用 HealthValue 判断，足够做“演出预判”）
            var predictedDead = false;
            try
            {
                var cur = __instance.HealthValue;
                predictedDead = cur > 0f && __0.damageValue >= cur - 0.001f;
            }
            catch
            {
            }

            LocalHitKillFx.ClientPlayForDestructible(__instance, __0, predictedDead);

            // 继续你的原有逻辑：把命中发给主机权威结算
            return false;
        }

        return true;
    }
}

// 统一给所有可破坏体（HealthSimpleBase）打上 NetDestructibleTag 并注册进索引
[HarmonyPatch(typeof(HealthSimpleBase), "Awake")]
internal static class Patch_HSB_Awake_AddTagAndRegister
{
    private static void Postfix(HealthSimpleBase __instance)
    {
        try
        {
            var mod = ModBehaviourF.Instance;
            if (mod == null) return;

            // 没有就补一个
            var tag = __instance.GetComponent<NetDestructibleTag>();
            if (!tag) tag = __instance.gameObject.AddComponent<NetDestructibleTag>();

            // 尽量用“墙体根”等稳定根节点算稳定ID；失败则退回到自身
            uint id = 0;
            try
            {
                // 你已有的稳定ID算法在 Mod.cs 里；这里直接复用 NetDestructibleTag 的稳定计算兜底
                id = NetDestructibleTag.ComputeStableId(__instance.gameObject);
            }
            catch
            {
                /* 忽略差异 */
            }

            tag.id = id;
            COOPManager.destructible.RegisterDestructible(id, __instance);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Coop][HSB.Awake] Tag/Register failed: {ex}");
        }
    }
}

// 兜底：即使有第三方路径仍触发 DestroyOnDelay，吞掉异常防止打断主循环（可选）
[HarmonyPatch(typeof(Health), "DestroyOnDelay")]
internal static class Patch_Health_DestroyOnDelay_Finalizer
{
    private static Exception Finalizer(Exception __exception)
    {
        // 返回 null 表示吞掉异常
        if (__exception != null)
            Debug.LogWarning("[COOP] Swallow DestroyOnDelay exception: " + __exception.Message);
        return null;
    }
}

[HarmonyPatch(typeof(Health), "Hurt")]
internal static class Patch_Health_Hurt_AIdEAD
{
    private static void Postfix(Health __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return;

        if (__instance.TryGetCharacter().GetComponentInChildren<RemoteAIReplicaTag>() == null) return;

        if (!mod.IsServer)
        { 
            if(__instance.IsDead)
            {
                var forced = new DamageInfo();
                forced.damageValue = 99999f;
                forced.finalDamage = 99999f;;
                COOPManager.AI.Client_ReportAiHealth(__instance.TryGetCharacter().GetComponentInChildren<RemoteAIReplicaTag>().Id, __instance, forced);
            }
        }

    }
}

[HarmonyPatch(typeof(Health), "Hurt")]
internal static class Patch_Health_Hurt_RemoteAnti
{
    private static bool Prefix(Health __instance, ref DamageInfo damageInfo)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;

        if (__instance.TryGetCharacter().GetComponentInChildren<RemoteAIReplicaTag>() != null) return true;
        if (__instance.TryGetCharacter().GetComponentInChildren<AICharacterController>() != null) return true;
        if (__instance.TryGetCharacter().GetComponentInChildren<AutoRequestHealthBar>() != null) return false;

       

        return true;
    }
}


[HarmonyPatch(typeof(Health), "Hurt")]
internal static class Patch_Health_Hurt_HPClamp
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var setter = AccessTools.PropertySetter(typeof(Health), "CurrentHealth");
        var helper = AccessTools.Method(typeof(HealthILHelper), nameof(HealthILHelper.AfterApplyFinalDamage));

        bool injected = false;

        foreach (var code in instructions)
        {

            yield return code;


            if (!injected && code.Calls(setter))
            {
                injected = true;

 
                yield return new CodeInstruction(OpCodes.Ldarg_0); // this
                yield return new CodeInstruction(OpCodes.Ldarg_1); // damageInfo
                yield return new CodeInstruction(OpCodes.Call, helper);
            }
        }
    }
}

public static class HealthILHelper
{

    public static void AfterApplyFinalDamage(Health __instance, DamageInfo damageInfo)
    {
        var chr = __instance.TryGetCharacter();


        if (MultiSceneCore.Instance.SceneInfo.ID == "Base"
            && !ModBehaviourF.Instance.IsServer
            && chr != null
            && chr.characterModel.name == "CharacterModel_Dummy_1(Clone)")
        {

            if (__instance.CurrentHealth <= 0f)
            {
                __instance.CurrentHealth = 1f;
            }

        }
    }
}

