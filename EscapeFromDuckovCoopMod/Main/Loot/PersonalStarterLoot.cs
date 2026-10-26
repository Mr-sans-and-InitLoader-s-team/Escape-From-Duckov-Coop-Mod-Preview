using Duckov.Scenes;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Data;

namespace EscapeFromDuckovCoopMod;

internal static class PersonalStarterLoot
{
    private const string SceneId = "Level_SurivalChallenge_Main";
    private static readonly Dictionary<Inventory, Supply> Supplies = new();
    private static readonly Dictionary<int, byte[]> Remainders = new();
    private static MultiSceneCore _serverCore, _clientCore;
    private static string _serverWorld, _clientWorld;
    private sealed class Supply
    {
        internal InteractableLootbox Box;
        internal bool Ready, Restoring;
        internal int Key;
    }
    internal static void Observe(InteractableLootbox box, Inventory inventory)
    {
        if (!box || !inventory || box._overrideInteractNameKey != "UI_LootBox_Starter" ||
            MultiSceneCore.MainSceneID != SceneId || Supplies.ContainsKey(inventory)) return;
        foreach (var stale in Supplies.Keys.Where(i => !i).ToArray()) Supplies.Remove(stale);
        var supply = new Supply { Box = box, Key = LootManager.ComputeLootKeyFromPos(box.transform.position) };
        Supplies[inventory] = supply;
        inventory.onContentChanged += Changed;
        if (_clientCore && _clientCore == MultiSceneCore.Instance) Restore(inventory, supply, _clientWorld).Forget();
    }
    internal static bool IsSupply(Inventory inventory) => inventory && Supplies.TryGetValue(inventory, out var supply) && supply.Box;
    internal static bool IsPersonal(Inventory inventory) => NetService.Instance?.networkStarted == true && IsSupply(inventory);
    internal static bool IsReady(Inventory inventory) => !IsPersonal(inventory) || NetService.Instance.IsServer ||
        _clientCore == MultiSceneCore.Instance && Supplies[inventory].Ready;
    private static void Changed(Inventory inventory, int _)
    {
        if (NetService.Instance?.networkStarted != true || NetService.Instance.IsServer ||
            !Supplies.TryGetValue(inventory, out var supply) || !supply.Ready || supply.Restoring ||
            _clientCore != MultiSceneCore.Instance) return;
        Remainders[supply.Key] = ES3.Serialize(InventoryData.FromInventory(inventory));
    }
    internal static void SendContext(NetPeer peer)
    {
        var core = MultiSceneCore.Instance;
        if (!core || MultiSceneCore.MainSceneID != SceneId) return;
        if (_serverCore != core) { _serverCore = core; _serverWorld = Guid.NewGuid().ToString("N"); }
        var rpc = new StarterLootContextRpc { WorldId = _serverWorld };
        CoopTool.SendRpcTo(peer, in rpc);
    }
    internal static void ReceiveContext(RpcContext context, StarterLootContextRpc rpc)
    {
        if (context.IsServer || !MultiSceneCore.Instance || MultiSceneCore.MainSceneID != SceneId || string.IsNullOrEmpty(rpc.WorldId)) return;
        var changed = _clientWorld != rpc.WorldId || _clientCore != MultiSceneCore.Instance;
        if (_clientWorld != rpc.WorldId) { Remainders.Clear(); _clientWorld = rpc.WorldId; }
        _clientCore = MultiSceneCore.Instance;
        foreach (var pair in Supplies.Where(p => p.Key && p.Value.Box && p.Value.Box.gameObject.scene.isLoaded).ToArray())
        {
            if (changed) pair.Value.Ready = false;
            Restore(pair.Key, pair.Value, rpc.WorldId).Forget();
        }
    }
    private static async UniTaskVoid Restore(Inventory inventory, Supply supply, string world)
    {
        if (supply.Restoring || supply.Ready) return;
        supply.Restoring = true;
        try
        {
            await UniTask.WaitUntil(() => !inventory || world != _clientWorld ||
                !inventory.Loading && LevelManager.LevelInited);
            if (!inventory || world != _clientWorld) return;
            if (Remainders.TryGetValue(supply.Key, out var bytes))
            {
                var data = ES3.Deserialize<InventoryData>(bytes);
                inventory.DestroyAllContent();
                inventory.SetCapacity(data.capacity);
                foreach (var entry in data.entries)
                {
                    var item = await ItemTreeData.InstantiateAsync(entry.itemTreeData);
                    if (!inventory || world != _clientWorld)
                    {
                        if (item) UnityEngine.Object.Destroy(item.gameObject);
                        return;
                    }
                    if (item && !inventory.AddAt(item, entry.inventoryPosition)) item.DestroyTree();
                }
            }
            if (!inventory || world != _clientWorld) return;
            supply.Ready = true;
        }
        finally { supply.Restoring = false; }
    }
}
