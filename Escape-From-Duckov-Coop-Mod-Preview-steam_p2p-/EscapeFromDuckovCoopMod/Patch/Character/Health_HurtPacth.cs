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
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(Health), "Hurt", typeof(DamageInfo))]
internal static class Patch_AIHealth_Hurt_HostAuthority
{
    [HarmonyPriority(Priority.High)]
    private static bool Prefix(Health __instance, ref DamageInfo damageInfo)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;
        if (mod.IsServer) return true; // 主机照常
        var isMain = false;
        try
        {
            isMain = __instance.IsMainCharacterHealth;
        }
        catch
        {
        }

        if (isMain) return true;

        if (__instance.gameObject.GetComponent<AutoRequestHealthBar>() != null) return false;

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

        var victimIsAI = victim &&
                         (victim.GetComponent<AICharacterController>() != null ||
                          victim.GetComponent<NetAiTag>() != null);
        if (!victimIsAI) return true;

        var attacker = damageInfo.fromCharacter;
        if (attacker == CharacterMainControl.Main)
            return true; // 本机玩家命中 AI：允许本地结算

        // —— 不处理 AI→AI ——
        var attackerIsAI = attacker &&
                           (attacker.GetComponent<AICharacterController>() != null ||
                            attacker.GetComponent<NetAiTag>() != null);
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

        var tag = cmc.GetComponent<NetAiTag>();
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

        if (__instance.gameObject.GetComponent<AutoRequestHealthBar>() != null) return false;

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

        // 仅客户端 + 仅本机玩家打到 AI 时，走“拦截→本地播特效→网络上报”
        if (!mod.IsServer && isAiVictim && fromLocalMain)
        {
            var tag = victimCmc ? victimCmc.GetComponent<NetAiTag>() : null;
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

        if (!mod.IsServer)
        {
            var isMain = false;
            try
            {
                isMain = __instance.IsMainCharacterHealth;
            }
            catch
            {
            }

            if (isMain) return true;
        }

        var isProxy = __instance.gameObject.GetComponent<AutoRequestHealthBar>() != null;

        if (mod.IsServer && isProxy)
        {
            var owner = HealthTool.Server_FindOwnerPeerByHealth(__instance);
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