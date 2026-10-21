namespace ItemStatsSystem
{
    public class Item : UnityEngine.Object { public Data.ItemTreeData Tree = new(); public Inventory InInventory; }
    public class Inventory : UnityEngine.Object { public SortedDictionary<int, Item> Items = new(); public Item AttachedToItem; }
}
public static class SceneLoader { public static bool IsSceneLoading; }
namespace ItemStatsSystem.Data
{
    public class ItemTreeData
    {
        public int rootInstanceID;
        public int Type, Count = 1;
        public float Durability;
        public List<ItemTreeData> Children = new();
    }
    public class InventoryData
    {
        public class Entry { public int inventoryPosition; public ItemTreeData itemTreeData; }
        public int capacity = 64;
        public List<Entry> entries = new();
        public static InventoryData FromInventory(ItemStatsSystem.Inventory inv) => new()
        {
            entries = inv.Items.Select(kv => new Entry { inventoryPosition = kv.Key, itemTreeData = kv.Value.Tree }).ToList()
        };
    }
}
public static class ES3
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new() { IncludeFields = true };
    public static byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, Options);
    public static T Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data, Options);
}
public class InteractableBase : UnityEngine.Object { public GameObject gameObject = new(); }
public class InteractableLootbox : InteractableBase
{
    public static InteractableLootbox CreateFromItem(ItemStatsSystem.Item item, Vector3 position, Quaternion rotation,
        bool moveToMainScene, InteractableLootbox prefab, bool filterDontDropOnDead) => new();
    public ItemStatsSystem.Inventory Inventory = new();
    public class InteractEvent
    {
        private event Action<CharacterMainControl, InteractableBase> Listeners;
        public void AddListener(Action<CharacterMainControl, InteractableBase> action) => Listeners += action;
        public void Invoke(CharacterMainControl character, InteractableBase box) => Listeners?.Invoke(character, box);
    }
    public InteractEvent OnInteractStartEvent = new();
}
namespace Duckov
{
    public class DeadBodyManager : UnityEngine.Object
    {
        public static DeadBodyManager Instance;
        public class DeathInfo
        {
            public uint raidID;
            public bool touched;
            public bool spawned;
            public ItemStatsSystem.Data.ItemTreeData itemTreeData;
        }
        private List<DeathInfo> deaths = new();
        private List<DeathInfo> spawnedBodies = new();
        public void Add(DeathInfo record) => deaths.Add(record);
        public List<DeathInfo> Read() => deaths;
        private void Save() => Saves.SavesSystem.Saved = deaths.Select(d => new DeathInfo
        {
            raidID = d.raidID, touched = d.touched, itemTreeData = d.itemTreeData
        }).ToList();
    }
}
namespace Duckov.Scenes { public class MultiSceneCore { public static MultiSceneCore Instance = new(); } }
namespace Duckov.Utilities
{
    public static class GameplayDataSettings
    {
        public class PrefabSet { public InteractableLootbox LootBoxPrefab_Tomb = new(); }
        public static PrefabSet Prefabs = new();
    }
}
namespace Saves
{
    public static class SavesSystem
    {
        public static int CurrentSlot = 1;
        public static int Writes;
        public static List<Duckov.DeadBodyManager.DeathInfo> Saved;
        private static readonly Dictionary<(int Slot, string Key), object> Values = new();
        public static T Load<T>(string key) => Values.TryGetValue((CurrentSlot, key), out var data) ? (T)data : default;
        public static void Save<T>(string key, T data) => Values[(CurrentSlot, key)] = data;
        public static void SaveFile(bool writeSaveTime) => Writes++;
    }
}
namespace EscapeFromDuckovCoopMod
{
    public class HealthM
    {
        public static HealthM Instance = new();
        public int Reports;
        public void NotifyLocalDeadLootReady(Health health) => Reports++;
    }
    public static class DeadLootSpawnContext { public static CharacterMainControl InOnDead; }
    public class DeadLootBox
    {
        public static DeadLootBox Instance;
        public void Server_OnDeadLootboxSpawned(InteractableLootbox box, CharacterMainControl victim, bool tomb) { }
    }
    public static class CoopTool
    {
        public static readonly List<(CoopPeer Owner, CorpseInventoryRpc Message)> Sent = new();
        public static void SendRpcTo(CoopPeer peer, in CorpseInventoryRpc message) => Sent.Add((peer, message));
    }
    public static class COOPManager { public static LootNet LootNet = new(); }
    public class LootNet { public void Server_EnsureInventoryHook(ItemStatsSystem.Inventory inventory) { } }
    internal static partial class CorpseRecovery
    {
        private static readonly HashSet<string> Reported = new();
        private static readonly HashSet<string> PendingSpawns = new();
        private static readonly Dictionary<string, object> PendingReports = new();
        private static void PruneReported() { }
    }
}
