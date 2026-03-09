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
using HarmonyLib;
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod.Patch.Loot;

// Preserve per-client search progress: don't mark containers inspected when they were closed mid-search.
[HarmonyPatch(typeof(InteractableLootbox), "OnInteractStop")]
internal static class LootboxInteractStopPatch
{
    private static void Postfix(InteractableLootbox __instance)
    {

        if (LevelManager.Instance == null || MultiSceneCore.Instance == null) return;

        var inv = __instance == null ? null : __instance.Inventory;
        if (inv == null) return;

        var needInspection = LootSearchWorldGate.GetNeedInspection(inv);
        var fullyInspected = !needInspection;

        if (needInspection)
        {
            fullyInspected = true;
            try
            {
                foreach (var item in inv)
                {
                    if (!item) continue;
                    if (!item.Inspected)
                    {
                        fullyInspected = false;
                        break;
                    }
                }
            }
            catch
            {
                fullyInspected = false;
            }
        }

        try
        {
            inv.NeedInspection = !fullyInspected;
        }
        catch
        {
        }

        try
        {
            inv.hasBeenInspectedInLootBox = fullyInspected;
        }
        catch
        {
        }
    }
}
