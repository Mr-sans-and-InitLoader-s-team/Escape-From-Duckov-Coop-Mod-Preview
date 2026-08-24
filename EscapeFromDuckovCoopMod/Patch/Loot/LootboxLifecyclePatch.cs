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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Scenes;
using Duckov.Utilities;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Patch.Loot;

internal static class LootboxLifecycleGuard
{
    internal static bool IsLevelInventoryReady()
    {
        if (LevelManager.Instance == null)
            return false;

        try
        {
            return LevelManager.LootBoxInventories != null;
        }
        catch
        {
            return false;
        }
    }
}

[HarmonyPatch(typeof(InteractableLootbox), "Start")]
internal static class LootboxStartDelayPatch
{
    private static readonly MethodInfo StartMethod = AccessTools.Method(typeof(InteractableLootbox), "Start");
    private static readonly HashSet<int> Pending = new();
    private static readonly HashSet<int> Replaying = new();

    private static bool Prefix(InteractableLootbox __instance)
    {
        if (!__instance)
            return true;

        var id = __instance.GetInstanceID();
        if (Replaying.Remove(id))
            return true;

        if (LootboxLifecycleGuard.IsLevelInventoryReady())
            return true;

        if (Pending.Add(id))
            __instance.StartCoroutine(DelayedStart(__instance, id));

        return false;
    }

    private static IEnumerator DelayedStart(InteractableLootbox lootbox, int id)
    {
        while (lootbox && !LootboxLifecycleGuard.IsLevelInventoryReady())
            yield return null;

        Pending.Remove(id);
        if (!lootbox || !LootboxLifecycleGuard.IsLevelInventoryReady())
            yield break;

        Replaying.Add(id);
        try
        {
            StartMethod?.Invoke(lootbox, Array.Empty<object>());
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        finally
        {
            Replaying.Remove(id);
        }

        try
        {
            var loader = lootbox.GetComponent<LootBoxLoader>();
            if (loader)
                LootBoxLoaderRegisterUtil.TryRegister(loader);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(InteractableLootbox), "IsInteractable")]
internal static class LootboxIsInteractableGuardPatch
{
    private static bool Prefix(ref bool __result)
    {
        if (LootboxLifecycleGuard.IsLevelInventoryReady())
            return true;

        __result = false;
        return false;
    }
}
