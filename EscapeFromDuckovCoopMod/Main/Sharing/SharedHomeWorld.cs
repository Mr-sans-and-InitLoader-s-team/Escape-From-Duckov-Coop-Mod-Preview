using Duckov.Economy;
using Duckov.Quests.Tasks;
using Duckov.Scenes;
using Duckov.Utilities;
using Saves;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeWorld
{
    private static readonly HashSet<string> Flags = new() { "MissleLuncherClosed" };
    internal static readonly HashSet<StockShop> Shops = new();
    internal static string[] FlagKeys => Flags.OrderBy(k => k).ToArray();
    internal static void AddFlags(IEnumerable<string> keys)
    {
        foreach (var key in keys)
            if (!string.IsNullOrWhiteSpace(key) && key.Length <= 256) Flags.Add(key);
    }

    internal static void Init()
    {
        LevelManager.OnLevelInitialized += RegisterFlags;
        MultiSceneCore.OnSetSceneVisited += _ => SharedHome.MarkDirty();
        Duckov.MasterKeys.MasterKeysManager.OnMasterKeyUnlocked += _ => SharedHome.MarkDirty();
        RegisterFlags();
    }
    private static void RegisterFlags()
    {
        if (GameplayDataSettings.QuestCollection != null)
            foreach (var task in GameplayDataSettings.QuestCollection.Where(q => q).SelectMany(q => q.Tasks).OfType<QuestTask_CheckSaveData>())
                if (!string.IsNullOrEmpty(task.SaveDataKey)) Flags.Add(task.SaveDataKey);
        foreach (var proxy in UnityEngine.Object.FindObjectsByType<SaveDataBoolProxy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!string.IsNullOrEmpty(proxy.key)) Flags.Add(proxy.key);
    }
    internal static bool ValueKey(string key) => Flags.Contains(key) || key.StartsWith("MultiSceneCore_Visited_") ||
        key.StartsWith("Count/") || key.StartsWith("CutScene_") || key.StartsWith("Breakable_");
    internal static void SetFlag(string key, bool value)
    {
        if (!ValueKey(key)) return;
        SavesSystem.Save(key, value);
        SharedHome.MarkDirty();
    }
    internal static void Reload(HashSet<string> changed)
    {
        Shops.RemoveWhere(s => !s);
        foreach (var shop in Shops)
            if (changed == null || changed.Contains("StockShop_" + shop.MerchantID)) SharedHomeSave.Load(shop);
        if (changed != null)
            foreach (var key in changed.Where(k => k.StartsWith("MultiSceneCore_Visited_")))
                (AccessTools.Field(typeof(MultiSceneCore), "OnSetSceneVisited").GetValue(null) as Action<string>)?
                    .Invoke(key.Substring("MultiSceneCore_Visited_".Length));
    }
}
