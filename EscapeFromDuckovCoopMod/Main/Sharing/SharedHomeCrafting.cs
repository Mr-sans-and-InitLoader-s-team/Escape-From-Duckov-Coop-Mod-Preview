using Duckov.UI;
using ItemStatsSystem;
using Newtonsoft.Json.Linq;
using SodaCraft.StringUtilities;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeCrafting
{
    internal static async UniTask<List<Item>> Request(string id)
    {
        if (!CraftingFormulaCollection.TryGetFormula(id, out var formula) ||
            !await SharedHomeInventoryTransaction.Request("craft", new { id })) return null;
        var view = (CraftView)AccessTools.Property(typeof(CraftView), "Instance").GetValue(null);
        if (view)
        {
            var format = (string)AccessTools.Property(typeof(CraftView), "NotificationFormat").GetValue(view);
            NotificationText.Push(format.Format(new { itemDisplayName = ItemAssetsCollection.GetMetaData(formula.result.id).DisplayName }));
        }
        // Shared output already belongs to the warehouse; it must not be created again locally.
        return new List<Item>();
    }

    internal static async UniTask<bool> Execute(JObject args, Item character, Inventory safe, Func<bool> current)
    {
        var id = (string)args["id"];
        if (!current() || !CraftingManager.Instance || !CraftingManager.UnlockedFormulaIDs.Contains(id)) return false;
        CoopSessionLog.Write("Craft", $"Started formula={id}");
        var task = SharedHomeInventoryTransaction.WithInventories(character, safe, () => CraftingManager.Instance.Craft(id));
        var items = await task;
        CoopSessionLog.Write("Craft", $"Finished formula={id} success={items != null}");
        return items != null;
    }
}
