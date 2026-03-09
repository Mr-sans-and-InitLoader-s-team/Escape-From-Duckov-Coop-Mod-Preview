//// Escape-From-Duckov-Coop-Mod-Preview
//// Copyright (C) 2025  Mr.sans and InitLoader's team
////
//// This program is not a free software.
//// It's distributed under a license based on AGPL-3.0,
//// with strict additional restrictions:
////  YOU MUST NOT use this software for commercial purposes.
////  YOU MUST NOT use this software to run a headless game server.
////  YOU MUST include a conspicuous notice of attribution to
////  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
////
//// This program is distributed in the hope that it will be useful,
//// but WITHOUT ANY WARRANTY; without even the implied warranty of
//// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//// GNU Affero General Public License for more details.

using Duckov.Scenes;
using Duckov.Utilities;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Patch.Loot;

// 让 AI 死亡掉落的战利品箱通过服务器广播给客户端，客户端再按需请求快照
[HarmonyPatch(typeof(InteractableLootbox), nameof(InteractableLootbox.CreateFromItem),
    typeof(Item), typeof(Vector3), typeof(Quaternion), typeof(bool), typeof(InteractableLootbox), typeof(bool))]
internal static class DeadLootSpawnPatch
{
    private static bool Prefix(ref InteractableLootbox __result, InteractableLootbox prefab)
    {
        if (LevelManager.Instance == null || MultiSceneCore.Instance == null) return true;
        var service = NetService.Instance;
        var tombPrefab = GameplayDataSettings.Prefabs?.LootBoxPrefab_Tomb;
        var isTomb = tombPrefab && prefab && prefab.gameObject == tombPrefab.gameObject;
        // 客户端不要本地生成尸体箱，否则会和主机广播的箱子重复
        if (!isTomb && service != null && !service.IsServer && service.networkStarted)
        {
            __result = null;
            return false;
        }

        return true;
    }

    private static void Postfix(InteractableLootbox __result)
    {
        if (__result == null)
            return;

        if (LevelManager.Instance == null || MultiSceneCore.Instance == null) return;

        var service = NetService.Instance;
        if (service == null || !service.IsServer || !service.networkStarted)
            return;

        DeadLootBox.Instance?.Server_OnDeadLootboxSpawned(__result, DeadLootSpawnContext.InOnDead);
    }
}
