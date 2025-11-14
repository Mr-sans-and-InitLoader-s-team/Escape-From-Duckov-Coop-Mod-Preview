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

using NodeCanvas.Tasks.Conditions;

namespace EscapeFromDuckovCoopMod;

/// <summary>
/// 修复 CheckHurt.OnCheck() 中的空引用异常
/// 问题：damageInfo.fromCharacter 可能为 null（环境伤害、已销毁的角色等）
/// 解决：添加空值检查，避免访问 null.mainDamageReceiver
/// </summary>
[HarmonyPatch(typeof(CheckHurt), "OnCheck")]
internal static class Patch_CheckHurt_OnCheck_NullCheck
{
    private static bool Prefix(CheckHurt __instance, ref bool __result)
    {
        // 基础检查
        if (__instance.agent == null || __instance.cacheFromCharacterDmgReceiver == null)
        {
            __result = false;
            return false; // 跳过原方法
        }

        DamageInfo damageInfo = default(DamageInfo);
        if (!__instance.agent.IsHurt(__instance.hurtTimeThreshold, __instance.damageThreshold, ref damageInfo))
        {
            __result = false;
            return false; // 跳过原方法
        }

        // 🔍 关键修复：检查 fromCharacter 是否为 null
        if (damageInfo.fromCharacter == null)
        {
            // 伤害来自环境或已销毁的角色，无法缓存 mainDamageReceiver
            // 但仍然认为受伤条件满足
            __result = true;
            return false; // 跳过原方法
        }

        // 正常情况：缓存伤害来源的 mainDamageReceiver
        __instance.cacheFromCharacterDmgReceiver.value = damageInfo.fromCharacter.mainDamageReceiver;
        __result = true;
        return false; // 跳过原方法
    }
}
