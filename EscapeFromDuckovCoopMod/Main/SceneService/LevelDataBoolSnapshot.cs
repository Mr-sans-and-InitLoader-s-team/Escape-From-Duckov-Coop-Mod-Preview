using Duckov.Scenes;

namespace EscapeFromDuckovCoopMod;

internal static class LevelDataBoolSnapshot
{
    internal static void Send(NetPeer peer)
    {
        var core = MultiSceneCore.Instance;
        if (!core || core.SceneInfo == null) return;
        var personalKeys = new HashSet<int>();
        foreach (var entry in CoopSyncDatabase.Loot.Entries.Where(e => LootboxDetectUtil.IsPrivateInventory(e.Inventory)))
        {
            personalKeys.Add(entry.PositionKey);
            if (entry.Lootbox) personalKeys.Add((int)AccessTools.Method(typeof(InteractableBase), "GetKey").Invoke(entry.Lootbox, null));
            if (entry.Loader) personalKeys.Add((int)AccessTools.Method(typeof(Duckov.Utilities.LootBoxLoader), "GetKey").Invoke(entry.Loader, null));
        }
        var values = core.inLevelData.Where(p => p.Value is bool && !personalKeys.Contains(p.Key)).ToArray();
        for (var offset = 0; offset < values.Length; offset += 64)
        {
            var entries = values.Skip(offset).Take(64).ToArray();
            var rpc = new EnvLevelDataBoolSnapshotRpc
            {
                SceneId = core.SceneInfo.ID,
                Keys = entries.Select(p => p.Key).ToArray(),
                Values = entries.Select(p => (bool)p.Value).ToArray()
            };
            CoopTool.SendRpcTo(peer, in rpc);
        }
    }
    internal static void Apply(RpcContext context, EnvLevelDataBoolSnapshotRpc rpc)
    {
        var core = MultiSceneCore.Instance;
        if (context.IsServer || !core || core.SceneInfo?.ID != rpc.SceneId) return;
        for (var i = 0; i < rpc.Keys.Length; i++) core.inLevelData[rpc.Keys[i]] = rpc.Values[i];
    }
}
