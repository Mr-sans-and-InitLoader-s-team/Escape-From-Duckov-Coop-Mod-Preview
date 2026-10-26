using Duckov;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Saves;

namespace EscapeFromDuckovCoopMod;

internal static partial class CorpseRecovery
{
    private const string SaveKey = "CoopCorpseRemainders";
    private static readonly System.Reflection.FieldInfo DeathsField = AccessTools.Field(typeof(DeadBodyManager), "deaths");
    private static readonly System.Reflection.FieldInfo SpawnedField = AccessTools.Field(typeof(DeadBodyManager), "spawnedBodies");
    private static readonly System.Reflection.MethodInfo SaveMethod = AccessTools.Method(typeof(DeadBodyManager), "Save");
    private static readonly Dictionary<Inventory, Body> ServerBodies = new();
    private static readonly HashSet<Inventory> Pending = new();
    private static readonly Dictionary<string, long> Received = new();
    private static long _revision;
    private static int _epoch;
    private static bool _flushScheduled;

    private sealed class Body
    {
        public NetPeer Owner;
        public string RecordId;
        public string PlayerIdentity;
        public byte[] LastData;
    }

    private static List<DeadBodyManager.DeathInfo> Deaths =>
        DeadBodyManager.Instance ? DeathsField.GetValue(DeadBodyManager.Instance) as List<DeadBodyManager.DeathInfo> : null;
    private static List<DeadBodyManager.DeathInfo> Spawned =>
        DeadBodyManager.Instance ? SpawnedField.GetValue(DeadBodyManager.Instance) as List<DeadBodyManager.DeathInfo> : null;
    private static string RecordId(DeadBodyManager.DeathInfo info) =>
        $"{SavesSystem.CurrentSlot}:{info.raidID}:{info.itemTreeData.rootInstanceID}";
    private static Dictionary<string, byte[]> LoadRecords() =>
        SavesSystem.Load<Dictionary<string, byte[]>>(SaveKey) ?? new();
    private static DeadBodyManager.DeathInfo FindRecord(string id) =>
        Deaths?.FirstOrDefault(d => d.itemTreeData != null && RecordId(d) == id);

    private static void MarkSpawned(DeadBodyManager.DeathInfo info)
    {
        if (Spawned != null && !Spawned.Contains(info)) Spawned.Add(info);
        info.spawned = true;
    }

    internal static string GetLocalRecordId(CharacterMainControl character)
    {
        var root = character.CharacterItem.GetInstanceID();
        var info = Deaths?.LastOrDefault(d => d.itemTreeData?.rootInstanceID == root);
        return info == null ? string.Empty : RecordId(info);
    }

    internal static bool HasRemaining(DeadBodyManager.DeathInfo info)
    {
        if (info?.itemTreeData == null || !LoadRecords().TryGetValue(RecordId(info), out var bytes)) return false;
        return ES3.Deserialize<InventoryData>(bytes).entries.Count > 0;
    }

    internal static void ApplySnapshot(CorpseInventoryRpc rpc)
    {
        var info = FindRecord(rpc.RecordId);
        if (info == null || rpc.Data == null || rpc.Data.Length == 0 ||
            (Received.TryGetValue(rpc.RecordId, out var last) && rpc.Revision <= last)) return;
        var data = ES3.Deserialize<InventoryData>(rpc.Data);
        if (data?.entries == null) return;
        Received[rpc.RecordId] = rpc.Revision;
        PendingReports.Remove(rpc.RecordId);
        var records = LoadRecords();
        records[rpc.RecordId] = rpc.Data;
        foreach (var key in records.Keys.Where(key => FindRecord(key) == null).ToArray()) records.Remove(key);
        SavesSystem.Save(SaveKey, records);
        // Managed bodies restore the saved container, never the original full character tree.
        // Native recovery stays disabled when the mod is absent to prevent duplicate loot.
        info.touched = true;
        var main = CharacterMainControl.Main;
        if (main && main.Health.IsDead && info.itemTreeData.rootInstanceID == main.CharacterItem.GetInstanceID())
            MarkSpawned(info);
        SaveMethod.Invoke(DeadBodyManager.Instance, Array.Empty<object>());
        SavesSystem.SaveFile(false);
        Debug.Log($"[CORPSE] Saved remainder record={rpc.RecordId} items={data.entries.Count} revision={rpc.Revision}");
    }

    internal static void RegisterServerBody(InteractableLootbox box, NetPeer owner, string recordId, string identity = null)
    {
        if (!box || !box.Inventory || string.IsNullOrEmpty(recordId)) return;
        foreach (var inv in ServerBodies.Keys.Where(inv => !inv).ToArray()) ServerBodies.Remove(inv);
        ServerBodies[box.Inventory] = new Body { Owner = owner, RecordId = recordId, PlayerIdentity = identity };
        if (owner == null && FindRecord(recordId) is { } info) MarkSpawned(info);
        COOPManager.LootNet.Server_EnsureInventoryHook(box.Inventory);
        Capture(box.Inventory);
    }

    internal static void QueueSnapshot(Inventory inventory)
    {
        if (SceneLoader.IsSceneLoading || NetService.Instance?.IsServer != true || !inventory || !ServerBodies.ContainsKey(inventory)) return;
        Pending.Add(inventory);
        if (_flushScheduled) return;
        _flushScheduled = true;
        FlushAfterMutations(_epoch).Forget();
    }

    internal static void QueueItemTreeSnapshot(Item item)
    {
        if (ServerBodies.Count == 0 || SceneLoader.IsSceneLoading || NetService.Instance?.IsServer != true || !item) return;
        var inventory = item.InInventory;
        if (!inventory) return;
        if (ServerBodies.ContainsKey(inventory)) QueueSnapshot(inventory);
        else if (inventory.AttachedToItem) QueueSnapshot(inventory.AttachedToItem.InInventory);
    }

    private static async UniTask FlushAfterMutations(int epoch)
    {
        await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
        if (epoch != _epoch) return;
        _flushScheduled = false;
        Flush();
    }

    internal static void Flush()
    {
        foreach (var inv in Pending.ToArray())
        {
            Pending.Remove(inv);
            if (inv) Capture(inv);
        }
    }

    private static void Capture(Inventory inventory)
    {
        if (!ServerBodies.TryGetValue(inventory, out var body)) return;
        var data = InventoryData.FromInventory(inventory);
        UpdateVisibility(inventory, data.entries.Count > 0);
        var bytes = ES3.Serialize(data);
        if (body.LastData != null && body.LastData.SequenceEqual(bytes)) return;
        body.LastData = bytes;
        var rpc = new CorpseInventoryRpc { RecordId = body.RecordId, Revision = ++_revision, Data = bytes };
        if (body.Owner == null) ApplySnapshot(rpc);
        else CoopTool.SendRpcTo(body.Owner, in rpc);
    }

    internal static void ResetNetwork()
    {
        Flush();
        foreach (var inv in ServerBodies.Keys.Where(inv => !inv).ToArray()) ServerBodies.Remove(inv);
    }

    internal static void StopNetwork()
    {
        ResetNetwork();
        _epoch++;
        _flushScheduled = false;
        Pending.Clear();
        ServerBodies.Clear();
        PendingSpawns.Clear();
        Received.Clear();
        PendingReports.Clear();
    }
}
