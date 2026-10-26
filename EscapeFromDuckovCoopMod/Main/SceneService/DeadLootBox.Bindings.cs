namespace EscapeFromDuckovCoopMod;

public partial class DeadLootBox
{
    private readonly Dictionary<int, InteractableLootbox> _clientSpawns = new();

    internal void UpdateCorpseOwner(ItemStatsSystem.Inventory inventory, NetPeer owner)
    {
        if (!CoopSyncDatabase.Loot.TryGetByInventory(inventory, out var entry) || !entry.Lootbox ||
            !_serverSpawns.TryGetValue(entry.Lootbox, out var rpc)) return;
        rpc.OwnerId = Service.GetPlayerId(owner);
        _serverSpawns[entry.Lootbox] = rpc;
        rpc.PlayerId = string.Empty;
        CoopTool.SendRpcTo(owner, in rpc);
    }

    internal void RebindPersistentLoot()
    {
        foreach (var pair in _serverSpawns.ToArray())
        {
            if (!pair.Key) { _serverSpawns.Remove(pair.Key); continue; }
            Bind(pair.Key, pair.Value.LootUid, true);
        }
        foreach (var pair in _clientSpawns.ToArray())
        {
            if (!pair.Value) { _clientSpawns.Remove(pair.Key); continue; }
            Bind(pair.Value, pair.Key, false);
        }
    }
    private void Bind(InteractableLootbox box, int uid, bool server)
    {
        if (!Duckov.Scenes.MultiSceneCore.Instance || !box.Inventory || !LootManager.Instance) return;
        var inventory = box.Inventory;
        CoopSyncDatabase.Loot.Register(box, inventory, uid);
        if (server)
        {
            LootManager.Instance._srvLootByUid[uid] = inventory;
            COOPManager.LootNet?.Server_EnsureInventoryHook(inventory);
        }
        else LootManager.Instance._cliLootByUid[uid] = inventory;
    }
}
