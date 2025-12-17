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
using System.Reflection;
using Duckov.Utilities;
using Duckov.Weathers;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public static class MeleeLocalGuard
{
    [ThreadStatic] public static bool LocalMeleeTryingToHurt;
}

public sealed class RemoteReplicaTag : MonoBehaviour
{
}

public sealed class RemoteAIReplicaTag : MonoBehaviour
{
    public int Id;
    public bool SuppressBuffForward;
}

[HarmonyPatch(typeof(DamageReceiver), "Hurt")]
internal static class Patch_ServerForwardRemotePlayerDamage
{
    [HarmonyPriority(Priority.High)]
    private static bool Prefix(DamageReceiver __instance, ref DamageInfo __0)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return true;

        var health = __instance ? __instance.health : null;
        var cmc = health ? health.TryGetCharacter() : null;
        if (!cmc) return true;

        if (!cmc.GetComponentInChildren<RemoteReplicaTag>()) return true;

        var peer = CoopTool.TryGetPeerForCharacter(cmc);
        if (peer == null) return true;

        var service = NetService.Instance;
        var playerId = service != null ? service.GetPlayerId(peer) : string.Empty;

        if (service != null && !string.IsNullOrEmpty(playerId) && service.IsPlayerInvincible(playerId))
            return false;

        var rpc = new PlayerDamageForwardRpc
        {
            PlayerId = playerId,
            Damage = DamageForwardPayload.FromDamageInfo(__0)
        };

        CoopTool.SendRpcTo(peer, in rpc);
        return false;
    }
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

//[HarmonyPatch(typeof(TimeOfDayDisplay), "RefreshStormText")]
//internal static class Patch_TimeOfDayDisplay_UseSyncedStorm
//{
//    private static bool Prefix(TimeOfDayDisplay __instance, Duckov.Weathers.Weather _weather)
//    {
//        var mod = ModBehaviourF.Instance;
//        if (mod == null || mod.IsServer || !mod.networkStarted) return true;

//        var snapshot = EscapeFromDuckovCoopMod.Weather.LastStormSnapshot;
//        if (!snapshot.HasData) return true;

//        TimeSpan timeSpan;
//        float fillAmount;

//        switch (_weather)
//        {
//            case Duckov.Weathers.Weather.Stormy_I:
//                __instance.stormIndicatorAnimator.SetBool("Grow", false);
//                __instance.stormTitleText.text = __instance.StormPhaseIIETAKey.ToPlainText();
//                timeSpan = TimeSpan.FromSeconds(snapshot.StormIOverSeconds);
//                fillAmount = snapshot.StormRemainPercent;
//                __instance.stormDescObject.SetActive(LevelManager.Instance.IsBaseLevel);
//                break;
//            case Duckov.Weathers.Weather.Stormy_II:
//                __instance.stormIndicatorAnimator.SetBool("Grow", false);
//                __instance.stormTitleText.text = __instance.StormOverETAKey.ToPlainText();
//                timeSpan = TimeSpan.FromSeconds(snapshot.StormIIOverSeconds);
//                fillAmount = snapshot.StormRemainPercent;
//                __instance.stormDescObject.SetActive(LevelManager.Instance.IsBaseLevel);
//                break;
//            default:
//                __instance.stormIndicatorAnimator.SetBool("Grow", true);
//                timeSpan = TimeSpan.FromSeconds(snapshot.StormEtaSeconds);
//                fillAmount = snapshot.StormSleepPercent;
//                if (timeSpan.TotalHours < 24.0)
//                {
//                    __instance.stormTitleText.text = __instance.StormComingOneDayKey.ToPlainText();
//                    __instance.stormDescObject.SetActive(LevelManager.Instance.IsBaseLevel);
//                }
//                else
//                {
//                    __instance.stormTitleText.text = __instance.StormComingETAKey.ToPlainText();
//                    __instance.stormDescObject.SetActive(false);
//                }

//                break;
//        }

//        if (timeSpan.TotalSeconds < 0)
//            return true;

//        __instance.stormFillImage.fillAmount = Mathf.Clamp01(fillAmount);
//        __instance.stormText.text = string.Format("{0:000}:{1:00}", Mathf.FloorToInt((float)timeSpan.TotalHours), timeSpan.Minutes);
//        return false;
//    }
//}

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

[HarmonyPatch(typeof(Duckov.MiniMaps.SimplePointOfInterest), "get_DisplayName")]
public class SimplePointOfInterest_DisplayName_Patch
{
    static bool Prefix(Duckov.MiniMaps.SimplePointOfInterest __instance, ref string __result)
    {
        try
        {
            GameObject obj = __instance.gameObject;

            if (ModBehaviourF.PhantomPlayerNames.TryGetValue(obj, out var playerName))
            {
                __result = playerName;
                return false;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[联机] SimplePointOfInterest_DisplayName_Patch出错: {e.Message}");
        }

        return true;
    }
}