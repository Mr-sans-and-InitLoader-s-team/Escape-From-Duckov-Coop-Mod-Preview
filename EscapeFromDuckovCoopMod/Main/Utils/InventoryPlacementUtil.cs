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
using HarmonyLib;
using System.Reflection;
using System.Runtime.CompilerServices;
using Object = UnityEngine.Object;
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

internal static class InventoryPlacementUtil
{
    private static readonly string[] _inventoryRefNames =
    {
        "InInventory", "inventory", "_inventory", "m_inventory"
    };

    private static readonly string[] _slotIndexNames =
    {
        "InventoryIndex", "InventoryPosition", "inventoryIndex",
        "inventoryPosition", "_inventoryIndex", "_inventoryPosition"
    };

    private static readonly ConditionalWeakTable<Type, MemberInfo> _inventoryMemberCache = new();
    private static readonly ConditionalWeakTable<Type, MemberInfo> _slotIndexMemberCache = new();
    private static readonly ConditionalWeakTable<Type, MethodInfo> _notifyAddedCache = new();

    public static bool TryPlaceItemExact(Inventory inv, Item item, int position)
    {
        if (!inv || !item) return false;
        if (position < 0) return false;

        var content = inv.Content;
        if (content == null) return false;

        var capacity = inv.Capacity;
        if (capacity <= 0) capacity = content.Count;
        if (position >= capacity) return false;

        while (content.Count <= position)
            content.Add(null);

        var existing = content[position];
        if (existing && !ReferenceEquals(existing, item))
        {
            try
            {
                existing.Detach();
            }
            catch
            {
            }

            try
            {
                Object.Destroy(existing.gameObject);
            }
            catch
            {
            }
        }

        content[position] = item;

        TrySetInventoryReference(item, inv);
        TrySetSlotIndex(item, position);
        TryNotifyAdded(item, inv);

        try
        {
            if (item.transform)
                item.transform.SetParent(inv.transform, false);
        }
        catch
        {
        }

        return true;
    }

    private static void TrySetInventoryReference(Item item, Inventory inv)
    {
        var type = item.GetType();
        if (!_inventoryMemberCache.TryGetValue(type, out var member))
        {
            member = ResolveMember(type, _inventoryRefNames);
            if (member != null)
                _inventoryMemberCache.Add(type, member);
        }

        try
        {
            switch (member)
            {
                case PropertyInfo pi when pi.CanWrite:
                    pi.SetValue(item, inv);
                    break;
                case FieldInfo fi:
                    fi.SetValue(item, inv);
                    break;
            }
        }
        catch
        {
        }
    }

    private static void TrySetSlotIndex(Item item, int position)
    {
        var type = item.GetType();
        if (!_slotIndexMemberCache.TryGetValue(type, out var member))
        {
            member = ResolveMember(type, _slotIndexNames);
            if (member != null)
                _slotIndexMemberCache.Add(type, member);
        }

        try
        {
            switch (member)
            {
                case PropertyInfo pi when pi.CanWrite:
                    pi.SetValue(item, position);
                    break;
                case FieldInfo fi:
                    fi.SetValue(item, position);
                    break;
            }
        }
        catch
        {
        }
    }

    private static void TryNotifyAdded(Item item, Inventory inv)
    {
        var type = item.GetType();
        if (!_notifyAddedCache.TryGetValue(type, out var method))
        {
            method = AccessTools.Method(type, "NotifyAddedToInventory", new[] { typeof(Inventory) });
            if (method != null)
                _notifyAddedCache.Add(type, method);
        }

        if (method == null) return;

        try
        {
            method.Invoke(item, new object[] { inv });
        }
        catch
        {
        }
    }

    private static MemberInfo ResolveMember(Type type, string[] names)
    {
        foreach (var name in names)
        {
            var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite)
                return prop;

            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field;
        }

        return null;
    }
}
