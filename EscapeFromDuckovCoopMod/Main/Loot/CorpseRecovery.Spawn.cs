using Duckov;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using UnityEngine.SceneManagement;

namespace EscapeFromDuckovCoopMod;

internal static partial class CorpseRecovery
{
    private static readonly HashSet<(NetPeer Owner, string Record, int Scene)> Reported = new();
    private static readonly HashSet<(NetPeer Owner, string Record)> PendingSpawns = new();
    private static readonly Dictionary<string, CorpseSpawnRequestRpc> PendingReports = new();
    private static float _nextReport;
    private static void PruneReported() => Reported.RemoveWhere(key => key.Scene != SceneManager.GetActiveScene().handle);

    internal static void Tick()
    {
        if (NetService.Instance?.networkStarted != true || NetService.Instance.IsServer ||
            !LevelManager.LevelInited || Time.unscaledTime < _nextReport) return;
        _nextReport = Time.unscaledTime + 1f;
        foreach (var rpc in PendingReports.Values.ToArray())
            if (rpc.SceneIndex == SceneManager.GetActiveScene().buildIndex) CoopTool.SendRpc(in rpc);
    }

    internal static bool TryRestore(DeadBodyManager.DeathInfo info, out UniTask task)
    {
        task = default;
        if (info?.itemTreeData == null || !LoadRecords().TryGetValue(RecordId(info), out var bytes)) return false;
        MarkSpawned(info);
        task = Restore(info, bytes);
        return true;
    }

    private static async UniTask Restore(DeadBodyManager.DeathInfo info, byte[] bytes)
    {
        if (NetService.Instance.IsServer)
            await SpawnContainer(null, RecordId(info), info.worldPosition, bytes);
        else
            ReportStoredBody(RecordId(info), info.worldPosition, bytes);
    }

    internal static void RegisterNativeBody(InteractableLootbox box, DeadBodyManager.DeathInfo info)
    {
        if (!box || info == null || NetService.Instance?.networkStarted != true) return;
        var id = RecordId(info);
        if (NetService.Instance.IsServer)
            RegisterServerBody(box, null, id);
        else
        {
            var bytes = ES3.Serialize(InventoryData.FromInventory(box.Inventory));
            ReportStoredBody(id, info.worldPosition, bytes);
            box.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(box.gameObject);
        }
    }

    private static void ReportStoredBody(string id, Vector3 position, byte[] bytes)
    {
        var rpc = new CorpseSpawnRequestRpc
        {
            RecordId = id, SceneIndex = SceneManager.GetActiveScene().buildIndex,
            Position = position, Data = bytes
        };
        PendingReports[id] = rpc;
        CoopTool.SendRpc(in rpc);
    }

    internal static void HandleSpawnRequest(NetPeer owner, CorpseSpawnRequestRpc rpc)
    {
        if (NetService.Instance?.IsServer != true || owner == null || string.IsNullOrEmpty(rpc.RecordId) ||
            rpc.Data == null || !PendingSpawns.Add((owner, rpc.RecordId))) return;
        SpawnReported(owner, rpc, _epoch).Forget();
    }

    private static async UniTask SpawnReported(NetPeer owner, CorpseSpawnRequestRpc rpc, int epoch)
    {
        var deadline = Time.unscaledTime + 60f;
        await UniTask.WaitUntil(() => epoch != _epoch || !NetService.Instance.networkStarted ||
            owner.ConnectionState != ConnectionState.Connected || Time.unscaledTime >= deadline ||
            (SceneManager.GetActiveScene().buildIndex == rpc.SceneIndex && LevelManager.LevelInited));
        PendingSpawns.Remove((owner, rpc.RecordId));
        if (epoch != _epoch) return;
        if (!NetService.Instance.networkStarted || owner.ConnectionState != ConnectionState.Connected ||
            SceneManager.GetActiveScene().buildIndex != rpc.SceneIndex || !LevelManager.LevelInited)
        {
            Debug.LogWarning($"[CORPSE] Recovery spawn could not enter scene {rpc.SceneIndex}");
            return;
        }
        if (Reported.Add((owner, rpc.RecordId, SceneManager.GetActiveScene().handle)))
            await SpawnContainer(owner, rpc.RecordId, rpc.Position, rpc.Data);
    }

    private static async UniTask SpawnContainer(NetPeer owner, string id, Vector3 position, byte[] bytes)
    {
        var data = ES3.Deserialize<InventoryData>(bytes);
        if (data.entries.Count == 0)
        {
            var empty = new CorpseInventoryRpc { RecordId = id, Revision = ++_revision, Data = bytes };
            if (owner == null) ApplySnapshot(empty);
            else CoopTool.SendRpcTo(owner, in empty);
            return;
        }
        var prefab = GameplayDataSettings.Prefabs.LootBoxPrefab_Tomb;
        var scene = SceneManager.GetActiveScene();
        var epoch = _epoch;
        var go = UnityEngine.Object.Instantiate(prefab.gameObject, position, Quaternion.identity);
        go.SetActive(false);
        var box = go.GetComponent<InteractableLootbox>();
        var inv = go.AddComponent<Inventory>();
        AccessTools.Field(typeof(InteractableLootbox), "inventoryReference").SetValue(box, inv);
        inv.SetCapacity(Math.Max(data.capacity, data.entries.Max(e => e.inventoryPosition) + 1));
        try
        {
            foreach (var entry in data.entries)
            {
                var item = await ItemTreeData.InstantiateAsync(entry.itemTreeData);
                if (epoch != _epoch || !NetService.Instance.networkStarted || SceneManager.GetActiveScene() != scene || !inv)
                {
                    if (item) UnityEngine.Object.Destroy(item.gameObject);
                    if (go) UnityEngine.Object.Destroy(go);
                    return;
                }
                if (!item || !inv.AddAt(item, entry.inventoryPosition))
                    throw new InvalidOperationException("Failed to restore corpse item");
            }
            DeadLootBox.Instance.Server_OnDeadLootboxSpawned(box, null, true, ownerId: NetService.Instance.GetPlayerId(owner));
            RegisterServerBody(box, owner, id);
            go.SetActive(true);
        }
        catch (Exception error)
        {
            if (go) UnityEngine.Object.Destroy(go);
            Debug.LogError($"[CORPSE] Restore failed: {error}");
        }
    }
}
