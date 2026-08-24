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
using ItemStatsSystem.Items;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Patch.Character;

internal static class CharacterEquipmentAgentGuard
{
    internal static bool IsCharacterEquipmentSlot(Slot slot)
    {
        if (slot == null || slot.Master == null || !slot.Master.IsCharacter)
            return false;

        return slot.Key == "Armor" ||
               slot.Key == "Helmat" ||
               slot.Key == "FaceMask" ||
               slot.Key == "Backpack" ||
               slot.Key == "Headset";
    }

    internal static bool HasEquipmentVisual(Item item)
    {
        if (item == null)
            return false;

        try
        {
            if (item.AgentUtilities?.GetPrefab(CharacterEquipmentController.equipmentModelHash) != null)
                return true;
        }
        catch
        {
        }

        try
        {
            return item.ItemGraphic;
        }
        catch
        {
            return false;
        }
    }

    internal static Slot GetTryPlugTargetSlot(Item main, Item part, bool emptyOnly)
    {
        if (main == null || part == null || main.Slots == null)
            return null;

        Slot first = null;
        foreach (var slot in main.Slots)
        {
            if (slot == null)
                continue;

            bool canPlug;
            try
            {
                canPlug = slot.CanPlug(part);
            }
            catch
            {
                continue;
            }

            if (!canPlug)
                continue;

            if (part.PluggedIntoSlot == slot)
                return null;

            first ??= slot;
            if (slot.Content == null)
                return slot;
        }

        return emptyOnly ? null : first;
    }
}

[HarmonyPatch(typeof(ItemUtilities), nameof(ItemUtilities.TryPlug))]
internal static class InvalidCharacterEquipmentTryPlugPatch
{
    private static bool Prefix(Item __0, Item __1, bool __2, ref bool __result)
    {
        try
        {
            var targetSlot = CharacterEquipmentAgentGuard.GetTryPlugTargetSlot(__0, __1, __2);
            if (!CharacterEquipmentAgentGuard.IsCharacterEquipmentSlot(targetSlot))
                return true;

            if (CharacterEquipmentAgentGuard.HasEquipmentVisual(__1))
                return true;

            __result = false;
            return false;
        }
        catch
        {
            return true;
        }
    }
}

[HarmonyPatch(typeof(CharacterEquipmentController), "ChangeEquipmentModel")]
internal static class InvalidCharacterEquipmentModelPatch
{
    private static bool Prefix(Slot slot, Transform socket)
    {
        try
        {
            if (!CharacterEquipmentAgentGuard.IsCharacterEquipmentSlot(slot))
                return true;

            return CharacterEquipmentAgentGuard.HasEquipmentVisual(slot.Content);
        }
        catch
        {
            return true;
        }
    }
}
