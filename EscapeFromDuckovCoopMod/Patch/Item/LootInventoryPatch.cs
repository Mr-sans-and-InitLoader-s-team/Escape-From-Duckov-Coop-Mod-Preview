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
using static EscapeFromDuckovCoopMod.LootNet;

namespace EscapeFromDuckovCoopMod;

internal static class LootInventoryPatch
{
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddAt))]
    [HarmonyPrefix]
    private static void LootAddAtPrefix(ref int __state)
    {
        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return;
        }
        __state = LootUiGuards.InLootAddAtDepth;
        LootUiGuards.InLootAddAtDepth++;
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddAt))]
    [HarmonyPostfix]
    private static void LootAddAtPostfix(Inventory __instance, int atPosition, Item item, bool __result, int __state)
    {
        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return;
        }
        LootUiGuards.InLootAddAtDepth = __state;
        if (!__result || !item) return;
        NotifySlotChanged(__instance, atPosition, item, true);
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveAt))]
    [HarmonyPostfix]
    private static void LootRemoveAtPostfix(Inventory __instance, int position, bool __result)
    {
        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return;
        }
        if (!__result) return;
        NotifySlotChanged(__instance, position, null, false);
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem))]
    [HarmonyPostfix]
    private static void LootAddItemPostfix(Inventory __instance, Item item, bool __result)
    {
        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return;
        }
        if (!__result || item == null) return;
        var pos = __instance.GetLastItemPosition();
        if (pos >= 0)
            NotifySlotChanged(__instance, pos, item, true);
    }

    internal static void NotifySlotChanged(Inventory inv, int slot, Item item, bool isAdd)
    {
        if (inv == null) return;
        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return;
        }

        var service = NetService.Instance;
        if (service == null || !service.networkStarted) return;

        var lootNet = COOPManager.LootNet;
        if (lootNet == null || lootNet.ApplyingLootState) return;

        if (!LootboxDetectUtil.TryResolveLootOwner(inv, out var lootInv, out var ownerSlot, out var master)) return;
        if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return;

        var targetSlot = ownerSlot >= 0 ? ownerSlot : slot;

        if (service.IsServer)
        {
            if (isAdd && item)
                lootNet.Server_MarkPlayerPlaced(item);
            lootNet.Server_BroadcastHostSlotChange(lootInv, targetSlot);
        }
        else
        {
            if (!lootNet.Client_ShouldSendLootChange(lootInv)) return;

            if (master != null && ownerSlot >= 0 && !ReferenceEquals(inv, lootInv))
            {
                lootNet.Client_RequestLootSlotSnapshot(lootInv, master);
            }
            else
            {
                lootNet.Server_OnInventorySlotChanged(lootInv, targetSlot);
            }
        }
    }
}

[HarmonyPatch(typeof(global::ItemSetting_Gun), nameof(global::ItemSetting_Gun.TakeOutAllBullets))]
internal static class GunUnloadPatch
{
    private static void Postfix(global::ItemSetting_Gun __instance)
    {
        var gun = __instance == null ? null : __instance.Item;
        if (gun == null) return;
        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return;
        }

        var service = NetService.Instance;
        var lootNet = COOPManager.LootNet;
        if (service == null || lootNet == null || !service.networkStarted) return;

        var inv = gun.InInventory;
        if (inv == null) return;

        if (!LootboxDetectUtil.TryResolveLootOwner(inv, out var lootInv, out var ownerSlot, out var master)) return;
        if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return;

        var slot = ownerSlot >= 0 ? ownerSlot : lootInv.GetIndex(master ?? gun);
        if (slot < 0) return;

        if (service.IsServer)
        {
            lootNet.Server_BroadcastHostSlotChange(lootInv, slot);
        }
        else
        {
            if (!lootNet.Client_ShouldSendLootChange(lootInv)) return;
            lootNet.Client_RequestLootSlotSnapshot(lootInv, master ?? gun);
        }
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddAt))]
internal static class ClientLootAddAtPatch
{
    private static bool Prefix(Inventory __instance, Item item, int atPosition)
    {
        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return true;
        }

        var service = NetService.Instance;
        var lootNet = COOPManager.LootNet;
        if (service == null || !service.networkStarted || lootNet == null)
            return true;

        if (lootNet.ApplyingLootState || LootUiGuards.InLootAddAt) return true;
        if (!LootboxDetectUtil.TryResolveLootOwner(__instance, out var lootInv, out var ownerSlot, out var master))
            return true;
        if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return true;
        if (!lootNet.Client_ShouldSendLootChange(lootInv)) return true;

        if (master != null && ownerSlot >= 0 && !ReferenceEquals(__instance, lootInv))
        {
            lootNet.Client_RequestLootSlotSnapshot(lootInv, master);
            return true;
        }

        lootNet.Client_SendLootPutRequest(lootInv, item, atPosition);
        // Let the local UI continue so drag/drop keeps working; the host remains
        // authoritative and will correct the slot with its delta if this diverges.
        return true;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem))]
internal static class ClientLootAddItemPatch
{
    private static bool Prefix(Inventory __instance, Item item)
    {
        var service = NetService.Instance;
        var lootNet = COOPManager.LootNet;
        if (service == null || !service.networkStarted || lootNet == null)
            return true;

        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return true;
        }

        if (lootNet.ApplyingLootState || LootUiGuards.InLootAddAt) return true;
        if (!LootboxDetectUtil.TryResolveLootOwner(__instance, out var lootInv, out var ownerSlot, out var master))
            return true;
        if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return true;
        if (!lootNet.Client_ShouldSendLootChange(lootInv)) return true;

        if (master != null && ownerSlot >= 0 && !ReferenceEquals(__instance, lootInv))
        {
            lootNet.Client_RequestLootSlotSnapshot(lootInv, master);
            return true;
        }

        lootNet.Client_SendLootPutRequest(lootInv, item, -1);
        // Allow the local add path to proceed so the UI can hand off the item; the
        // server delta will reconcile the authoritative slot contents.
        return true;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveAt))]
internal static class ClientLootRemoveAtPatch
{
    private static bool Prefix(Inventory __instance, int position)
    {
        var service = NetService.Instance;
        var lootNet = COOPManager.LootNet;
        if (service == null || !service.networkStarted || lootNet == null)
            return true;

        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return true;
        }

        if (lootNet.ApplyingLootState) return true;
        if (!LootboxDetectUtil.TryResolveLootOwner(__instance, out var lootInv, out var ownerSlot, out var master))
            return true;
        if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return true;
        if (!lootNet.Client_ShouldSendLootChange(lootInv)) return true;

        if (master != null && ownerSlot >= 0 && !ReferenceEquals(__instance, lootInv))
        {
            lootNet.Client_RequestLootSlotSnapshot(lootInv, master);
            return true;
        }

        lootNet.Client_SendLootTakeRequest(lootInv, position);
        // Let the local removal continue so the dragged item is detached; the host
        // delta will confirm or correct the slot state.
        return true;
    }
}

[HarmonyPatch(typeof(Item), nameof(Item.StackCount), MethodType.Setter)]
internal static class LootItemStackPatch
{
    private static void Postfix(Item __instance)
    {
        var service = NetService.Instance;
        if (service == null || !service.networkStarted) return;

        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return;
        }

        var inv = __instance ? __instance.InInventory : null;
        if (inv == null) return;

        var lootNet = COOPManager.LootNet;
        if (lootNet == null || lootNet.ApplyingLootState || lootNet._serverApplyingLoot) return;

        if (!LootboxDetectUtil.TryResolveLootOwner(inv, out var lootInv, out var ownerSlot, out var master))
            return;
        if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return;

        var slot = ownerSlot >= 0 ? ownerSlot : lootInv.GetIndex(master ?? __instance);
        if (slot < 0) return;

        if (service.IsServer)
        {
            LootInventoryPatch.NotifySlotChanged(lootInv, slot, master ?? __instance, true);
        }
        else
        {
            if (!lootNet.Client_ShouldSendLootChange(lootInv)) return;

            if (master != null && ownerSlot >= 0 && !ReferenceEquals(inv, lootInv))
            {
                lootNet.Client_RequestLootSlotSnapshot(lootInv, master);
            }
            else
            {
                lootNet.Client_SendLootSplitRequest(lootInv, slot, __instance.StackCount);
            }
        }
    }
}

[HarmonyPatch(typeof(ItemExtensions), nameof(ItemExtensions.Drop), typeof(Item), typeof(Vector3), typeof(bool), typeof(Vector3), typeof(float))]
internal static class ItemDropPatch
{
    private static bool Prefix(Item item, Vector3 pos, bool createRigidbody, Vector3 dropDirection, float randomAngle, ref DuckovItemAgent __result)
    {
        var service = NetService.Instance;
        if (service == null || !service.networkStarted)
            return true;

        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return true;
        }

        if (ItemNet.InNetworkDrop)
            return true;

        if (item == null) return false;

        if (service.IsServer)
        {
            // Let the server perform the normal drop path; we'll register the result in a postfix.
            return true;
        }

        var owningInventory = item.InInventory ?? item.Slots?.Master?.InInventory;
        var hasOwnership = owningInventory != null || item.Slots != null;
        if (!hasOwnership)
            return true;

        COOPManager.ItemNet?.Client_RequestDrop(item, pos, createRigidbody, dropDirection, randomAngle, true);
        __result = null;
        return false;
    }

    private static void Postfix(Item item, Vector3 pos, bool createRigidbody, Vector3 dropDirection, float randomAngle, DuckovItemAgent __result)
    {
        var service = NetService.Instance;
        if (service == null || !service.IsServer || !service.networkStarted || ItemNet.InNetworkDrop)
            return;

        if (item == null) return;

        COOPManager.ItemNet?.Server_RegisterLocalDrop(item, __result, pos, createRigidbody, dropDirection, randomAngle);
    }
}

[HarmonyPatch(typeof(CharacterMainControl), nameof(CharacterMainControl.PickupItem))]
internal static class CharacterPickupPatch
{
    private static void Postfix(CharacterMainControl __instance, Item item, bool __result)
    {
        if (!__result || item == null) return;

        var service = NetService.Instance;
        if (service == null || !service.networkStarted) return;

        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return;
        }

        var itemNet = COOPManager.ItemNet;
        if (itemNet == null) return;

        if (service.IsServer)
        {
            itemNet.Server_HandleLocalPickup(item);
        }
        else
        {
            itemNet.Client_RequestPickup(item);
        }
    }
}

[HarmonyPatch]
internal static class LootSlotWeaponSnapshotHelper
{
    public static void TrySendLocalWeaponSnapshot(Item master)
    {
        try
        {
            var lpm = LocalPlayerManager.Instance;
            var service = NetService.Instance;
            if (lpm == null || service == null || !service.networkStarted || master == null)
                return;

            var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
            if (serverLoading)
            {
                return;
            }

            var main = CharacterMainControl.Main;
            if (main == null) return;

            var gun = main.GetGun();
            var melee = main.GetMeleeWeapon();
            var slotHash = 0;

            if (gun != null && gun.Item == master)
                slotHash = (int)HandheldSocketTypes.normalHandheld;
            else if (melee != null && melee.Item == master)
                slotHash = (int)HandheldSocketTypes.meleeWeapon;

            if (slotHash == 0) return;

            lpm.SendWeaponAttachmentSnapshot(master, slotHash);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(Slot), nameof(Slot.Plug))]
internal static class ClientLootSlotPlugPatch
{
    private static void Postfix(Slot __instance, Item otherItem, bool __result)
    {
        var service = NetService.Instance;
        var lootNet = COOPManager.LootNet;
        if (service == null || lootNet == null || otherItem == null || !service.networkStarted)
            return;

        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return;
        }

        if (!__result) return;
        if (lootNet.ApplyingLootState || LootUiGuards.InLootAddAt) return;

        var master = __instance?.Master;
        if (master != null)
            LootSlotWeaponSnapshotHelper.TrySendLocalWeaponSnapshot(master);

        var inv = master ? master.InInventory : null;
        if (inv == null || LootboxDetectUtil.IsPrivateInventory(inv) || !LootboxDetectUtil.IsLootboxInventory(inv))
            return;

        if (service.IsServer) return;
        if (!lootNet.Client_ShouldSendLootChange(inv)) return;

        lootNet.Client_RequestLootSlotSnapshot(inv, master);
    }
}

[HarmonyPatch(typeof(Slot), nameof(Slot.Unplug))]
internal static class ClientLootSlotUnplugPatch
{
    private static void Postfix(Slot __instance, Item __result)
    {
        var service = NetService.Instance;
        var lootNet = COOPManager.LootNet;
        if (service == null || lootNet == null || !service.networkStarted)
            return;

        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return;
        }

        if (lootNet.ApplyingLootState || LootUiGuards.InLootAddAt) return;

        var master = __instance?.Master;
        if (master != null)
            LootSlotWeaponSnapshotHelper.TrySendLocalWeaponSnapshot(master);

        var inv = master ? master.InInventory : null;
        if (inv == null || LootboxDetectUtil.IsPrivateInventory(inv) || !LootboxDetectUtil.IsLootboxInventory(inv))
            return;

        if (service.IsServer) return;
        if (!lootNet.Client_ShouldSendLootChange(inv)) return;

        lootNet.Client_RequestLootSlotSnapshot(inv, master);
    }
}

// 监听玩家手中武器的配件插拔，立即推送武器快照（不受容器/私有库存限制）
[HarmonyPatch(typeof(Slot), nameof(Slot.Plug))]
internal static class PlayerWeaponSlotPlugPatch
{
    private static void Postfix(Slot __instance, Item otherItem, bool __result)
    {
        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return;
        }

        if (!__result || otherItem == null) return;
        TrySendHeldWeaponSnapshot(__instance);
    }

    private static void TrySendHeldWeaponSnapshot(Slot slot)
    {
        var service = NetService.Instance;
        var lpm = LocalPlayerManager.Instance;
        var main = CharacterMainControl.Main;
        if (service == null || lpm == null || !service.networkStarted || main == null) return;

        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return;
        }

        var master = slot?.Master;
        if (master == null) return;

        var gun = main.GetGun();
        if (gun != null && gun.Item == master)
        {
            lpm.SendWeaponAttachmentSnapshot(master, (int)HandheldSocketTypes.normalHandheld);
            return;
        }

        var melee = main.GetMeleeWeapon();
        if (melee != null && melee.Item == master)
        {
            lpm.SendWeaponAttachmentSnapshot(master, (int)HandheldSocketTypes.meleeWeapon);
        }
    }
}

[HarmonyPatch(typeof(Slot), nameof(Slot.Unplug))]
internal static class PlayerWeaponSlotUnplugPatch
{
    private static void Postfix(Slot __instance, Item __result)
    {
        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return;
        }
        TrySendHeldWeaponSnapshot(__instance);
    }

    private static void TrySendHeldWeaponSnapshot(Slot slot)
    {
        var service = NetService.Instance;
        var lpm = LocalPlayerManager.Instance;
        var main = CharacterMainControl.Main;
        if (service == null || lpm == null || !service.networkStarted || main == null) return;

        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return;
        }

        var master = slot?.Master;
        if (master == null) return;

        var gun = main.GetGun();
        if (gun != null && gun.Item == master)
        {
            lpm.SendWeaponAttachmentSnapshot(master, (int)HandheldSocketTypes.normalHandheld);
            return;
        }

        var melee = main.GetMeleeWeapon();
        if (melee != null && melee.Item == master)
        {
            lpm.SendWeaponAttachmentSnapshot(master, (int)HandheldSocketTypes.meleeWeapon);
        }
    }
}
