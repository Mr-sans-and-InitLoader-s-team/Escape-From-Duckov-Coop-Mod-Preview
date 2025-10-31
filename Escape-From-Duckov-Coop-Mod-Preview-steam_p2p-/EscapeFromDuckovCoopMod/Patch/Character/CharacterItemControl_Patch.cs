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

namespace EscapeFromDuckovCoopMod;

//给 CharacterItemControl.PickupItem 打前后钩，围出一个方法域
[HarmonyPatch(typeof(CharacterItemControl), nameof(CharacterItemControl.PickupItem))]
internal static class Patch_CharacterItemControl_PickupItem
{
    private static void Prefix()
    {
        NetSilenceGuards.InPickupItem = true;
    }

    private static void Finalizer()
    {
        NetSilenceGuards.InPickupItem = false;
    }
}

[HarmonyPatch(typeof(CharacterAnimationControl_MagicBlend), "Update")]
internal static class Patch_MagicBlend_Update_ForRemote
{
    private static bool Prefix(CharacterAnimationControl_MagicBlend __instance)
    {
        // 远端实体：禁用本地“写Animator参数”的逻辑，避免覆盖网络同步
        if (__instance && __instance.GetComponentInParent<RemoteReplicaTag>() != null)
            return false;
        return true;
    }
}