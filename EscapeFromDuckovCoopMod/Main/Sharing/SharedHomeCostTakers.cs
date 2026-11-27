using Duckov.Economy;
using Duckov.Scenes;
using ItemStatsSystem;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeCostTakers
{
    private static readonly System.Reflection.FieldInfo PaidEvent = AccessTools.Field(typeof(CostTaker), "onPayed");
    private static readonly Dictionary<CostTaker, NetPeer> PendingLotteries = new();
    private static bool HasMaterials(CostTaker taker) => taker && taker.Cost.items != null && taker.Cost.items.Any(item => item.amount > 0);
    private static string SceneId => MultiSceneCore.Instance?.SceneInfo?.ID;
    private static string Signature(Cost cost) => JsonConvert.SerializeObject(new
    {
        cost.money,
        items = (cost.items ?? Array.Empty<Cost.ItemEntry>()).Select(item => new { item.id, item.amount })
    });

    internal static bool TryRequest(CostTaker taker)
    {
        if (taker && SharedHome.Active && PendingLotteries.ContainsKey(taker))
        {
            ShowFailure();
            return true;
        }
        if (!SharedHome.Guest || SharedHome.Applying || !HasMaterials(taker)) return false;
        Request(taker).Forget();
        return true;
    }

    private static async UniTaskVoid Request(CostTaker taker)
    {
        if (SceneLoader.IsSceneLoading || !LevelDataBoolNet.TryGetInteractableKey(taker, out var key) || string.IsNullOrEmpty(SceneId))
        {
            ShowFailure();
            return;
        }
        var scene = SceneId;
        if (!await SharedHomeInventoryTransaction.Request("costTaker", new { scene, key, cost = Signature(taker.Cost) }))
        {
            ShowFailure();
            return;
        }
        if (!taker || !SharedHome.Guest || SceneLoader.IsSceneLoading || SceneId != scene || ConstructionSiteReplay.IsSharedConstructionCost(taker)) return;
        // Personal payment callbacks, including lottery rewards, still run on the payer.
        NotifyPaid(taker);
    }

    internal static bool Execute(JObject args, Item character, Inventory safe, NetPeer peer, Func<bool> current)
    {
        if (!current() || SceneLoader.IsSceneLoading || string.IsNullOrEmpty(SceneId) || (string)args["scene"] != SceneId) return false;
        var signature = (string)args["cost"];
        if (string.IsNullOrEmpty(signature) || signature.Length > 4096) return false;
        var matches = CostTaker.ActiveCostTakers.Where(taker => HasMaterials(taker) && taker.isActiveAndEnabled && !PendingLotteries.ContainsKey(taker) &&
            LevelDataBoolNet.TryGetInteractableKey(taker, out var key) && key == (int)args["key"] && Signature(taker.Cost) == signature).ToArray();
        if (matches.Length != 1) return false;
        var taker = matches[0];
        return SharedHomeInventoryTransaction.WithInventories(character, safe, () =>
        {
            if (!taker.Cost.Enough || !taker.Cost.Pay(!taker.CashOnly)) return false;
            // Commit shared construction on the host even if the payer leaves before receiving the reply.
            if (ConstructionSiteReplay.IsSharedConstructionCost(taker)) NotifyPaid(taker);
            else if (LotteryBoxNet.IsPaymentGate(taker))
            {
                PendingLotteries[taker] = peer;
                if (taker.Interacting) taker.StopInteract();
            }
            return true;
        });
    }

    private static void NotifyPaid(CostTaker taker)
    {
        (PaidEvent.GetValue(taker) as Action<CostTaker>)?.Invoke(taker);
        taker.onPayedUnityEvent?.Invoke(taker);
        LevelDataBoolNet.OnCostTakerPaid(taker);
    }

    private static void ShowFailure() => MModUI.ShowTip(CoopLocalization.Get("ui.sharedHome.paymentFailed"));
    internal static bool CanBeginLottery(CostTaker taker, NetPeer peer) =>
        !taker || !PendingLotteries.TryGetValue(taker, out var payer) || payer == peer;
    internal static void CancelPending(JObject args, NetPeer peer)
    {
        if (!int.TryParse((string)args["key"], out var key)) return;
        foreach (var taker in PendingLotteries.Where(entry => entry.Value == peer &&
                     LevelDataBoolNet.TryGetInteractableKey(entry.Key, out var candidate) && candidate == key).Select(entry => entry.Key).ToArray())
            PendingLotteries.Remove(taker);
    }
    internal static void Release(CostTaker taker) => PendingLotteries.Remove(taker);
    internal static void Reset() => PendingLotteries.Clear();
    internal static void PeerDisconnected(NetPeer peer)
    {
        foreach (var taker in PendingLotteries.Where(entry => entry.Value == peer).Select(entry => entry.Key).ToArray())
            PendingLotteries.Remove(taker);
    }
}
