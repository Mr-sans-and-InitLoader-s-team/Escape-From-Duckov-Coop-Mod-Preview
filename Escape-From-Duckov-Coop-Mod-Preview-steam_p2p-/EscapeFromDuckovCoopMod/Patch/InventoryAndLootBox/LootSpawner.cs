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

using Duckov.Utilities;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(LootSpawner), "Start")]
internal static class Patch_LootSpawner_Start_PrimeNeedInspect
{
    private static void Postfix(LootSpawner __instance)
    {
        var lb = __instance.GetComponent<InteractableLootbox>();
        WorldLootPrime.PrimeIfClient(lb);
    }
}

[HarmonyPatch(typeof(LootSpawner), "Setup")]
internal static class Patch_LootSpawner_Setup_PrimeNeedInspect
{
    private static void Postfix(LootSpawner __instance)
    {
        var lb = __instance.GetComponent<InteractableLootbox>();
        WorldLootPrime.PrimeIfClient(lb);
    }
}

[HarmonyPatch(typeof(LootBoxLoader), "Awake")]
internal static class Patch_LootBoxLoader_Awake_PrimeNeedInspect
{
    private static void Postfix(LootBoxLoader __instance)
    {
        var lb = __instance.GetComponent<InteractableLootbox>();
        WorldLootPrime.PrimeIfClient(lb);
    }
}