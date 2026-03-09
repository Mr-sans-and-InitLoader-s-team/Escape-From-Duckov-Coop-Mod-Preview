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
using HarmonyLib;
using System;
using System.Reflection;
using System.Reflection.Emit;
using TMPro;
using UnityEngine;
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
    private static float ReadMaxHealthValue(HealthSimpleBase hs)
    {
        return Destructible.ReadMaxHealthValue(hs, 0f);
    }

    private static void Postfix(HealthSimpleBase __instance, DamageInfo dmgInfo)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return;

        var tag = __instance.GetComponent<NetDestructibleTag>();
        if (!tag) tag = __instance.gameObject.AddComponent<NetDestructibleTag>();

        if (!mod.IsServer)
        {
            // 仅转发本机玩家造成的伤害，避免 AI/环境事件反复覆写主机血量
            if (dmgInfo.fromCharacter == null || dmgInfo.fromCharacter != CharacterMainControl.Main)
                return;

            var current = __instance != null ? __instance.HealthValue : 0f;
            if (current > 0f)
                COOPManager.destructible.Client_ReportDestructibleHealth(tag.id, ReadMaxHealthValue(__instance), current, false, dmgInfo);
            return;
        }

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
        if (mod == null || !mod.networkStarted) return;

        var tag = __instance.GetComponent<NetDestructibleTag>();
        if (!tag) tag = __instance.gameObject.AddComponent<NetDestructibleTag>();
        if (!tag) return;

        if (!mod.IsServer)
        {
            var reported = dmgInfo;
            reported.damageValue = 999999f;
            reported.finalDamage = Math.Max(reported.finalDamage, 999999f);
            COOPManager.destructible.Client_ReportDestructibleHealth(tag.id, Destructible.ReadMaxHealthValue(__instance, 0f), 0f, true, reported);
            return;
        }

        COOPManager.destructible.Server_RegisterDestructibleDeath(tag.id);
        COOPManager.destructible.Server_BroadcastDestructibleDead(tag.id, dmgInfo);
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

        if (Patch_Health_Hurt_RemoteAnti.IsSimulatingRemoteAiHurt)
            return;

        if (mod.IsServer)
        {
            if(__instance.TryGetCharacter().GetComponentInChildren<RemoteAIReplicaTag>() == null)
            {
                if (__instance.IsDead)
                {
                    DeadLootSpawnContext.InOnDead = __instance.TryGetCharacter();
                }
            }         
        }

        var character = __instance.TryGetCharacter();
        if (character == null) return;

        var remoteTag = character.GetComponentInChildren<RemoteAIReplicaTag>();
        if (remoteTag == null) return;

        if (!mod.IsServer && __instance.IsDead)
        {
            var alreadyDead = false;
            if (CoopSyncDatabase.AI.TryGet(remoteTag.Id, out var entry) && entry != null)
            {
                alreadyDead = entry.Status == AIStatus.Dead || entry.CurrentHealth <= 0.0001f;
            }

            if (!alreadyDead)
            {
                var forced = new DamageInfo
                {
                    damageValue = 99999f,
                    finalDamage = 99999f
                };
                COOPManager.AI?.Client_ReportAiHealth(remoteTag.Id, __instance, forced);
            }
        }
    }
}

[HarmonyPatch(typeof(Health), "Hurt")]
internal static class Patch_Health_Hurt_RemoteAnti
{
    [ThreadStatic] private static bool _simulatingRemoteAiHurt;

    internal static bool IsSimulatingRemoteAiHurt => _simulatingRemoteAiHurt;

    private static (float max, float cur, float body, float head) Snapshot(Health health)
    {
        float max = 0f, cur = 0f, body = 0f, head = 0f;

        try { max = health.MaxHealth; } catch { }
        try { cur = health.CurrentHealth; } catch { }
        try { body = health.BodyArmor; } catch { }
        try { head = health.HeadArmor; } catch { }

        if (max <= 0f)
            max = Mathf.Max(cur, 1f);

        return (max, cur, body, head);
    }

    private static void Restore(Health health, (float max, float cur, float body, float head) snap)
    {
        if (health == null) return;
        try
        {
            HealthM.Instance.ForceSetHealth(health, snap.max > 0f ? snap.max : Mathf.Max(1f, snap.cur), snap.cur, true, snap.body, snap.head);
        }
        catch
        {
        }
    }

    private static bool Prefix(Health __instance, ref DamageInfo damageInfo)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;

        //if (_simulatingRemoteAiHurt)
        //    return true;

        //if (!mod.IsServer && CharacterMainControl.Main != null && damageInfo.fromCharacter == CharacterMainControl.Main)
        //    DamageStatsTracker.Instance?.RecordLocalDamage(damageInfo.damageValue);

        //if (mod.IsServer && CharacterMainControl.Main != null)
        //{
        //    var chr = __instance.TryGetCharacter();
        //    if (chr != null && chr.GetComponentInChildren<AICharacterController>() != null)
        //        DamageStatsTracker.Instance?.RecordLocalDamage(damageInfo.damageValue);
        //}

        // 客户端侧的远端 AI：仅转发给主机，不在本地再次结算伤害，避免“假伤害”比主机高
        var remoteAi = __instance.TryGetCharacter().GetComponentInChildren<RemoteAIReplicaTag>();
        if (remoteAi != null && !mod.IsServer && damageInfo.fromCharacter != null && damageInfo.fromCharacter == LevelManager.Instance.MainCharacter)
        {
            COOPManager.AI._clientLastDamage[remoteAi.Id] = damageInfo;
            //COOPManager.AI?.Client_ReportAiHealth(remoteAi.Id, __instance, damageInfo);

            //// 可选的“本地视觉预测”：在本地运行一次 Hurt 让击中特效/僵直正确，但随后立即恢复血量，避免和主机权威值偏差
            //var snap = Snapshot(__instance);
            //try
            //{
            //    _simulatingRemoteAiHurt = true;
            //    __instance.Hurt(damageInfo);
            //}
            //catch (Exception ex)
            //{
            //    Debug.LogWarning($"[COOP] Remote AI local hurt simulate failed: {ex.Message}");
            //}
            //finally
            //{
            //    _simulatingRemoteAiHurt = false;
            //    Restore(__instance, snap);
            //}

            COOPManager.AI?.Client_ReportAiHealth(remoteAi.Id, __instance, damageInfo);
            return true;
        }

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

        if (MultiSceneCore.Instance.SceneInfo.ID == "Base" && __instance.team == Teams.middle)
        {

            if (__instance.CurrentHealth <= 0f)
            {
                __instance.CurrentHealth = 1f;
            }

        }
    }
}

//[HarmonyPatch(typeof(HealthBar), "Refresh")]
//internal static class Patch_Health_Refresh_Finalizer
//{
//    private static readonly Color RemoteHealthColor = new Color(0.227f, 0.839f, 0.227f, 1f);
//    private static void Postfix(HealthBar __instance)
//    {
//        var mod = ModBehaviourF.Instance;
//        if (mod == null || !mod.networkStarted)
//            return;
//        var health = __instance.target;

//        Debug.Log("CreateHealthBarFor is ready");
//        if (IsLocalPlayerHealth(health))
//            return;

//        if (__instance == null || health == null)
//        {
//            Debug.Log("CreateHealthBarFor __result or health null");

//            return;
//        }

//        if (health.TryGetCharacter().GetComponentInChildren<AICharacterController>() != null)
//        {
//            Debug.Log("AICharacterController");
//            return;
//        }

//        if (health.TryGetCharacter().GetComponentInChildren<RemoteAIReplicaTag>() != null)
//        {
//            Debug.Log("RemoteAIReplicaTag");
//            return;
//        }

//        if (!TryResolvePlayerMetadata(health, out var playerId, out var fallbackName))
//        {
//            Debug.Log("TryResolvePlayerMetadata null");
//            return;
//        }

//        Traverse.Create(__instance).Field<Image>("fill").Value.color = RemoteHealthColor;
//        Traverse.Create(__instance).Field<Image>("followFill").Value.color = RemoteHealthColor;
//    }

//    private static bool IsLocalPlayerHealth(Health health)
//    {
//        var cmc = health.TryGetCharacter();
//        return cmc != null && cmc == CharacterMainControl.Main;
//    }



//    private static bool TryResolvePlayerMetadata(Health health, out string playerId, out string fallbackName)
//    {
//        playerId = null;
//        fallbackName = null;

//        var service = NetService.Instance;
//        if (service == null)
//            return false;

//        var cmc = health.TryGetCharacter();
//        if (cmc != null && cmc == CharacterMainControl.Main)
//        {
//            playerId = service.localPlayerStatus?.EndPoint;
//            fallbackName = service.localPlayerStatus?.PlayerName;
//            return !string.IsNullOrEmpty(playerId) || !string.IsNullOrEmpty(fallbackName);
//        }

//        if (service.IsServer)
//        {
//            foreach (var kv in service.remoteCharacters)
//            {
//                var go = kv.Value;
//                if (go == null)
//                    continue;

//                if (!health.transform.IsChildOf(go.transform))
//                    continue;

//                service.playerStatuses.TryGetValue(kv.Key, out var status);
//                playerId = service.GetPlayerId(kv.Key);
//                fallbackName = status?.PlayerName;
//                break;
//            }
//        }
//        else
//        {
//            foreach (var kv in service.clientRemoteCharacters)
//            {
//                var go = kv.Value;
//                if (go == null)
//                    continue;

//                if (!health.transform.IsChildOf(go.transform))
//                    continue;

//                service.clientPlayerStatuses.TryGetValue(kv.Key, out var status);
//                playerId = kv.Key;
//                fallbackName = status?.PlayerName;
//                break;
//            }
//        }

//        return !string.IsNullOrEmpty(playerId) || !string.IsNullOrEmpty(fallbackName);
//    }
//}

//[HarmonyPatch(typeof(HealthBarManager), "CreateHealthBarFor")]
//internal static class Patch_HealthBarManager_CreateHealthBarFor
//{
//    private static readonly Color RemoteHealthColor = new Color(0.227f, 0.839f, 0.227f, 1f);

//    private static void Postfix(HealthBar __result, ref Health health)
//    {
//        var mod = ModBehaviourF.Instance;
//        if (mod == null || !mod.networkStarted)
//            return;
//        Debug.Log("CreateHealthBarFor is ready");
//        if (IsLocalPlayerHealth(health))
//            return;

//        if (__result == null || health == null)
//        {
//            Debug.Log("CreateHealthBarFor __result or health null");

//            return;
//        }

//        if (health.TryGetCharacter().GetComponentInChildren<AICharacterController>() != null)
//        {
//            Debug.Log("AICharacterController");
//            return;
//        }

//        if (health.TryGetCharacter().GetComponentInChildren<RemoteAIReplicaTag>() != null)
//        {
//            Debug.Log("RemoteAIReplicaTag");
//            return;
//        }

//        if (!TryResolvePlayerMetadata(health, out var playerId, out var fallbackName))
//        {
//            Debug.Log("TryResolvePlayerMetadata null");
//            return;
//        }

 
//        Traverse.Create(__result).Field<Image>("fill").Value.color = RemoteHealthColor;
//        Traverse.Create(__result).Field<Image>("followFill").Value.color = RemoteHealthColor;
//        //Traverse.Create(__result).Field<Image>("hurtBlink").Value.color = RemoteHealthColor;
//        //简单粗暴
//        //var display = __result.GetComponent<HealthBarNameDisplay>() ?? __result.gameObject.AddComponent<HealthBarNameDisplay>();
//        // display.Configure(playerId, fallbackName, health, __result);
//    }

//    private static bool IsLocalPlayerHealth(Health health)
//    {
//        var cmc = health.TryGetCharacter();
//        return cmc != null && cmc == CharacterMainControl.Main;
//    }

 

//    private static bool TryResolvePlayerMetadata(Health health, out string playerId, out string fallbackName)
//    {
//        playerId = null;
//        fallbackName = null;

//        var service = NetService.Instance;
//        if (service == null)
//            return false;

//        var cmc = health.TryGetCharacter();
//        if (cmc != null && cmc == CharacterMainControl.Main)
//        {
//            playerId = service.localPlayerStatus?.EndPoint;
//            fallbackName = service.localPlayerStatus?.PlayerName;
//            return !string.IsNullOrEmpty(playerId) || !string.IsNullOrEmpty(fallbackName);
//        }

//        if (service.IsServer)
//        {
//            foreach (var kv in service.remoteCharacters)
//            {
//                var go = kv.Value;
//                if (go == null)
//                    continue;

//                if (!health.transform.IsChildOf(go.transform))
//                    continue;

//                service.playerStatuses.TryGetValue(kv.Key, out var status);
//                playerId = service.GetPlayerId(kv.Key);
//                fallbackName = status?.PlayerName;
//                break;
//            }
//        }
//        else
//        {
//            foreach (var kv in service.clientRemoteCharacters)
//            {
//                var go = kv.Value;
//                if (go == null)
//                    continue;

//                if (!health.transform.IsChildOf(go.transform))
//                    continue;

//                service.clientPlayerStatuses.TryGetValue(kv.Key, out var status);
//                playerId = kv.Key;
//                fallbackName = status?.PlayerName;
//                break;
//            }
//        }

//        return !string.IsNullOrEmpty(playerId) || !string.IsNullOrEmpty(fallbackName);
//    }
//}
