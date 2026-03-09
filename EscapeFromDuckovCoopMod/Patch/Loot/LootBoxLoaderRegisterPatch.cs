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
using Duckov.Scenes;
using HarmonyLib;
using ItemStatsSystem;
using EscapeFromDuckovCoopMod;

namespace EscapeFromDuckovCoopMod.Patch.Loot;

// 把地图上的 Lootbox 在生成时登记进同步数据库，方便客户端用位置/实例号解析并拿到主机快照
[HarmonyPatch(typeof(LootBoxLoader), "Awake")]
internal static class LootBoxLoaderRegisterPatch
{
    private static void Postfix(LootBoxLoader __instance)
    {
        if (LevelManager.Instance == null || MultiSceneCore.Instance == null) return;
        LootBoxLoaderRegisterUtil.TryRegister(__instance);
    }
}

internal static class LootBoxLoaderRegisterUtil
{
    internal static void TryRegister(LootBoxLoader loader)
    {
        var lootbox = loader ? loader.GetComponent<InteractableLootbox>() : null;
        var inv = lootbox ? lootbox.Inventory : null;
        if (inv == null || LootboxDetectUtil.IsPrivateInventory(inv))
            return;

        if (LevelManager.Instance == null || MultiSceneCore.Instance == null) return;

        // 记录为世界容器，并把位置/实例信息放进数据库供客户端解析
        LootSearchWorldGate.EnsureWorldFlag(inv);
        CoopSyncDatabase.Loot.Register(lootbox, inv);

        var lootNet = COOPManager.LootNet;
        if (lootNet != null && !lootNet.IsServer &&
            CoopSyncDatabase.Loot.TryGetByInventory(inv, out var entry) && entry != null)
        {
            var key = entry.PositionKey;
            if (key != 0 && lootNet.Client_TryGetCachedVisibility(key, out var active))
            {
                if (ItemTool.InventoryHasQuestItem(inv))
                    return;

                var go = loader.gameObject;
                if (go && go.activeSelf != active)
                    go.SetActive(active);

                try
                {
                    var core = Duckov.Scenes.MultiSceneCore.Instance;
                    if (core?.inLevelData != null)
                        core.inLevelData[key] = active;
                }
                catch
                {
                }
            }
        }
    }

}
