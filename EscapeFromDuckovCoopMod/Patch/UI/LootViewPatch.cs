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

using HarmonyLib;
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod.Patch.UI;

[HarmonyPatch(typeof(Duckov.UI.LootView), "OnStartLoot")]
internal static class LootViewPatch
{
    private static void Postfix(InteractableLootbox lootbox)
    {
        var service = NetService.Instance;
        var lootNet = COOPManager.LootNet;

        if (lootbox == null || service == null || service.IsServer || !service.networkStarted || lootNet == null)
            return;

        var inv = lootbox.Inventory;
        if (!LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv))
            return;

        lootNet.Client_SetLootActive(inv, true);
        lootNet.Client_RequestLootState(inv);
    }
}

[HarmonyPatch(typeof(Duckov.UI.LootView), "OnClose")]
internal static class LootViewClosePatch
{
    private static void Postfix(Duckov.UI.LootView __instance)
    {
        var service = NetService.Instance;
        var lootNet = COOPManager.LootNet;

        if (__instance == null || service == null || service.IsServer || lootNet == null)
            return;

        var inv = __instance.TargetInventory;
        if (inv == null) return;

        lootNet.Client_SetLootActive(inv, false);
    }
}
