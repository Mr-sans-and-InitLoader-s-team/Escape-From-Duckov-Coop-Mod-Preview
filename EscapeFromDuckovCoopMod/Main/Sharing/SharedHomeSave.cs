using Duckov;
using Duckov.BlackMarkets;
using Duckov.Buildings;
using Duckov.Economy;
using Duckov.Endowment;
using Duckov.NoteIndexs;
using Duckov.Utilities;
using Duckov.PerkTrees;
using Duckvo.Beacons;
using Saves;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeSave
{
    private static ES3File _personal;
    private static ES3File _personalGlobal;
    private static string _path;
    private static readonly Dictionary<string, byte[]> LastValues = new();
    private static readonly Dictionary<string, byte[]> PublishedValues = new();
    internal static bool Capturing { get; private set; }
    internal static bool OverlayActive => _personal != null;
    private static readonly HashSet<string> Keys = new()
    {
        "EXP_Value", "QuestData", "BuildingData", "BeaconManager", "Crafting/UnlockedFormulaIDs",
        "EconomyData", "BlackMarket_Data", "CommonVariablesData", "HasFished", "GamingConsoleData", "BitcoinMiner_Data",
        "MasterKeys", "BaseSceneOverride", "GameRulesManager_RuleIndex", "Rule_Custom", "CustomDifficultyMarker"
    };
    internal static bool SharedKey(string key) => Keys.Contains(key) || key.StartsWith("PerkTree_") ||
        key.StartsWith("PlayerStorage") || key.StartsWith("Inventory/PlayerStorage") || key.StartsWith("Item/Showcase_") || key.StartsWith("StockShop_") ||
        key.StartsWith("ConstructionSite_") || key.StartsWith("NoteIndex") || SharedHomeWorld.ValueKey(key);

    internal static void ResetPublished() => PublishedValues.Clear();
    internal static byte[] Capture(bool full = false)
    {
        Capturing = true;
        try
        {
            SavesSystem.CollectSaveData();
            if (!ES3File.cachedFiles.TryGetValue(SavesSystem.CurrentFilePath, out var source))
                throw new InvalidOperationException("Shared home save cache is unavailable");
            var selected = new ES3File(false);
            void Add(string key, object value)
            {
                var single = new ES3File(false);
                single.Save(key, value);
                var bytes = single.LoadRawBytes();
                if (!full && PublishedValues.TryGetValue(key, out var previous) && previous.SequenceEqual(bytes)) return;
                selected.Save(key, value);
                if (!full) PublishedValues[key] = bytes;
            }
            foreach (var key in source.GetKeys().Where(SharedKey))
            {
                // Live warehouse changes already use transactional loot packets.
                if (!full && key.StartsWith("Inventory/PlayerStorage")) continue;
                Add(key, source.Load(key));
            }
            var unlocks = new Dictionary<string, bool>();
            if (EndowmentManager.Instance)
                foreach (var entry in EndowmentManager.Instance.Entries)
                    unlocks[$"Endowment_Unlock_R_{entry.Index}"] = EndowmentManager.GetEndowmentUnlocked(entry.Index);
            unlocks["Difficulty/RageUnlocked"] = SavesSystem.LoadGlobal<bool>("Difficulty/RageUnlocked");
            Add("SharedHomeGlobalUnlocks", unlocks);
            Add("SharedHomeFlagKeys", SharedHomeWorld.FlagKeys);
            if (full) selected.Save("SharedHomeFull", true);
            return selected.GetKeys().Length == 0 ? null : selected.LoadRawBytes();
        }
        finally { Capturing = false; }
    }
    internal static byte[] Combine(byte[] before, byte[] after)
    {
        if (after == null || after.Length == 0) return before;
        if (before == null || before.Length == 0) return after;
        var combined = new ES3File(before);
        var incoming = new ES3File(after);
        foreach (var key in incoming.GetKeys()) combined.Save(key, incoming.Load(key));
        return combined.LoadRawBytes();
    }
    internal static void Begin()
    {
        if (_personal != null) return;
        SavesSystem.KeyExisits("Created");
        // Preserve the current unsaved personal state as well as the disk save.
        if (LevelManager.Instance && LevelManager.LevelInited) LevelManager.Instance.NotifySaveBeforeLoadScene(false);
        else SavesSystem.CollectSaveData();
        _path = SavesSystem.CurrentFilePath;
        _personal = ES3File.cachedFiles[_path];
        ES3File.cachedFiles[_path] = new ES3File(_personal.LoadRawBytes(), _personal.settings);
        SharedHomeSafeInventory.ClearPersonal(ES3File.cachedFiles[_path]);
        SavesSystem.LoadGlobal<bool>("SharedHome_CacheProbe");
        if (ES3File.cachedFiles.TryGetValue(SavesSystem.GlobalSaveDataFilePath, out _personalGlobal))
            ES3File.cachedFiles[SavesSystem.GlobalSaveDataFilePath] = new ES3File(_personalGlobal.LoadRawBytes(), _personalGlobal.settings);
    }
    internal static void Apply(byte[] bytes, bool reload = true)
    {
        if (bytes == null || bytes.Length == 0) return;
        Begin();
        var incoming = new ES3File(bytes);
        var target = ES3File.cachedFiles[_path];
        if (incoming.KeyExists("SharedHomeFlagKeys"))
            SharedHomeWorld.AddFlags(incoming.Load<string[]>("SharedHomeFlagKeys"));
        var full = incoming.KeyExists("SharedHomeFull");
        if (full)
            foreach (var key in target.GetKeys().Where(SharedKey)) target.DeleteKey(key);
        var changed = new HashSet<string>();
        foreach (var key in incoming.GetKeys())
        {
            if (key == "SharedHomeFull") continue;
            if (key == "SharedHomeFlagKeys") continue;
            if (key == "SharedHomeGlobalUnlocks")
            {
                var global = ES3File.cachedFiles[SavesSystem.GlobalSaveDataFilePath];
                foreach (var pair in incoming.Load<Dictionary<string, bool>>(key))
                    if (pair.Key.StartsWith("Endowment_Unlock_R_") || pair.Key == "Difficulty/RageUnlocked") global.Save(pair.Key, pair.Value);
                continue;
            }
            if (!SharedKey(key)) throw new InvalidOperationException("Unexpected shared save key: " + key);
            var value = incoming.Load(key);
            var single = new ES3File(false);
            single.Save(key, value);
            var serialized = single.LoadRawBytes();
            if (!full && LastValues.TryGetValue(key, out var previous) && previous.SequenceEqual(serialized)) continue;
            // Native save types are retained by ES3, including private save-data classes.
            target.Save(key, value);
            LastValues[key] = serialized;
            changed.Add(key);
        }
        if (reload) Reload(full ? null : changed);
    }
    internal static void Restore()
    {
        if (_personal == null) return;
        ES3File.cachedFiles[_path] = _personal;
        if (_personalGlobal != null) ES3File.cachedFiles[SavesSystem.GlobalSaveDataFilePath] = _personalGlobal;
        _personal = null;
        _personalGlobal = null;
        LastValues.Clear();
        SharedHome.Reloading = true;
        var defer = SceneLoader.IsSceneLoading;
        if (!defer) SharedHome.Apply(() => Reload(null));
        FinishRestore(defer).Forget();
    }
    private static async UniTaskVoid FinishRestore(bool defer)
    {
        try
        {
            if (defer)
            {
                await UniTask.WaitWhile(() => SceneLoader.IsSceneLoading);
                SharedHome.Apply(() => Reload(null));
            }
            await UniTask.WaitUntil(() => !PlayerStorage.Loading);
        }
        finally { SharedHome.Reloading = false; }
    }
    internal static void Load(object target, string method = "Load")
    {
        if (target == null || target is Object obj && !obj) return;
        var result = AccessTools.Method(target.GetType(), method, Type.EmptyTypes)?.Invoke(target, null);
        if (result is UniTask task) task.Forget();
    }
    private static void Reload(HashSet<string> changed)
    {
        bool Has(string key) => changed == null || changed.Contains(key);
        if (Has("EXP_Value"))
        {
            var previous = EXPManager.EXP;
            var previousLevel = EXPManager.Level;
            Load(EXPManager.Instance);
            if (EXPManager.EXP != previous) EXPManager.onExpChanged?.Invoke(EXPManager.EXP);
            if (EXPManager.Level != previousLevel)
                AccessTools.Method(typeof(EXPManager), "OnLevelChanged").Invoke(null, new object[] { previousLevel, EXPManager.Level });
        }
        if (PerkTreeManager.Instance)
            foreach (var tree in PerkTreeManager.Instance.perkTrees)
                if (tree && Has("PerkTree_" + tree.ID)) SharedHomePerks.Reload(tree);
        if (Has("QuestData")) SharedHomeQuests.Reload();
        if (Has("EconomyData"))
        {
            var previous = EconomyManager.Money;
            Load(EconomyManager.Instance);
            if (EconomyManager.Money != previous)
                (AccessTools.Field(typeof(EconomyManager), "OnMoneyChanged").GetValue(null) as Action<long, long>)?.Invoke(previous, EconomyManager.Money);
        }
        if (Has("Crafting/UnlockedFormulaIDs")) Load(CraftingManager.Instance);
        if (Has("BeaconManager")) Load(BeaconManager.Instance);
        if (Has("NoteIndexData")) Load(NoteIndex.Instance);
        if (Has("MasterKeys")) Load(Duckov.MasterKeys.MasterKeysManager.Instance);
        if (Has("GameRulesManager_RuleIndex") || Has("Rule_Custom"))
        {
            Load(Duckov.Rules.GameRulesManager.Instance, "ReloadCustomRuleSet");
            Duckov.Rules.GameRulesManager.NotifyRuleChanged();
        }
        SharedHomeWorld.Reload(changed);
        if (Has("CommonVariablesData"))
            Load(AccessTools.Field(typeof(CommonVariables), "instance").GetValue(null));
        if (Has("Endowment_SelectedIndex")) Load(EndowmentManager.Instance, "ApplyCurrentEndowment");
        if (Has("BlackMarket_Data")) Load(BlackMarket.Instance);
        if (Has("BuildingData"))
            SharedHomeBuildings.ApplyLayout();
        SharedHomeDisplays.Reload(changed);
        SharedHomeConsoles.Reload(changed);
        SharedHomeMiner.Reload(changed);
        if (changed == null || changed.Any(k => k.StartsWith("PlayerStorage_Buffer")))
        {
            PlayerStorageBuffer.LoadBuffer();
            (AccessTools.Field(typeof(PlayerStorage), "OnTakeBufferItem").GetValue(null) as Action)?.Invoke();
        }
        // Warehouse replicas are updated by the existing authoritative inventory protocol.
        if (changed == null && !SharedHome.Guest) Load(PlayerStorage.Instance);
        if (changed == null && !SharedHome.Guest) SharedHomeLoadouts.ReloadDeaths();
        if (changed == null && !SharedHome.Guest && LevelManager.Instance?.MainCharacter) LevelManager.Instance.RefreshMainCharacterFace();
    }
}
