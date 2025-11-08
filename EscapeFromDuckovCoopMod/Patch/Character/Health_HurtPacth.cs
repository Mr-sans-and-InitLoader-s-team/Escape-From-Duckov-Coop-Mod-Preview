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
using System;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

// 【优化】Health 缓存辅助类
internal static class HealthCache
{
    // 【优化】缓存 Health -> Proxy 判定结果
    private static readonly Dictionary<Health, bool> _proxyCache = new();
    
    // 【优化】缓存 Health -> Main 判定结果
    private static readonly Dictionary<Health, bool> _mainCache = new();
    
    // 【优化】缓存 Health -> NetPeer 映射关系
    private static readonly Dictionary<Health, NetPeer> _ownerCache = new();

    public static bool IsProxy(Health health)
    {
        if (health == null) return false;
        
        if (!_proxyCache.TryGetValue(health, out var isProxy))
        {
            isProxy = health.gameObject.GetComponent<AutoRequestHealthBar>() != null;
            _proxyCache[health] = isProxy;
        }
        
        return isProxy;
    }

    public static bool IsMain(Health health)
    {
        if (health == null) return false;
        
        if (!_mainCache.TryGetValue(health, out var isMain))
        {
            try
            {
                isMain = health.IsMainCharacterHealth;
            }
            catch
            {
                isMain = false;
            }
            _mainCache[health] = isMain;
        }
        
        return isMain;
    }

    public static NetPeer GetOwner(Health health)
    {
        if (health == null) return null;
        
        if (!_ownerCache.TryGetValue(health, out var owner))
        {
            owner = HealthTool.Server_FindOwnerPeerByHealth(health);
            if (owner != null)
                _ownerCache[health] = owner;
        }
        
        return owner;
    }

    // 【优化】在玩家连接/断开时更新缓存
    public static void RegisterOwner(Health health, NetPeer peer)
    {
        if (health != null && peer != null)
            _ownerCache[health] = peer;
    }

    public static void ClearOwner(NetPeer peer)
    {
        // 清理该 peer 的所有映射
        var keysToRemove = _ownerCache.Where(kv => kv.Value == peer).Select(kv => kv.Key).ToList();
        foreach (var key in keysToRemove)
            _ownerCache.Remove(key);
    }

    public static void Clear()
    {
        _proxyCache.Clear();
        _mainCache.Clear();
        _ownerCache.Clear();
    }
}

[HarmonyPatch(typeof(Health), "Hurt", typeof(DamageInfo))]
internal static class Patch_AIHealth_Hurt_HostAuthority
{
    [HarmonyPriority(Priority.High)]
    private static bool Prefix(Health __instance, ref DamageInfo damageInfo)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;
        if (mod.IsServer) return true; // 主机照常
        
        // 【优化】使用缓存判定
        if (HealthCache.IsMain(__instance)) return true;
        if (HealthCache.IsProxy(__instance)) return false;

        // 是否 AI
        CharacterMainControl victim = null;
        try
        {
            victim = __instance.TryGetCharacter();
        }
        catch
        {
        }

        if (!victim)
            try
            {
                victim = __instance.GetComponentInParent<CharacterMainControl>();
            }
            catch
            {
            }

        // 【优化】使用 ComponentCache 避免重复 GetComponent
        var victimIsAI = ComponentCache.IsAI(victim);
        if (!victimIsAI) return true;

        var attacker = damageInfo.fromCharacter;
        if (attacker == CharacterMainControl.Main)
            return true; // 本机玩家命中 AI：允许本地结算

        // —— 不处理 AI→AI ——
        // 【优化】使用 ComponentCache 避免重复 GetComponent
        var attackerIsAI = ComponentCache.IsAI(attacker);
        if (attackerIsAI)
            return false; // 直接阻断，AI↔AI 不做任何本地效果

        return false;
    }

    // 主机在结算后广播 AI 当前血量（你已有的广播逻辑，保留）
    private static void Postfix(Health __instance, DamageInfo damageInfo)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;

        var cmc = __instance.TryGetCharacter();
        if (!cmc)
            try
            {
                cmc = __instance.GetComponentInParent<CharacterMainControl>();
            }
            catch
            {
            }

        if (!cmc) return;

        // 【优化】使用 ComponentCache 避免重复 GetComponent
        var tag = ComponentCache.GetNetAiTag(cmc);
        if (!tag) return;

        if (ModBehaviourF.LogAiHpDebug)
            Debug.Log($"[AI-HP][SERVER] Hurt => broadcast aiId={tag.aiId} cur={__instance.CurrentHealth}");
        COOPManager.AIHealth.Server_BroadcastAiHealth(tag.aiId, __instance.MaxHealth, __instance.CurrentHealth);
    }
}

// ========== 客户端：拦截 Health.Hurt（AI 被打） -> 仅本机玩家命中时播放本地特效/数字，然后发给主机 ==========
[HarmonyPatch(typeof(Health), "Hurt")]
internal static class Patch_Health
{
    [ThreadStatic] private static bool _cliReport;
    [ThreadStatic] private static int _cliReportAiId;
    [ThreadStatic] private static float _cliReportPrevHp;

    private static bool Prefix(Health __instance, ref DamageInfo __0)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;

        // 【优化】使用缓存判定
        if (HealthCache.IsProxy(__instance)) return false;

        // 受击者是不是 AI/NPC
        CharacterMainControl victimCmc = null;
        try
        {
            victimCmc = __instance ? __instance.TryGetCharacter() : null;
        }
        catch
        {
        }

        var isAiVictim = victimCmc && victimCmc != CharacterMainControl.Main;

        // 攻击者是不是本机玩家
        var from = __0.fromCharacter;
        var fromLocalMain = from == CharacterMainControl.Main;

        _cliReport = false;

        // 仅客户端 + 仅本机玩家打到 AI 时，走"拦截→本地播特效→网络上报"
        if (!mod.IsServer && isAiVictim && fromLocalMain)
        {
            // 【优化】使用 ComponentCache 避免重复 GetComponent
            var tag = ComponentCache.GetNetAiTag(victimCmc);
            if (tag != null && tag.aiId != 0)
            {
                _cliReport = true;
                _cliReportAiId = tag.aiId;
                try
                {
                    _cliReportPrevHp = __instance.CurrentHealth;
                }
                catch
                {
                    _cliReportPrevHp = -1f;
                }
            }

            return true;
        }

        // 其它情况放行（包括 AI→AI、AI→障碍物、远端玩家→AI 等）
        return true;
    }

    private static void Postfix(Health __instance)
    {
        if (!_cliReport) return;
        _cliReport = false;

        var mod = ModBehaviourF.Instance;
        if (mod == null || mod.IsServer) return;

        var aiId = _cliReportAiId;
        if (aiId == 0) return;

        float max = 0f, cur = 0f;
        try
        {
            max = __instance.MaxHealth;
        }
        catch
        {
        }

        try
        {
            cur = __instance.CurrentHealth;
        }
        catch
        {
        }

        if (_cliReportPrevHp > 0f && Mathf.Abs(_cliReportPrevHp - cur) < 0.001f) return;

        COOPManager.AIHealth.Client_ReportAiHealth(aiId, max, cur);
    }
}

[HarmonyPatch(typeof(Health), "Hurt", typeof(DamageInfo))]
internal static class Patch_CoopPlayer_Health_Hurt
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Health __instance, ref DamageInfo damageInfo)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;

        // 【优化】使用缓存判定 Main
        if (!mod.IsServer)
        {
            if (HealthCache.IsMain(__instance)) return true;
        }

        // 【优化】使用缓存判定 Proxy
        var isProxy = HealthCache.IsProxy(__instance);

        if (mod.IsServer && isProxy)
        {
            // 【优化】使用缓存的 Owner 查找
            var owner = HealthCache.GetOwner(__instance);
            if (owner != null)
                try
                {
                    HealthM.Instance.Server_ForwardHurtToOwner(owner, damageInfo);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[HP] forward to owner failed: " + e);
                }

            return false;
        }

        if (!mod.IsServer && isProxy) return false;
        return true;
    }
}