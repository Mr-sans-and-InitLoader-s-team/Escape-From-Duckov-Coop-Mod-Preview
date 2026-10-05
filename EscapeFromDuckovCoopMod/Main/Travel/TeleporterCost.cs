using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

internal sealed class TeleporterCost
{
    private Cost _refund;
    private bool _hasRefund;
    public bool Paid { get; private set; }

    public static MapSelectionEntry Resolve(string sceneId, int beaconIndex)
    {
        var view = MapSelectionView.Instance;
        if (view == null) return null;
        return view.GetComponentsInChildren<MapSelectionEntry>(true)
            .FirstOrDefault(entry => entry.SceneID == sceneId && (beaconIndex < 0 || entry.BeaconIndex == beaconIndex));
    }

    public static bool Check(MapSelectionEntry entry, out string reason)
    {
        reason = null;
        var main = CharacterMainControl.Main;
        if (main == null || main.Health == null || main.Health.IsDead || SceneLoader.IsSceneLoading)
            reason = "ui.teleporter.reason.notReady";
        else if (entry == null) reason = "ui.teleporter.reason.destinationMissing";
        else if (!entry.Cost.Enough) reason = "ui.teleporter.reason.missingCost";
        return reason == null;
    }

    public bool TryPay(MapSelectionEntry entry, out string reason)
    {
        if (Paid) { reason = null; return true; }
        if (!Check(entry, out reason)) return false;
        var cost = entry.Cost;
        var beforeMoney = EconomyManager.Money + EconomyManager.Cash;
        var beforeItems = (cost.items ?? Array.Empty<Cost.ItemEntry>()).Select(item => item.id).Distinct()
            .ToDictionary(id => id, id => ItemUtilities.GetItemCount(id));
        try
        {
            Paid = cost.Pay();
            if (!Paid) reason = "ui.teleporter.reason.paymentFailed";
            return Paid;
        }
        finally
        {
            // The native Pay can spend money before item consumption fails. Save
            // actual deductions so cancellation neither loses nor creates items.
            _refund = new Cost
            {
                money = Math.Max(0L, beforeMoney - EconomyManager.Money - EconomyManager.Cash),
                items = beforeItems.Select(pair => new Cost.ItemEntry { id = pair.Key, amount = Math.Max(0L, pair.Value - ItemUtilities.GetItemCount(pair.Key)) })
                    .Where(item => item.amount > 0).ToArray()
            };
            _hasRefund = !_refund.IsFree;
        }
    }

    public void Commit() { _hasRefund = false; }

    public void Rollback()
    {
        if (!_hasRefund) return;
        _hasRefund = false;
        ReturnCost(_refund).Forget();
    }

    private static async UniTask ReturnCost(Cost cost)
    {
        try
        {
            var method = AccessTools.Method(typeof(Cost), "Return");
            var task = (UniTask)method.Invoke(cost, new object[] { false, true, 1, null });
            await task;
        }
        catch (Exception ex) { Debug.LogError($"[Teleporter] Cannot return cancelled travel cost: {ex}"); }
    }
}
