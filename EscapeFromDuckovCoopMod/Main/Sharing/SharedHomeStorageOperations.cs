using ItemStatsSystem;
using Newtonsoft.Json.Linq;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeStorageOperations
{
    private static readonly HashSet<Item> Depositing = new();
    private static int _generation;
    internal static void Reset()
    {
        _generation++;
        Depositing.Clear();
    }
    internal static bool Put(Item item, Inventory inventory, int position = -1)
    {
        if (!item || !SharedHomeStorage.Hold(inventory) || SharedHomeStorage.IsShared(item.InInventory)) return false;
        if (SharedHomeLoadouts.Ready && !Depositing.Contains(item)) SharedHomeStorage.SendBuffer(item, false, position);
        return true;
    }
    internal static async UniTaskVoid DepositPart(Item item, Item target)
    {
        if (!SharedHomeLoadouts.Ready || !Depositing.Add(item)) return;
        var generation = _generation;
        var position = target.InInventory.GetIndex(target);
        try
        {
            var count = Math.Min(item.StackCount, target.MaxStackCount - target.StackCount);
            if (count <= 0 || item.TypeID != target.TypeID || !target.Stackable) return;
            var part = count == item.StackCount ? item : await item.Split(count);
            if (!part) return;
            if (generation != _generation || !SharedHome.Guest) { part.DestroyTree(); return; }
            SharedHomeStorage.SendBuffer(part, false, position);
        }
        finally { Depositing.Remove(item); }
    }

    internal static string Key(Item item)
    {
        if (!item) return string.Empty;
        var writer = new NetDataWriter();
        ItemTool.WriteItemSnapshot(writer, WithoutInspection(ItemTool.MakeSnapshot(item)));
        using var hash = System.Security.Cryptography.SHA256.Create();
        return Convert.ToBase64String(hash.ComputeHash(writer.CopyData()));
    }

    private static ItemSnapshot WithoutInspection(ItemSnapshot snapshot)
    {
        // Search progress is local to each player and must not invalidate an otherwise identical item.
        if (snapshot.CustomDataKeys != null)
        {
            var indices = Enumerable.Range(0, snapshot.CustomDataKeys.Length)
                .Where(i => snapshot.CustomDataKeys[i] != "core.var.Inspected").ToArray();
            snapshot.CustomDataValues = indices.Select(i => snapshot.CustomDataValues[i]).ToArray();
            snapshot.CustomDataKeys = indices.Select(i => snapshot.CustomDataKeys[i]).ToArray();
        }
        if (snapshot.Inventory != null)
            for (var i = 0; i < snapshot.Inventory.Length; i++)
                snapshot.Inventory[i].Item = WithoutInspection(snapshot.Inventory[i].Item);
        if (snapshot.Slots != null)
            for (var i = 0; i < snapshot.Slots.Length; i++)
                if (snapshot.Slots[i].HasItem) snapshot.Slots[i].Item = WithoutInspection(snapshot.Slots[i].Item);
        return snapshot;
    }

    internal static async UniTaskVoid Execute(JObject args, NetPeer peer)
    {
        var token = (string)args["token"];
        if (string.IsNullOrEmpty(token) || token.Length > 64) return;
        var inventory = PlayerStorage.Inventory;
        try
        {
            var position = (int)args["position"];
            if (!inventory || position < 0 || position >= inventory.Capacity) return;
            var item = inventory.GetItemAt(position);
            if (!item || Key(item) != (string)args["expected"]) return;
            var details = (JObject)args["details"];
            switch ((string)details["operation"])
            {
                case "sell":
                    var shop = SharedHomeWorld.Shops.FirstOrDefault(s => s && s.MerchantID == (string)details["merchant"]);
                    if (!shop || shop.Busy || !item.CanBeSold) return;
                    await (UniTask)AccessTools.Method(typeof(Duckov.Economy.StockShop), "Sell").Invoke(shop, new object[] { item });
                    SharedHome.MarkDirty();
                    break;
                case "drop":
                    if (!item.CanDrop) return;
                    item.Drop(new Vector3((float)details["x"], (float)details["y"], (float)details["z"]),
                        (bool)details["rigidbody"], new Vector3((float)details["dx"], (float)details["dy"], (float)details["dz"]),
                        (float)details["angle"]);
                    break;
                case "merge":
                case "move":
                    var target = (int)details["target"];
                    if (target < 0 || target >= inventory.Capacity || target == position) return;
                    var other = inventory.GetItemAt(target);
                    if (Key(other) != (string)details["expected"]) return;
                    if ((string)details["operation"] == "merge") other?.Combine(item);
                    else
                    {
                        item.Detach();
                        if (other) other.Detach();
                        inventory.AddAt(item, target);
                        if (other) inventory.AddAt(other, position);
                    }
                    break;
                case "unload":
                    item.GetComponent<ItemSetting_Gun>()?.TakeOutAllBullets();
                    COOPManager.LootNet.Server_BroadcastHostSlotChange(inventory, position);
                    break;
            }
        }
        finally
        {
            // Release the guest's operation lock and refresh even when its view was stale.
            if (inventory) COOPManager.LootNet.Server_SendLootboxState(peer, inventory);
            var reply = new NetDataWriter();
            reply.Put(token);
            reply.Put(false);
            SharedHome.Reply(peer, reply.CopyData());
        }
    }
}
