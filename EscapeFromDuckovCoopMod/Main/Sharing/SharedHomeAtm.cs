using Duckov.Economy;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Newtonsoft.Json.Linq;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeAtm
{
    private static readonly Queue<(JObject Args, NetPeer Peer)> Requests = new();
    private static readonly HashSet<NetPeer> PendingPeers = new();
    private static UniTaskCompletionSource<bool> _completion;
    private static string _token;
    private static bool _running;
    private static int _generation;
    private static Inventory[] _cashInventories;
    internal static bool Busy => _completion != null;

    internal static UniTask<bool> Request(long amount, bool draw)
    {
        if (Busy || !SharedHomeLoadouts.Ready || amount <= 0 || amount > int.MaxValue) return UniTask.FromResult(false);
        if (draw) amount = Math.Min(amount, ATMPanel.MaxDrawAmount);
        var profile = Convert.ToBase64String(SharedHomeLoadouts.Report());
        _token = Guid.NewGuid().ToString("N");
        var completion = _completion = new UniTaskCompletionSource<bool>();
        if (!SharedHome.Request("atm", new { token = _token, amount, draw,
                profile })) Complete(false);
        return completion.Task;
    }

    internal static void Enqueue(JObject args, NetPeer peer)
    {
        if (!PendingPeers.Add(peer)) return;
        Requests.Enqueue((args, peer));
        if (!_running) Run(_generation).Forget();
    }

    private static async UniTaskVoid Run(int generation)
    {
        _running = true;
        try
        {
            while (generation == _generation && Requests.Count > 0)
            {
                var request = Requests.Dequeue();
                try { await Execute(request.Args, request.Peer, generation); }
                finally { if (generation == _generation) PendingPeers.Remove(request.Peer); }
            }
        }
        finally { if (generation == _generation) _running = false; }
    }

    private static async UniTask Execute(JObject args, NetPeer peer, int generation)
    {
        var token = (string)args["token"];
        var amount = (long?)args["amount"] ?? 0;
        var draw = (bool?)args["draw"] == true;
        if (string.IsNullOrEmpty(token) || token.Length > 64 || amount <= 0 || amount > int.MaxValue) return;
        Item character = null;
        GameObject safeObject = null;
        var generated = new List<Item>();
        byte[] result = null;
        bool Current() => generation == _generation && SharedHome.Active && peer.ConnectionState == ConnectionState.Connected;
        try
        {
            if (!Current() || draw && (amount > ATMPanel.MaxDrawAmount || EconomyManager.Money < amount)) return;
            var profile = new ES3File(Convert.FromBase64String((string)args["profile"]));
            var context = profile.Load<string>("context");
            var tree = profile.Load<ItemTreeData>("character");
            if (context == null || context.Length > 128 || !context.StartsWith("MainCharacterItemData") ||
                tree?.entries == null || tree.entries.Count == 0 || tree.entries.Count > 10000) return;
            character = await ItemTreeData.InstantiateAsync(tree);
            if (!Current() || !character) return;
            safeObject = new GameObject("SharedAtmSafe");
            var safe = safeObject.AddComponent<Inventory>();
            var safeData = profile.KeyExists(SharedHomeSafeInventory.ProfileKey)
                ? profile.Load<InventoryData>(SharedHomeSafeInventory.ProfileKey) : new InventoryData();
            safe.SetCapacity(Math.Max(safeData.capacity, safeData.entries.Count == 0 ? 0 : safeData.entries.Max(e => e.inventoryPosition) + 1));
            foreach (var entry in safeData.entries)
            {
                var item = await ItemTreeData.InstantiateAsync(entry.itemTreeData);
                if (!item || !safe.AddAt(item, entry.inventoryPosition)) throw new InvalidOperationException("ATM safe inventory could not load");
            }
            if (safeData.lockedIndexes != null) safe.lockedIndexes.AddRange(safeData.lockedIndexes);
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
            await UniTask.WaitUntil(() => !Current() || !(bool)AccessTools.Field(typeof(ATMPanel), "drawingMoney").GetValue(null));
            if (!Current()) return;
            // Native cash consumption sees the requesting player's inventories,
            // never the host's personal backpack or safe.
            _cashInventories = new[] { PlayerStorage.Inventory, character.Inventory, safe };
            bool paid;
            try
            {
                paid = draw ? new Cost(amount).Pay(true, false) : new Cost((EconomyManager.CashItemID, amount)).Pay(false);
            }
            finally { _cashInventories = null; }
            if (!paid) return;
            if (draw)
            {
                foreach (var cash in generated)
                    if (!ItemUtilities.AddAndMerge(character.Inventory, cash)) PlayerStorage.Push(cash, false);
            }
            else EconomyManager.Add(amount);
            profile.Save("character", ItemTreeData.FromItem(character));
            profile.Save(SharedHomeSafeInventory.ProfileKey, InventoryData.FromInventory(safe));
            result = profile.LoadRawBytes();
            SharedHomeLoadouts.Receive(peer, 6, result);
            SharedHome.MarkDirty();
            (AccessTools.Field(typeof(ItemUtilities), "OnPlayerItemOperation").GetValue(null) as Action)?.Invoke();
        }
        catch (Exception error) { Debug.LogError($"[SharedHome] ATM transaction failed: {error}"); }
        finally
        {
            foreach (var cash in generated) if (cash && !cash.ParentObject && !cash.IsBeingDestroyed) cash.DestroyTree();
            if (character) character.DestroyTree();
            if (safeObject) { safeObject.GetComponent<Inventory>().DestroyAllContent(); Object.Destroy(safeObject); }
            if (Current())
            {
                var writer = new NetDataWriter();
                writer.Put(token); writer.Put(result != null);
                if (result != null) writer.PutBytesWithLength(result);
                SharedHome.SendPrivate(12, writer.CopyData(), peer);
            }
        }
    }

    internal static bool TryFindCash(Predicate<Item> predicate, out List<Item> items)
    {
        items = null;
        if (_cashInventories == null) return false;
        items = _cashInventories.Where(inv => inv).SelectMany(inv => inv).Where(i => i && predicate(i)).ToList();
        return true;
    }

    internal static void Receive(byte[] bytes) => ApplyReply(bytes, _generation).Forget();

    private static async UniTaskVoid ApplyReply(byte[] bytes, int generation)
    {
        var reader = new NetPacketReader(bytes, 0, bytes.Length);
        var token = reader.GetString();
        if (!Busy || token != _token) return;
        var accepted = reader.GetBool();
        try
        {
            if (!accepted) return;
            var profile = new ES3File(reader.GetBytesWithLength());
            bool Current() => generation == _generation && _token == token && SharedHome.Guest;
            if (!Current()) return;
            var main = CharacterMainControl.Main;
            var heldSlot = main.CurrentHoldItemAgent?.Item?.PluggedIntoSlot?.Key;
            await SharedHomeEquipment.LoadInto(profile.Load<ItemTreeData>("character"), main.CharacterItem, Current);
            if (!Current()) return;
            if (heldSlot != null) main.ChangeHoldItem(main.CharacterItem.Slots.GetSlot(heldSlot)?.Content);
            await SharedHomeSafeInventory.Restore(profile, Current);
            if (!Current()) return;
            (AccessTools.Field(typeof(ItemUtilities), "OnPlayerItemOperation").GetValue(null) as Action)?.Invoke();
        }
        catch (Exception error)
        {
            accepted = false;
            Debug.LogError($"[SharedHome] ATM receipt could not apply: {error}");
            if (generation == _generation) NetService.Instance.StopNetwork();
        }
        finally
        {
            if (generation == _generation && _token == token)
            {
                Complete(accepted);
                if (accepted) SharedHomeLoadouts.Flush();
            }
        }
    }

    private static void Complete(bool result)
    {
        var completion = _completion;
        _completion = null;
        _token = null;
        completion?.TrySetResult(result);
    }

    internal static void Reset()
    {
        _generation++;
        Requests.Clear(); PendingPeers.Clear(); _running = false;
        Complete(false);
    }
}
