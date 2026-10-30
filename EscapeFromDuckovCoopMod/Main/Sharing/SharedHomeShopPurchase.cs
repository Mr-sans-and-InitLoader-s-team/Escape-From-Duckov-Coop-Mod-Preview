using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;
using Newtonsoft.Json.Linq;
using SodaCraft.StringUtilities;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeShopPurchase
{
    private sealed class Purchase { internal Item Item; }
    private static readonly Dictionary<StockShop, Purchase> Purchases = new();
    private static readonly System.Reflection.FieldInfo Buying = AccessTools.Field(typeof(StockShop), "buying");

    internal static async UniTask<bool> Request(StockShop shop, int type, int amount)
    {
        if (!SharedHomeLoadouts.Ready || shop.Busy || amount <= 0) return false;
        Buying.SetValue(shop, true);
        try
        {
            if (shop.AccountAvaliable) return await SharedHomeStorage.RequestPurchase(shop, type, amount);
            if (!await SharedHomeInventoryTransaction.Request("shopBuyPersonal", new { merchant = shop.MerchantID, type, amount })) return false;
            var item = CharacterMainControl.Main.CharacterItem.Inventory.LastOrDefault(i => i && i.TypeID == type);
            Received(shop, item, item ? item.DisplayName : ItemAssetsCollection.GetMetaData(type).DisplayName);
            return true;
        }
        finally { if (shop) Buying.SetValue(shop, false); }
    }

    internal static async UniTask<bool> BuyPersonal(JObject args, Item character, Inventory safe, NetPeer peer)
    {
        var merchant = (string)args["merchant"];
        var type = (int)args["type"];
        var amount = (int)args["amount"];
        var shop = SharedHomeWorld.Shops.FirstOrDefault(s => s && s.MerchantID == merchant);
        if (!shop || shop.AccountAvaliable || shop.Busy || Purchases.ContainsKey(shop) || amount <= 0 ||
            !shop.entries.Any(e => e.ItemTypeID == type && e.Show && e.CurrentStock >= amount)) return false;
        var purchase = new Purchase();
        Purchases[shop] = purchase;
        try
        {
            var task = SharedHomeInventoryTransaction.WithInventories(character, safe, () => shop.Buy(type, amount));
            if (!await task) return false;
            var item = purchase.Item;
            if (item && !character.Inventory.AddAndMerge(item))
            {
                if (LevelConfig.SaveCharacter) ItemUtilities.SendToPlayerStorage(item);
                else
                {
                    var owner = NetService.Instance.remoteCharacters.TryGetValue(peer, out var remote)
                        ? remote.GetComponent<CharacterMainControl>() : CharacterMainControl.Main;
                    item.Drop(owner, true);
                }
            }
            return true;
        }
        finally { Purchases.Remove(shop); }
    }

    internal static async UniTaskVoid Buy(string token, string merchant, int type, int amount, NetPeer peer)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 64) return;
        var shop = SharedHomeWorld.Shops.FirstOrDefault(s => s && s.MerchantID == merchant);
        if (!shop || shop.Busy || Purchases.ContainsKey(shop) || amount <= 0 ||
            !shop.entries.Any(e => e.ItemTypeID == type && e.Show && e.CurrentStock >= amount))
        {
            SharedHomeStorage.DeliverPurchase(token, null, peer);
            return;
        }
        var purchase = new Purchase();
        Purchases[shop] = purchase;
        try
        {
            if (!await shop.Buy(type, amount)) return;
        }
        finally
        {
            Purchases.Remove(shop);
            SharedHomeStorage.DeliverPurchase(token, purchase.Item, peer);
            SharedHome.MarkDirty();
        }
    }

    internal static bool SendToBuyer(Item item, bool dontMerge, StockShop shop)
    {
        if (!Purchases.TryGetValue(shop, out var purchase)) return ItemUtilities.SendToPlayerCharacterInventory(item, dontMerge);
        purchase.Item = item;
        return true;
    }

    internal static void Notify(string text, StockShop shop)
    {
        if (!Purchases.ContainsKey(shop)) NotificationText.Push(text);
    }

    internal static void Received(StockShop shop, Item item, string displayName)
    {
        if (!shop) return;
        (AccessTools.Field(typeof(StockShop), "OnAfterItemSold").GetValue(null) as Action<StockShop>)?.Invoke(shop);
        (AccessTools.Field(typeof(StockShop), "OnItemPurchased").GetValue(null) as Action<StockShop, Item>)?.Invoke(shop, item);
        NotificationText.Push(shop.PurchaseNotificationTextFormat.Format(new { itemDisplayName = displayName }));
    }
}
