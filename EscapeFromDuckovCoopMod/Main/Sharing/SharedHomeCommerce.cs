namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeCommerce
{
    internal static async UniTaskVoid Craft(string id)
    {
        if (!CraftingManager.Instance || !CraftingManager.UnlockedFormulaIDs.Contains(id)) return;
        CoopSessionLog.Write("Craft", $"Started formula={id}");
        var items = await CraftingManager.Instance.Craft(id);
        CoopSessionLog.Write("Craft", $"Finished formula={id} success={items != null}");
        SharedHome.MarkDirty();
    }
}
