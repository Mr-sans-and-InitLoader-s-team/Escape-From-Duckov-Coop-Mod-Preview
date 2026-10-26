using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeCommerce
{
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
