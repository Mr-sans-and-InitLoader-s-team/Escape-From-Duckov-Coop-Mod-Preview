using Duckov.Economy;
using ItemStatsSystem;
using Saves;
using System.Reflection;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch]
internal static class SharedHomeSavedFlagPatch
{
    private static MethodBase TargetMethod() => typeof(SavesSystem).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == "Save" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
        .MakeGenericMethod(typeof(bool));
    private static bool Prefix(string realKey, bool value)
    {
        if (!SharedHome.Guest || SharedHome.Applying || !SharedHomeWorld.ValueKey(realKey)) return true;
        if (!SharedHomeLoadouts.Ready && realKey.StartsWith("MultiSceneCore_Visited_")) return false;
        SharedHome.Request("worldFlag", new { key = realKey, value });
        return false;
    }
    private static void Postfix(string realKey)
    {
        if (!SharedHomeSave.Capturing && SharedHomeWorld.ValueKey(realKey)) SharedHome.MarkDirty();
    }
}

[HarmonyPatch(typeof(SaveDataBoolProxy), nameof(SaveDataBoolProxy.Save))]
internal static class SharedHomeWorldFlagPatch
{
    private static bool Prefix(SaveDataBoolProxy __instance)
    {
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        SharedHome.Request("worldFlag", new { __instance.key, __instance.value });
        return false;
    }
    private static void Postfix() => SharedHome.MarkDirty();
}

[HarmonyPatch(typeof(SavesCounter), nameof(SavesCounter.AddCount))]
internal static class SharedHomeCountPatch
{
    private static bool Prefix(string countKey, ref int __result)
    {
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        SharedHome.Request("count", new { key = countKey });
        __result = SavesCounter.GetCount(countKey);
        return false;
    }
    private static void Postfix() => SharedHome.MarkDirty();
}

[HarmonyPatch(typeof(StockShop), "Awake")]
internal static class SharedHomeShopRegisterPatch
{
    private static void Postfix(StockShop __instance) => SharedHomeWorld.Shops.Add(__instance);
}

[HarmonyPatch(typeof(StockShop), "RefreshIfNeeded")]
internal static class SharedHomeShopRefreshPatch
{
    private static bool Prefix() => !SharedHome.Guest;
    private static void Postfix() => SharedHome.MarkDirty();
}

[HarmonyPatch(typeof(StockShop), nameof(StockShop.Buy))]
internal static class SharedHomeShopBuyPatch
{
    private static bool Prefix(StockShop __instance, int itemTypeID, int amount, ref UniTask<bool> __result)
    {
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        __result = SharedHomeShopPurchase.Request(__instance, itemTypeID, amount);
        return false;
    }
}

[HarmonyPatch(typeof(CraftingManager), nameof(CraftingManager.Craft), typeof(string))]
internal static class SharedHomeCraftPatch
{
    private static bool Prefix(string id, ref UniTask<List<Item>> __result)
    {
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        __result = SharedHomeCrafting.Request(id);
        return false;
    }
}
