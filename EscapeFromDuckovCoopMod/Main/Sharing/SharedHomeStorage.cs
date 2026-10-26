using ItemStatsSystem;
using ItemStatsSystem.Items;
using Newtonsoft.Json.Linq;
using Saves;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeStorage
{
    internal const int LootUid = int.MaxValue - 17;
    private static InteractableLootbox _registered;
    private sealed class Destination
    {
        internal Item Item;
        internal Inventory Inventory;
        internal Slot Slot;
        internal Item Merge;
        internal bool Use;
        internal int Position;
        internal UniTaskCompletionSource<Item> Split;
        internal UniTaskCompletionSource<bool> Purchase;
        internal Duckov.Economy.StockShop Shop;
    }
    private static readonly Dictionary<string, Destination> Pending = new();
    private static readonly Dictionary<string, (NetPeer Peer, Item Item)> Escrow = new();
    private const string EscrowKey = "SharedHome/StorageEscrow";
    private static bool _recovered;
    internal static bool Sorting { get; private set; }
    internal static bool IsShared(Inventory inventory) => SharedHome.Active && (!SharedHome.Guest || SharedHomeSave.OverlayActive) &&
        inventory && ReferenceEquals(inventory, PlayerStorage.Inventory);
    internal static bool CanTransfer => !PlayerStorage.Loading && !(PlayerStorage.Inventory && PlayerStorage.Inventory.Loading) &&
        CharacterMainControl.Main && CharacterMainControl.Main.CharacterItem;
    internal static void BeginSort() => Sorting = true;
    internal static void EndSort()
    {
        Sorting = false;
        if (!IsShared(PlayerStorage.Inventory) || !NetService.Instance.IsServer) return;
        foreach (var peer in NetService.Instance.netManager.ConnectedPeerList)
            if (SharedHomeCompatibility.Allows(peer)) COOPManager.LootNet.Server_SendLootboxState(peer, PlayerStorage.Inventory);
        SharedHome.MarkDirty();
    }
    internal static void Ensure()
    {
        if (!SharedHome.Active || !PlayerStorage.Instance || PlayerStorage.Loading || !LootManager.Instance) return;
        if (SharedHome.Guest && !SharedHomeSave.OverlayActive) return;
        if (NetService.Instance.IsServer && !_recovered)
        {
            _recovered = true;
            var saved = SavesSystem.Load<List<byte[]>>(EscrowKey);
            if (saved != null)
                foreach (var bytes in saved)
                {
                    var reader = new NetPacketReader(bytes, 0, bytes.Length);
                    PlayerStorage.Push(ItemTool.BuildItemFromSnapshot(ItemTool.ReadItemSnapshot(reader)));
                }
            SaveEscrow();
        }
        var box = PlayerStorage.Instance.InteractableLootBox;
        if (!box || _registered == box && CoopSyncDatabase.Loot.TryGetByLootUid(LootUid, out var entry) && entry.Inventory == PlayerStorage.Inventory) return;
        Unregister();
        _registered = box;
        CoopSyncDatabase.Loot.Register(box, PlayerStorage.Inventory, LootUid);
        if (NetService.Instance.IsServer)
        {
            LootManager.Instance._srvLootByUid[LootUid] = PlayerStorage.Inventory;
            // Guests can finish loading first and have their initial request denied.
            foreach (var peer in NetService.Instance.netManager.ConnectedPeerList)
                if (SharedHomeCompatibility.Allows(peer)) COOPManager.LootNet.Server_SendLootboxState(peer, PlayerStorage.Inventory);
        }
        else
        {
            LootManager.Instance._cliLootByUid[LootUid] = PlayerStorage.Inventory;
            COOPManager.LootNet.Client_RequestLootState(PlayerStorage.Inventory, true);
        }
    }
    internal static void Reset()
    {
        foreach (var entry in Escrow.Values)
            if (entry.Item && PlayerStorage.Inventory) PlayerStorage.Push(entry.Item);
        Escrow.Clear();
        foreach (var pending in Pending.Values)
        {
            pending.Split?.TrySetResult(null);
            pending.Purchase?.TrySetResult(false);
        }
        Pending.Clear();
        SharedHomeStorageOperations.Reset();
        if (_recovered) SaveEscrow();
        _recovered = false;
        Unregister();
    }
    private static void Unregister()
    {
        if (_registered) CoopSyncDatabase.Loot.Unregister(_registered);
        _registered = null;
        LootManager.Instance?._srvLootByUid.Remove(LootUid);
        LootManager.Instance?._cliLootByUid.Remove(LootUid);
    }
    internal static bool RequestTake(Item item, Inventory destination = null, int position = -1, Slot slot = null, Item merge = null, int count = 0, bool use = false)
    {
        if (!item || !Hold(item.InInventory)) return false;
        if (IsShared(destination))
            return RequestEdit(item, new { operation = "move", target = position, expected = SharedHomeStorageOperations.Key(destination.GetItemAt(position)) });
        if (Pending.Values.Any(p => p.Item == item)) return true;
        var token = Guid.NewGuid().ToString("N");
        Pending[token] = new Destination { Item = item, Inventory = destination, Position = position, Slot = slot, Merge = merge, Use = use };
        SharedHome.Request("storageTake", new { token, position = item.InInventory.GetIndex(item), type = item.TypeID,
            count = count >= item.StackCount ? 0 : count, expected = SharedHomeStorageOperations.Key(item) });
        return true;
    }
    internal static bool RequestEdit(Item item, object details)
    {
        if (!item || !Hold(item.InInventory)) return false;
        if (!SharedHomeLoadouts.Ready || Pending.Values.Any(p => p.Item == item)) return true;
        var token = Guid.NewGuid().ToString("N");
        Pending[token] = new Destination { Item = item };
        SharedHome.Request("storageEdit", new { token, position = item.InInventory.GetIndex(item), expected = SharedHomeStorageOperations.Key(item), details });
        return true;
    }
    internal static void Take(JObject args, NetPeer peer)
    {
        var token = (string)args["token"];
        if (string.IsNullOrEmpty(token) || token.Length > 64 || Escrow.ContainsKey(token)) return;
        var position = (int)args["position"];
        var inv = PlayerStorage.Inventory;
        var item = inv?.GetItemAt(position);
        var accepted = item && item.TypeID == (int)args["type"] && SharedHomeStorageOperations.Key(item) == (string)args["expected"];
        var count = (int?)args["count"] ?? 0;
        if (count > 0 && (!accepted || !item.Stackable || count >= item.StackCount)) accepted = false;
        var writer = new NetDataWriter();
        writer.Put(token);
        writer.Put(accepted);
        if (accepted)
        {
            var snapshot = ItemTool.MakeSnapshot(item);
            var held = item;
            if (count > 0)
            {
                snapshot.Stack = count;
                held = ItemTool.BuildItemFromSnapshot(snapshot);
                held.StackCount = count;
                item.StackCount -= count;
            }
            else inv.RemoveAt(position, out _);
            ItemTool.WriteItemSnapshot(writer, ItemTool.MakeSnapshot(held));
            held.transform.SetParent(null);
            UnityEngine.Object.DontDestroyOnLoad(held.gameObject);
            Escrow[token] = (peer, held);
            SaveEscrow();
        }
        SharedHome.Reply(peer, writer.CopyData());
    }
    internal static void ReceiveTake(byte[] bytes)
    {
        var reader = new NetPacketReader(bytes, 0, bytes.Length);
        var token = reader.GetString();
        var accepted = reader.GetBool();
        if (!Pending.TryGetValue(token, out var destination)) return;
        Pending.Remove(token);
        if (!accepted) { destination.Split?.TrySetResult(null); destination.Purchase?.TrySetResult(false); return; }
        var item = ItemTool.BuildItemFromSnapshot(ItemTool.ReadItemSnapshot(reader));
        var displayName = item.DisplayName;
        if (destination.Purchase != null) item.Inspected = true;
        var stored = false;
        SharedHome.Apply(() =>
        {
            if (destination.Split != null) { stored = destination.Split.TrySetResult(item); return; }
            if (destination.Merge && destination.Merge.TypeID == item.TypeID &&
                destination.Merge.MaxStackCount - destination.Merge.StackCount >= item.StackCount)
            {
                destination.Merge.Combine(item);
                stored = true;
            }
            if (!stored && destination.Slot != null && destination.Slot.Content == null)
                stored = destination.Slot.Plug(item, out _);
            if (!stored && destination.Inventory && destination.Position >= 0 &&
                !destination.Inventory.GetItemAt(destination.Position))
                stored = destination.Inventory.AddAt(item, destination.Position);
            if (!stored && destination.Inventory && destination.Position < 0 && CanReceive(destination.Inventory, item))
                stored = ItemUtilities.AddAndMerge(destination.Inventory, item);
            if (destination.Purchase == null && !stored && (CanReceive(CharacterMainControl.Main.CharacterItem.Inventory, item) ||
                CharacterMainControl.Main.CharacterItem.Slots.Any(s => s.Content == null && s.CanPlug(item))))
                stored = ItemUtilities.SendToPlayerCharacter(item);
            if (stored && destination.Use && item) CharacterMainControl.Main.UseItem(item);
        });
        if (!stored && item) item.DestroyTree();
        SharedHomeLoadouts.Flush();
        SharedHome.Request("storageTaken", new { token, stored });
        if (destination.Purchase != null)
        {
            try { SharedHomeShopPurchase.Received(destination.Shop, item, displayName); }
            finally { destination.Purchase.TrySetResult(true); }
        }
    }
    internal static UniTask<bool> RequestPurchase(Duckov.Economy.StockShop shop, int type, int amount)
    {
        var token = Guid.NewGuid().ToString("N");
        var completion = new UniTaskCompletionSource<bool>();
        Pending[token] = new Destination { Purchase = completion, Shop = shop, Inventory = CharacterMainControl.Main.CharacterItem.Inventory, Position = -1 };
        SharedHome.Request("shopBuy", new { token, merchant = shop.MerchantID, type, amount });
        return completion.Task;
    }
    internal static void DeliverPurchase(string token, Item item, NetPeer peer)
    {
        if (peer.ConnectionState != ConnectionState.Connected)
        {
            if (item) PlayerStorage.Push(item, true);
            return;
        }
        var writer = new NetDataWriter();
        writer.Put(token);
        writer.Put((bool)item);
        if (item)
        {
            item.Detach();
            item.transform.SetParent(null);
            UnityEngine.Object.DontDestroyOnLoad(item.gameObject);
            Escrow[token] = (peer, item);
            SaveEscrow();
            ItemTool.WriteItemSnapshot(writer, ItemTool.MakeSnapshot(item));
        }
        SharedHome.Reply(peer, writer.CopyData());
    }
    internal static UniTask<Item> Split(Item item, int count)
    {
        if (!item.Stackable || count <= 0 || count >= item.StackCount || Pending.Values.Any(p => p.Item == item))
            return UniTask.FromResult<Item>(null);
        var completion = new UniTaskCompletionSource<Item>();
        var token = Guid.NewGuid().ToString("N");
        Pending[token] = new Destination { Item = item, Split = completion };
        SharedHome.Request("storageTake", new { token, position = item.InInventory.GetIndex(item), type = item.TypeID, count,
            expected = SharedHomeStorageOperations.Key(item) });
        return completion.Task;
    }
    private static bool CanReceive(Inventory inventory, Item item) => inventory && item &&
        (inventory.GetFirstEmptyPosition() >= 0 || item.Stackable && inventory.Where(i => i && i.TypeID == item.TypeID)
            .Sum(i => Math.Max(0, i.MaxStackCount - i.StackCount)) >= item.StackCount);
    internal static void FinishTake(JObject args, NetPeer peer)
    {
        var token = (string)args["token"];
        if (token == null || !Escrow.TryGetValue(token, out var entry) || entry.Peer != peer) return;
        Escrow.Remove(token);
        if ((bool)args["stored"]) entry.Item.DestroyTree();
        else PlayerStorage.Push(entry.Item);
        SaveEscrow();
    }
    internal static void PeerDisconnected(NetPeer peer)
    {
        if (Escrow.Count == 0) return;
        foreach (var key in Escrow.Where(p => p.Value.Peer == peer).Select(p => p.Key).ToArray())
        {
            var item = Escrow[key].Item;
            Escrow.Remove(key);
            if (item) PlayerStorage.Push(item);
        }
        SaveEscrow();
    }
    internal static bool Hold(Inventory inventory) => SharedHome.Guest && !SharedHome.Applying &&
        CanTransfer && COOPManager.LootNet?.ApplyingLootState != true && IsShared(inventory);
    internal static string BufferKey(int index)
    {
        if (index < 0 || index >= PlayerStorage.IncomingItemBuffer.Count) return string.Empty;
        using var hash = System.Security.Cryptography.SHA256.Create();
        return Convert.ToBase64String(hash.ComputeHash(ES3.Serialize(PlayerStorage.IncomingItemBuffer[index])));
    }
    internal static async UniTaskVoid TakeBuffer(int index, string expected)
    {
        if (index < 0 || index >= PlayerStorage.IncomingItemBuffer.Count || BufferKey(index) != expected) return;
        await PlayerStorage.TakeBufferItem(index);
        SharedHome.MarkDirty();
    }
    internal static void SendBuffer(Item item, bool toBuffer = true, int position = -1)
    {
        if (!item || !CanTransfer) return;
        var data = new ES3File(false);
        data.Save("item", ItemStatsSystem.Data.ItemTreeData.FromItem(item));
        var source = item.InInventory;
        var sourcePosition = source ? source.GetIndex(item) : -1;
        item.Detach();
        var sent = false;
        try
        {
            var character = SharedHomeLoadouts.Report();
            sent = SharedHome.Request("storageBufferPut", new { item = Convert.ToBase64String(data.LoadRawBytes()), character = Convert.ToBase64String(character), toBuffer, position });
            if (sent) item.DestroyTree();
        }
        finally
        {
            if (!sent && item)
            {
                SharedHome.Apply(() =>
                {
                    if (source && sourcePosition >= 0 && !source.GetItemAt(sourcePosition) && source.AddAt(item, sourcePosition)) return;
                    if (!ItemUtilities.SendToPlayerCharacterInventory(item)) item.Drop(CharacterMainControl.Main, true);
                });
                CoopSessionLog.Write("Storage", "Deposit request unavailable; original item returned");
            }
        }
    }
    internal static async UniTaskVoid PutBuffer(JObject args, NetPeer peer)
    {
        var data = new ES3File(Convert.FromBase64String((string)args["item"]));
        var tree = data.Load<ItemStatsSystem.Data.ItemTreeData>("item");
        if (tree?.entries == null || tree.entries.Count == 0 || tree.entries.Count > 10000) return;
        CoopSessionLog.Write("Storage", $"Deposit received peer={peer.EndPoint} type={tree.RootTypeID} count={tree.RootData.StackCount}");
        SharedHomeLoadouts.Receive(peer, 6, Convert.FromBase64String((string)args["character"]));
        var item = await ItemStatsSystem.Data.ItemTreeData.InstantiateAsync(tree);
        if (item)
        {
            var buffer = (bool?)args["toBuffer"] ?? true;
            var position = (int?)args["position"] ?? -1;
            var inventory = PlayerStorage.Inventory;
            if (!buffer && inventory && position >= 0 && position < inventory.Capacity)
            {
                var target = inventory.GetItemAt(position);
                if (!target)
                {
                    if (inventory.AddAt(item, position)) item = null;
                }
                else if (target.Stackable && target.TypeID == item.TypeID) target.Combine(item);
            }
            if (item && item.StackCount > 0) PlayerStorage.Push(item, buffer);
        }
        SharedHome.MarkDirty();
    }
    private static void SaveEscrow()
    {
        if (NetService.Instance?.IsServer != true) return;
        var items = new List<byte[]>();
        foreach (var entry in Escrow.Values)
        {
            var writer = new NetDataWriter();
            ItemTool.WriteItemSnapshot(writer, ItemTool.MakeSnapshot(entry.Item));
            items.Add(writer.CopyData());
        }
        SavesSystem.Save(EscrowKey, items);
        SharedHome.MarkDirty();
    }
}
