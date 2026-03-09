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
using Duckov.Utilities;
using HarmonyLib;
using System;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Patch.Loot;

[HarmonyPatch(typeof(LootSpawner))]
internal static class LootSpawnerPatch
{
    [HarmonyPrefix]
    [HarmonyPatch("Start")]
    private static void AdjustSpawnChance(ref float ___spawnChance)
    {
        if (!LootSettingsRuntime.ShouldApplyTuning) return;
        if (LevelManager.Instance == null || MultiSceneCore.Instance == null) return;
        ___spawnChance = LootSettingsRuntime.AdjustSpawnChance(___spawnChance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(LootSpawner.Setup))]
    private static void AdjustRandomCount(LootSpawner __instance, ref Vector2Int ___randomCount)
    {
        if (!LootSettingsRuntime.ShouldApplyTuning) return;
        if (LevelManager.Instance == null || MultiSceneCore.Instance == null) return;
        var tuned = LootSettingsRuntime.AdjustRandomCount(___randomCount);
        ___randomCount = ClampToSpawnCapacity(__instance, tuned);
    }

    private static Vector2Int ClampToSpawnCapacity(LootSpawner spawner, Vector2Int requested)
    {
        var capacity = GetSpawnPointCapacity(spawner);
        if (capacity <= 0)
            return requested;

        var min = Mathf.Clamp(requested.x, 1, capacity);
        var max = Mathf.Clamp(requested.y, min, capacity);
        return new Vector2Int(max, max);
    }

    private static int GetSpawnPointCapacity(LootSpawner spawner)
    {
        try
        {
            var points = Traverse.Create(spawner).Field<Points>("points").Value ?? spawner.GetComponent<Points>();
            var childCount = points != null ? points.transform?.childCount ?? 0 : 0;
            return childCount;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Loot] Failed to read spawn point capacity: {e}");
            return 0;
        }
    }
}

[HarmonyPatch(typeof(RandomContainer<int>))]
internal static class RandomContainerQualityPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(RandomContainer<int>.GetRandom), typeof(float))]
    private static bool Prefix(RandomContainer<int> __instance, float lowPercent, ref int __result)
    {
        if (!LootSettingsRuntime.ShouldApplyTuning || __instance.entries.Count < 1) return true;

        var total = 0f;
        foreach (var entry in __instance.entries)
        {
            total += LootSettingsRuntime.AdjustWeight(entry.value, entry.weight);
        }

        var roll = UnityEngine.Random.Range(total * lowPercent, total);
        var cursor = 0f;
        foreach (var entry in __instance.entries)
        {
            cursor += LootSettingsRuntime.AdjustWeight(entry.value, entry.weight);
            if (cursor >= roll)
            {
                __result = entry.value;
                return false;
            }
        }

        __result = __instance.entries[^1].value;
        return false;
    }
}
