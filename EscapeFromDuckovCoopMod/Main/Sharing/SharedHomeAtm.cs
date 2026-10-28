using Duckov.Economy;
using ItemStatsSystem;
using Newtonsoft.Json.Linq;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeAtm
{
    internal static bool Busy => SharedHomeInventoryTransaction.Busy;

    internal static UniTask<bool> Request(long amount, bool draw)
    {
        if (amount <= 0 || amount > int.MaxValue) return UniTask.FromResult(false);
        if (draw) amount = Math.Min(amount, ATMPanel.MaxDrawAmount);
        return SharedHomeInventoryTransaction.Request("atm", new { amount, draw });
    }

    internal static async UniTask<bool> Execute(JObject args, Item character, Inventory safe, Func<bool> current)
    {
        var amount = (long?)args["amount"] ?? 0;
        var draw = (bool?)args["draw"] == true;
        if (amount <= 0 || amount > int.MaxValue || draw && (amount > ATMPanel.MaxDrawAmount || EconomyManager.Money < amount)) return false;
        var generated = new List<Item>();
        try
        {
            if (draw)
            {
                var remaining = amount;
                while (remaining > 0)
                {
                    var cash = await ItemAssetsCollection.InstantiateAsync(EconomyManager.CashItemID);
                    cash.StackCount = (int)Math.Min(remaining, cash.MaxStackCount);
                    cash.FromInfoKey = "UI_ATM_Title";
                    generated.Add(cash);
                    remaining -= cash.StackCount;
                }
            }
            await UniTask.WaitUntil(() => !current() || !(bool)AccessTools.Field(typeof(ATMPanel), "drawingMoney").GetValue(null));
            if (!current()) return false;
            var paid = SharedHomeInventoryTransaction.WithInventories(character, safe, () =>
                draw ? new Cost(amount).Pay(true, false) : new Cost((EconomyManager.CashItemID, amount)).Pay(false));
            if (!paid) return false;
            if (draw)
            {
                foreach (var cash in generated)
                    if (!ItemUtilities.AddAndMerge(character.Inventory, cash)) PlayerStorage.Push(cash, false);
            }
            else EconomyManager.Add(amount);
            return true;
        }
        finally
        {
            foreach (var cash in generated) if (cash && !cash.ParentObject && !cash.IsBeingDestroyed) cash.DestroyTree();
        }
    }
}
