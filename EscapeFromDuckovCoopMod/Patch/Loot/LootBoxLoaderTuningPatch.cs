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
using System.Collections;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Patch.Loot;

[HarmonyPatch(typeof(LootBoxLoader))]
internal static class LootBoxLoaderTuningPatch
{
    [HarmonyPrefix]
    [HarmonyPatch("RandomActive")]
    private static void AdjustActivationChance(ref float ___activeChance)
    {
        if (!LootSettingsRuntime.ShouldApplyTuning) return;
        if (LevelManager.Instance == null || MultiSceneCore.Instance == null) return;
        ___activeChance = LootSettingsRuntime.AdjustSpawnChance(___activeChance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(LootBoxLoader.Setup))]
    private static void AdjustRandomItemCount(LootBoxLoader __instance, ref Vector2Int ___randomCount)
    {
        if (!LootSettingsRuntime.ShouldApplyTuning) return;
        if (LevelManager.Instance == null || MultiSceneCore.Instance == null) return;
        var tuned = LootSettingsRuntime.AdjustRandomCount(___randomCount);
        ___randomCount = ClampToInventory(__instance, tuned);
    }

    private static Vector2Int ClampToInventory(LootBoxLoader loader, Vector2Int requested)
    {
        var capacity = Math.Max(1, Traverse.Create(loader).Field<int>("inventorySize").Value);

        try
        {
            var inventory = Traverse.Create(loader).Property("Inventory")?.GetValue() as object;
            if (inventory != null)
            {
                var sizeProp = AccessTools.Property(inventory.GetType(), "Size") ?? AccessTools.Property(inventory.GetType(), "Capacity");
                if (sizeProp != null)
                {
                    capacity = Math.Max(capacity, Convert.ToInt32(sizeProp.GetValue(inventory)));
                }
            }
        }
        catch
        {
        }

        var min = Mathf.Clamp(requested.x, 1, capacity);
        var max = Mathf.Clamp(requested.y, min, capacity);
        return new Vector2Int(min, max);
    }
}

[HarmonyPatch]
internal static class LootBoxRandomPoolPatch
{
    private static System.Reflection.MethodBase TargetMethod()
    {
        var entryType = AccessTools.Inner(typeof(LootBoxLoader), "Entry");
        var generic = typeof(RandomContainer<>).MakeGenericType(entryType);
        return AccessTools.Method(generic, nameof(RandomContainer<int>.GetRandom), new[] { typeof(float) });
    }

    private static bool Prefix(object __instance, float lowPercent, ref object __result)
    {
        if (!LootSettingsRuntime.ShouldApplyTuning)
            return true;

        if (LevelManager.Instance == null || MultiSceneCore.Instance == null) return true;

        var entriesField = __instance.GetType().GetField("entries");
        var list = entriesField?.GetValue(__instance) as IList;
        if (list == null || list.Count < 1)
            return true;

        var entryType = list[0].GetType();
        var valueField = entryType.GetField("value");
        var weightField = entryType.GetField("weight");
        if (valueField == null || weightField == null)
            return true;

        var total = 0f;
        foreach (var entry in list)
        {
            var weight = Convert.ToSingle(weightField.GetValue(entry));
            var value = valueField.GetValue(entry);
            total += LootSettingsRuntime.AdjustWeight(value, weight);
        }

        var roll = UnityEngine.Random.Range(total * lowPercent, total);
        var cursor = 0f;
        var lastEntry = list[list.Count - 1];
        foreach (var entry in list)
        {
            var weight = Convert.ToSingle(weightField.GetValue(entry));
            var value = valueField.GetValue(entry);
            cursor += LootSettingsRuntime.AdjustWeight(value, weight);
            if (cursor >= roll)
            {
                __result = value;
                return false;
            }
        }

        __result = valueField.GetValue(lastEntry);
        return false;
    }
}
