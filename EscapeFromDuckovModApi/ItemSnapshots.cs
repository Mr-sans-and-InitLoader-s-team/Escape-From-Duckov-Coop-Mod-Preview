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

namespace EscapeFromDuckovCoopMod;

public struct ItemSnapshot
{
    public int TypeId;
    public int Stack;
    public bool HasDurability;
    public float Durability;
    public int InventoryCapacity;
    public ItemInventoryEntrySnapshot[] Inventory;
    public ItemSlotSnapshot[] Slots;
    public string[] CustomDataKeys;
    public string[] CustomDataValues;
}

public struct ItemInventoryEntrySnapshot
{
    public int Slot;
    public ItemSnapshot Item;
}

public struct ItemSlotSnapshot
{
    public string Key;
    public bool HasItem;
    public ItemSnapshot Item;
}
