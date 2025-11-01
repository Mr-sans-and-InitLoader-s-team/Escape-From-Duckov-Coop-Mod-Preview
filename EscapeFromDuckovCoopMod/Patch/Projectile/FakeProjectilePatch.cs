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

using Duckov.Buffs;
using Duckov.Utilities;
using HarmonyLib;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(Projectile), "UpdateMoveAndCheck")]
internal static class Patch_Projectile_UpdateMoveAndCheck_Fake
{
    private static void Prefix(Projectile __instance)
    {
        FakeProjectileRegistry.BeginFrame(__instance);
    }

    private static void Postfix(Projectile __instance)
    {
        FakeProjectileRegistry.EndFrame(__instance);
    }
}

[HarmonyPatch(typeof(Projectile), "Release")]
internal static class Patch_Projectile_Release_Fake
{
    private static void Postfix(Projectile __instance)
    {
        FakeProjectileRegistry.Unregister(__instance);
    }
}

[HarmonyPatch(typeof(DamageReceiver), nameof(DamageReceiver.AddBuff), typeof(Buff), typeof(CharacterMainControl))]
internal static class Patch_DamageReceiver_AddBuff_Fake
{
    private static bool Prefix(ref bool __result, Buff buffPfb)
    {
        if (buffPfb == GameplayDataSettings.Buffs.Pain && FakeProjectileRegistry.IsCurrentFake)
        {
            __result = false;
            return false;
        }

        return true;
    }
}
