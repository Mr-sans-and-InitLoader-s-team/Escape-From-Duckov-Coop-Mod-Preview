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

using System.Reflection;
using Duckov.Utilities;
using FX;

namespace EscapeFromDuckovCoopMod;

public sealed class LocalMeleeOncePerFrame : MonoBehaviour
{
    public int lastFrame;
}

public static class MeleeLocalGuard
{
    [ThreadStatic] public static bool LocalMeleeTryingToHurt;
}

[HarmonyPatch(typeof(DamageReceiver), "Hurt")]
internal static class Patch_ClientReportMeleeHit
{
    private static bool Prefix(DamageReceiver __instance, ref DamageInfo __0)
    {
        var mod = ModBehaviourF.Instance;

        // 不在联网、在主机、或没到“本地近战结算阶段”，都不拦
        if (mod == null || !mod.networkStarted || mod.IsServer || !MeleeLocalGuard.LocalMeleeTryingToHurt)
            return true;

        if (mod.connectedPeer == null)
        {
            Debug.LogWarning("[CLIENT] MELEE_HIT_REPORT aborted: connectedPeer==null, fallback to local Hurt");
            return true; // 让原始 Hurt 生效，避免“无伤”
        }

        try
        {
            var w = new NetDataWriter();
            w.Put((byte)Op.MELEE_HIT_REPORT);
            w.Put(mod.localPlayerStatus != null ? mod.localPlayerStatus.EndPoint : "");

            // DamageInfo 关键字段
            w.Put(__0.damageValue);
            w.Put(__0.armorPiercing);
            w.Put(__0.critDamageFactor);
            w.Put(__0.critRate);
            w.Put(__0.crit);

            w.PutV3cm(__0.damagePoint);
            w.PutDir(__0.damageNormal);

            w.Put(__0.fromWeaponItemID);
            w.Put(__0.bleedChance);
            w.Put(__0.isExplosion);

            // 近战范围（主机用于邻域搜）
            var range = 1.2f;
            try
            {
                var main = CharacterMainControl.Main;
                var melee = main ? main.CurrentHoldItemAgent as ItemAgent_MeleeWeapon : null;
                if (melee != null) range = Mathf.Max(0.6f, melee.AttackRange);
            }
            catch
            {
            }

            w.Put(range);


            mod.connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CLIENT] Melee hit report failed: " + e);
            return true; // 发送失败时回退到本地 Hurt，避免“无伤”
        }

        try
        {
            if (PopText.instance)
            {
                // 取默认物理伤害的弹字样式（跟 Health.Hurt 一致的来源）
                var look = GameplayDataSettings.UIStyle
                    .GetElementDamagePopTextLook(ElementTypes.physics);

                // 位置：优先用伤害点；没有就用受击者位置；整体上抬一点更清晰
                var pos = (__0.damagePoint.sqrMagnitude > 1e-6f ? __0.damagePoint : __instance.transform.position)
                          + Vector3.up * 2f;

                // 暴击大小/图标
                var size = __0.crit > 0 ? look.critSize : look.normalSize;
                var sprite = __0.crit > 0 ? GameplayDataSettings.UIStyle.CritPopSprite : null;

                // 文本：有数值就显示数值，没有就显示“HIT”
                var text = __0.damageValue > 0f ? __0.damageValue.ToString("F1") : "HIT";

                PopText.Pop(text, pos, look.color, size, sprite);
            }
        }
        catch
        {
        }


        // 成功上报，由主机权威结算
        return false;
    }
}

public sealed class RemoteReplicaTag : MonoBehaviour
{
}

[HarmonyPatch(typeof(SetActiveByPlayerDistance), "FixedUpdate")]
internal static class Patch_SABPD_FixedUpdate_AllPlayersUnion
{
    private static NetService Service => NetService.Instance;
    private static Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;

    private static bool Prefix(SetActiveByPlayerDistance __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true; // 单机：走原版

        var tr = Traverse.Create(__instance);

        // 被管理对象列表
        var list = tr.Field<List<GameObject>>("cachedListRef").Value;
        if (list == null) return false;

        // 距离阈值
        float dist;
        var prop = AccessTools.Property(__instance.GetType(), "Distance");
        if (prop != null) dist = (float)prop.GetValue(__instance, null);
        else dist = tr.Field<float>("distance").Value;
        var d2 = dist * dist;

        // === 收集所有在线玩家的位置（本地 + 远端） ===
        var sources = new List<Vector3>(8);
        var main = CharacterMainControl.Main;
        if (main) sources.Add(main.transform.position);

        foreach (var kv in playerStatuses)
        {
            var st = kv.Value;
            if (st != null && st.IsInGame) sources.Add(st.Position);
        }

        // 没拿到位置：放行原版
        if (sources.Count == 0) return true;

        // 逐个对象：任一玩家在范围内就激活
        for (var i = 0; i < list.Count; i++)
        {
            var go = list[i];
            if (!go) continue;

            var within = false;
            var p = go.transform.position;
            for (var s = 0; s < sources.Count; s++)
                if ((p - sources[s]).sqrMagnitude <= d2)
                {
                    within = true;
                    break;
                }

            if (go.activeSelf != within) go.SetActive(within);
        }

        return false; // 跳过原方法
    }
}

[HarmonyPatch(typeof(DamageReceiver), "Hurt")]
internal static class Patch_BlockClientAiVsAi_AtReceiver
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(DamageReceiver __instance, ref DamageInfo __0)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || mod.IsServer) return true;

        var target = __instance ? __instance.GetComponentInParent<CharacterMainControl>() : null;
        var victimIsAI = target && (target.GetComponent<AICharacterController>() != null || target.GetComponent<NetAiTag>() != null);
        if (!victimIsAI) return true;

        var attacker = __0.fromCharacter;
        var attackerIsAI = attacker && (attacker.GetComponent<NetAiTag>() != null || attacker.GetComponent<NetAiTag>() != null);
        if (attackerIsAI) return false; // 不让伤害继续走向 Health

        return true;
    }
}

[HarmonyPatch(typeof(SetActiveByPlayerDistance), "FixedUpdate")]
internal static class Patch_SABD_KeepRemoteAIActive_Client
{
    private static void Postfix(SetActiveByPlayerDistance __instance)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || m.IsServer) return;

        var forceAll = m.Client_ForceShowAllRemoteAI;
        if (forceAll) Traverse.Create(__instance).Field<float>("distance").Value = 9999f;
    }
}

[HarmonyPatch(typeof(DamageReceiver), "Hurt")]
internal static class Patch_ClientMelee_HurtRedirect_Destructible
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(DamageReceiver __instance, ref DamageInfo __0)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || m.IsServer) return true;

        // 只拦“本地玩家的近战结算帧”
        if (!MeleeLocalGuard.LocalMeleeTryingToHurt) return true;

        // 仅处理环境可破坏体
        var hs = __instance ? __instance.GetComponentInParent<HealthSimpleBase>() : null;
        if (!hs) return true;

        // 计算/获取稳定 id
        uint id = 0;
        var tag = hs.GetComponent<NetDestructibleTag>();
        if (tag) id = tag.id;
        if (id == 0)
            try
            {
                id = NetDestructibleTag.ComputeStableId(hs.gameObject);
            }
            catch
            {
            }

        if (id == 0) return true; // 算不出 id，就放行给原逻辑，避免“打不掉”

        // 正确的调用：传 id，而不是传 HealthSimpleBase
        COOPManager.HurtM.Client_RequestDestructibleHurt(id, __0);
        return false; // 阻止本地结算，等主机广播
    }
}

//观战
[HarmonyPatch]
internal static class Patch_ClosureView_ShowAndReturnTask_SpectatorGate
{
    private static MethodBase TargetMethod()
    {
        var t = AccessTools.TypeByName("Duckov.UI.ClosureView");
        if (t == null) return null;
        return AccessTools.Method(t, "ShowAndReturnTask", new[] { typeof(DamageInfo), typeof(float) });
    }

    private static bool Prefix(ref UniTask __result, DamageInfo dmgInfo, float duration)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;

        if (Spectator.Instance._skipSpectatorForNextClosure)
        {
            Spectator.Instance._skipSpectatorForNextClosure = false;
            __result = UniTask.CompletedTask;
            return true;
        }

        // 如果还有队友活着，走观战并阻止结算 UI
        if (Spectator.Instance.TryEnterSpectatorOnDeath(dmgInfo))
            //  __result = UniTask.CompletedTask;
            // ClosureView.Instance.gameObject.SetActive(false);
            return true; // 拦截原方法

        return true;
    }
}

[HarmonyPatch(typeof(GameManager), "get_Paused")]
internal static class Patch_Paused_AlwaysFalse
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(ref bool __result)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;

        __result = false;

        return false;
    }
}

[HarmonyPatch(typeof(PauseMenu), "Show")]
internal static class Patch_PauseMenuShow_AlwaysFalse
{
    [HarmonyPriority(Priority.First)]
    [HarmonyPostfix]
    private static void Postfix()
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return;

        mod.Pausebool = true;
    }
}

[HarmonyPatch(typeof(PauseMenu), "Hide")]
internal static class Patch_PauseMenuHide_AlwaysFalse
{
    [HarmonyPriority(Priority.First)]
    [HarmonyPostfix]
    private static void Postfix()
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return;

        mod.Pausebool = false;
    }
}

internal static class NcMainRedirector
{
    [field: ThreadStatic] public static CharacterMainControl Current { get; private set; }

    public static void Set(CharacterMainControl cmc)
    {
        Current = cmc;
    }

    public static void Clear()
    {
        Current = null;
    }
}

//[HarmonyPatch(typeof(ZoneDamage), "Damage")]
//static class Patch_Mapen_ZoneDamage
//{
//    static bool Prefix(ZoneDamage __instance)
//    {
//        var mod = ModBehaviour.Instance;
//        if (mod == null || !mod.networkStarted) return true; 

//        foreach (Health health in __instance.zone.Healths)
//        {
//            if(health.gameObject == null)
//            {
//                return false;
//            }
//            if(health.gameObject.GetComponent<AutoRequestHealthBar>() != null)
//            {
//                return false;
//            }
//        }

//        return true;
//    }
//}

//[HarmonyPatch(typeof(StormWeather), "Update")]
//static class Patch_StormWeather_Update
//{
//    [HarmonyPrefix]
//    static bool Prefix(StormWeather __instance)
//    {
//        if (!LevelManager.LevelInited)
//        {
//            return false;
//        }
//        var tg = Traverse.Create(__instance).Field<CharacterMainControl>("target").Value;
//        if (tg != null)
//        {
//            if (tg.gameObject.GetComponent<AutoRequestHealthBar>() != null)
//            {
//                return false;
//            }
//        }
//        return true;
//    }
//}