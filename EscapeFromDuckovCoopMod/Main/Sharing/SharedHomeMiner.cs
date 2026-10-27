using Duckov.Bitcoins;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Saves;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeMiner
{
    private static Item _watched;
    private static int _version;
    private static float _nextPublish;
    private static void Changed(Item _) => SharedHome.MarkDirty();

    internal static void Track(BitcoinMiner miner)
    {
        var item = SharedHome.Active ? miner.Item : null;
        if (_watched != item)
        {
            if (_watched) _watched.onItemTreeChanged -= Changed;
            _watched = item;
            if (_watched) _watched.onItemTreeChanged += Changed;
        }
        if (!item || Time.unscaledTime < _nextPublish) return;
        _nextPublish = Time.unscaledTime + 10f;
        SharedHome.MarkDirty();
    }

    internal static void Reload(HashSet<string> changed)
    {
        if (changed != null && !changed.Contains(BitcoinMiner.SaveKey)) return;
        Refresh(BitcoinMiner.Instance, ++_version).Forget();
    }

    private static async UniTaskVoid Refresh(BitcoinMiner miner, int version)
    {
        Item item = null;
        try
        {
            await UniTask.WaitUntil(() => !miner || miner.Initialized && !miner.Loading);
            if (!miner || version != _version) return;
            var cache = ES3File.cachedFiles[SavesSystem.CurrentFilePath];
            var data = cache.KeyExists(BitcoinMiner.SaveKey) ? cache.Load(BitcoinMiner.SaveKey) : null;
            if (data == null) return;
            var type = data.GetType();
            item = await ItemTreeData.InstantiateAsync((ItemTreeData)AccessTools.Field(type, "itemData").GetValue(data));
            if (!miner || version != _version || !item) return;
            var snapshot = ItemTool.MakeSnapshot(item);
            SharedHome.Apply(() =>
            {
                ItemTool.ApplySnapshot(miner.Item, snapshot);
                foreach (var name in new[] { "work", "cachedPerformance", "lastUpdateDateTimeRaw" })
                    AccessTools.Field(typeof(BitcoinMiner), name).SetValue(miner, AccessTools.Field(type, name).GetValue(data));
            });
        }
        finally { if (item) item.DestroyTree(); }
    }
}
