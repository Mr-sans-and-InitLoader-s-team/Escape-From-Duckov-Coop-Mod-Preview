using ItemStatsSystem;
using ItemStatsSystem.Data;
using Newtonsoft.Json.Linq;
using Saves;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeInventoryTransaction
{
    private static readonly Queue<(string Action, JObject Args, NetPeer Peer)> Requests = new();
    private static readonly HashSet<NetPeer> PendingPeers = new();
    private static UniTaskCompletionSource<bool> _completion;
    private static string _token;
    private static bool _running;
    private static int _generation;
    private static Item _costCharacter;
    private static Inventory[] _costInventories;
    internal static bool Busy => _completion != null;

    internal static UniTask<bool> Request(string action, object arguments)
    {
        if (Busy || !SharedHomeLoadouts.Ready || !SharedHomeSafeInventory.Ready) return UniTask.FromResult(false);
        var profile = Convert.ToBase64String(SharedHomeLoadouts.Report());
        _token = Guid.NewGuid().ToString("N");
        var completion = _completion = new UniTaskCompletionSource<bool>();
        var args = JObject.FromObject(arguments);
        args["token"] = _token;
        args["profile"] = profile;
        if (!SharedHome.Request(action, args)) Complete(false);
        return completion.Task;
    }

    internal static void Enqueue(string action, JObject args, NetPeer peer)
    {
        if (!PendingPeers.Add(peer)) return;
        Requests.Enqueue((action, args, peer));
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
                try { await Execute(request.Action, request.Args, request.Peer, generation); }
                finally { if (generation == _generation) PendingPeers.Remove(request.Peer); }
            }
        }
        finally { if (generation == _generation) _running = false; }
    }

    private static async UniTask Execute(string action, JObject args, NetPeer peer, int generation)
    {
        var token = (string)args["token"];
        if (string.IsNullOrEmpty(token) || token.Length > 64) return;
        Item character = null;
        GameObject safeObject = null;
        byte[] result = null;
        bool Current() => generation == _generation && SharedHome.Active && peer.ConnectionState == ConnectionState.Connected;
        try
        {
            if (!Current()) return;
            var profile = new ES3File(Convert.FromBase64String((string)args["profile"]));
            var context = profile.Load<string>("context");
            var tree = profile.Load<ItemTreeData>("character");
            var profileKey = SharedHomeLoadouts.TransactionKey(peer, context);
            if (profileKey == null ||
                tree?.entries == null || tree.entries.Count == 0 || tree.entries.Count > 10000) return;
            character = await ItemTreeData.InstantiateAsync(tree);
            if (!Current() || !character) return;
            safeObject = new GameObject("SharedTransactionSafe");
            var safe = safeObject.AddComponent<Inventory>();
            var safeData = profile.KeyExists(SharedHomeSafeInventory.ProfileKey)
                ? profile.Load<InventoryData>(SharedHomeSafeInventory.ProfileKey) : new InventoryData();
            safe.SetCapacity(Math.Max(safeData.capacity, safeData.entries.Count == 0 ? 0 : safeData.entries.Max(e => e.inventoryPosition) + 1));
            foreach (var entry in safeData.entries)
            {
                var item = await ItemTreeData.InstantiateAsync(entry.itemTreeData);
                if (!item || !safe.AddAt(item, entry.inventoryPosition)) throw new InvalidOperationException("Transaction safe inventory could not load");
            }
            if (safeData.lockedIndexes != null) safe.lockedIndexes.AddRange(safeData.lockedIndexes);
            var success = action == "craft"
                ? await SharedHomeCrafting.Execute(args, character, safe, Current)
                : await SharedHomeAtm.Execute(args, character, safe, Current);
            if (!success || generation != _generation || !SharedHome.Active) return;
            profile.Save("character", ItemTreeData.FromItem(character));
            profile.Save(SharedHomeSafeInventory.ProfileKey, InventoryData.FromInventory(safe));
            result = profile.LoadRawBytes();
            if (Current()) SharedHomeLoadouts.Receive(peer, 6, result);
            else SavesSystem.Save(profileKey, result);
            SharedHome.MarkDirty();
            (AccessTools.Field(typeof(ItemUtilities), "OnPlayerItemOperation").GetValue(null) as Action)?.Invoke();
        }
        catch (Exception error) { Debug.LogError($"[SharedHome] {action} transaction failed: {error}"); }
        finally
        {
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

    internal static T WithInventories<T>(Item character, Inventory safe, Func<T> action)
    {
        // Native material checks and payment run synchronously, before any awaited item creation.
        _costCharacter = character;
        _costInventories = new[] { PlayerStorage.Inventory, character.Inventory, safe };
        try { return action(); }
        finally { _costCharacter = null; _costInventories = null; }
    }

    internal static bool TryFindItems(Predicate<Item> predicate, out List<Item> items)
    {
        items = null;
        if (_costInventories == null) return false;
        items = _costInventories.Where(inv => inv).SelectMany(inv => inv.FindAll(predicate)).ToList();
        return true;
    }

    internal static bool ReturnDetachedItem(Item item, bool dontMerge)
    {
        if (!_costCharacter) return false;
        if (!_costCharacter.TryPlug(item, emptyOnly: true) &&
            !(dontMerge ? _costCharacter.Inventory.AddItem(item) : _costCharacter.Inventory.AddAndMerge(item)))
            ItemUtilities.SendToPlayerStorage(item);
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
            Debug.LogError($"[SharedHome] Inventory receipt could not apply: {error}");
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
