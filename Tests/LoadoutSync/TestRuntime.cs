global using EscapeFromDuckovCoopMod;

namespace EscapeFromDuckovCoopMod;

internal static class UniTask
{
    private static readonly List<TaskCompletionSource> Waiters = new();
    private static readonly List<Task> Pending = new();
    public static Task NextFrame()
    {
        var completion = new TaskCompletionSource();
        Waiters.Add(completion);
        return completion.Task;
    }
    public static void Void(Func<Task> action) => Pending.Add(action());
    public static async Task AdvanceFrame()
    {
        var waiters = Waiters.ToArray(); Waiters.Clear();
        foreach (var waiter in waiters) waiter.SetResult();
        await Task.WhenAll(Pending); Pending.Clear();
    }
    public static void Forget(this Task task) => task.GetAwaiter().GetResult();
}
internal sealed class GameObject
{
    public readonly CharacterMainControl Character = new();
    public T GetComponent<T>() where T : class => Character as T;
}
internal sealed class CharacterMainControl { public CharacterModel characterModel = new(); }
internal sealed class CharacterModel { }
internal sealed class CoopPeer { }
internal sealed class PlayerStatus
{
    public readonly List<EquipmentSyncData> EquipmentList = new();
    public readonly List<WeaponSyncData> WeaponList = new();
}
internal sealed class EquipmentSyncData { public int SlotHash; public string ItemId; }
internal sealed class WeaponSyncData { public int SlotHash; public string ItemId; public int Snapshot; }
internal sealed class NetService
{
    public static NetService Instance;
    public bool networkStarted = true;
    public bool IsServer;
    public readonly Dictionary<CoopPeer, GameObject> remoteCharacters = new();
    public readonly Dictionary<CoopPeer, PlayerStatus> playerStatuses = new();
    public readonly Dictionary<string, GameObject> clientRemoteCharacters = new();
    public readonly Dictionary<string, PlayerStatus> clientPlayerStatuses = new();
}
internal sealed class RecordingApply
{
    public readonly List<string> Items = new();
    public Task ApplyEquipmentUpdate(CoopPeer peer, int slot, string item) { Items.Add(item); return Task.CompletedTask; }
    public Task ApplyWeaponUpdate(CoopPeer peer, int slot, string item, int snapshot) { Items.Add(item); return Task.CompletedTask; }
    public Task ApplyEquipmentUpdate_Client(string id, int slot, string item) { Items.Add(item); return Task.CompletedTask; }
    public Task ApplyWeaponUpdate_Client(string id, int slot, string item, int snapshot) { Items.Add(item); return Task.CompletedTask; }
}
internal static class COOPManager
{
    public static RecordingApply HostPlayer_Apply = new();
    public static RecordingApply ClientPlayer_Apply = new();
}
