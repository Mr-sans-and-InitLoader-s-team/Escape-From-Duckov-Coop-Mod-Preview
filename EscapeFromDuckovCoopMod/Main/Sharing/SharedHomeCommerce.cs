using Duckov.Economy;
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeCommerce
{
    internal static async UniTaskVoid Buy(string merchant, int type, int amount)
    {
        var shop = SharedHomeWorld.Shops.FirstOrDefault(s => s && s.MerchantID == merchant);
        if (!shop || amount <= 0 || !shop.entries.Any(e => e.ItemTypeID == type && e.Show && e.CurrentStock >= amount)) return;
        await shop.Buy(type, amount);
        SharedHome.MarkDirty();
    }
    internal static async UniTaskVoid Craft(string id)
    {
        if (!CraftingManager.Instance || !CraftingManager.UnlockedFormulaIDs.Contains(id)) return;
        await CraftingManager.Instance.Craft(id);
        SharedHome.MarkDirty();
    }
    internal static void StoreOutput(Item item)
    {
        if (!SharedHome.Active || !NetService.Instance.IsServer || !item) return;
        item.Detach();
        ItemUtilities.SendToPlayerStorage(item);
        SharedHome.MarkDirty();
    }
}
