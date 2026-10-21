using System.Reflection;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(PlayerStorage), nameof(PlayerStorage.TakeBufferItem))]
internal static class SharedHomeBufferTakePatch
{
    private static bool Prefix(int index, ref UniTask __result)
    {
        if (!SharedHome.Guest || SharedHome.Applying || !SharedHomeStorage.CanTransfer) return true;
        SharedHome.Request("storageBufferTake", new { index, expected = SharedHomeStorage.BufferKey(index) });
        __result = UniTask.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(PlayerStorage), nameof(PlayerStorage.Push))]
internal static class SharedHomeBufferPutPatch
{
    private static bool Prefix(Item item, bool toBufferDirectly)
    {
        if (!SharedHome.Guest || SharedHome.Applying || !SharedHomeLoadouts.Ready || !SharedHomeStorage.CanTransfer || !item || SharedHomeStorage.IsShared(item.InInventory)) return true;
        SharedHomeStorage.SendBuffer(item, toBufferDirectly);
        return false;
    }
}

[HarmonyPatch(typeof(Item), nameof(Item.Detach))]
internal static class SharedHomeHoldItemPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Item __instance) => !SharedHomeStorage.Hold(__instance.InInventory);
}

[HarmonyPatch(typeof(Item), nameof(Item.Split))]
internal static class SharedHomeSplitPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Item __instance, int count, ref UniTask<Item> __result)
    {
        if (!SharedHomeStorage.Hold(__instance.InInventory)) return true;
        __result = SharedHomeStorage.Split(__instance, count);
        return false;
    }
}

[HarmonyPatch]
internal static class SharedHomeNativeTakePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(ItemUtilities).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(m => m.Name == "SendToPlayerCharacter" || m.Name == "SendToPlayerCharacterInventory");
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Item item, ref bool __result)
    {
        if (!SharedHomeStorage.RequestTake(item)) return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddAt))]
internal static class SharedHomeInventoryTakePatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Inventory __instance, Item item, int atPosition, ref bool __result)
    {
        if (SharedHomeStorageOperations.Put(item, __instance, atPosition)) { __result = true; return false; }
        if (!SharedHomeStorage.RequestTake(item, __instance, atPosition)) return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(ItemUtilities), nameof(ItemUtilities.AddAndMerge))]
internal static class SharedHomeMergeTakePatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Inventory inventory, Item item, ref bool __result)
    {
        if (SharedHomeStorageOperations.Put(item, inventory)) { __result = true; return false; }
        if (!SharedHomeStorage.RequestTake(item, inventory)) return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Slot), nameof(Slot.Plug))]
internal static class SharedHomeSlotTakePatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Slot __instance, Item otherItem, object[] __args, ref bool __result)
    {
        if (!SharedHomeStorage.RequestTake(otherItem, slot: __instance)) return true;
        __args[1] = null;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveAt))]
internal static class SharedHomeHoldStoragePatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Inventory __instance, object[] __args, ref bool __result)
    {
        if (!SharedHomeStorage.Hold(__instance)) return true;
        __args[1] = null;
        __result = false;
        return false;
    }
}
