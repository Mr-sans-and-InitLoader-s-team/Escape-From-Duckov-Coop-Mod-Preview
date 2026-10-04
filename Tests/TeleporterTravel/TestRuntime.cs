global using UniTask = System.Threading.Tasks.Task;
global using UnityEngine;
using System.Reflection;
using Duckov.Economy;

namespace UnityEngine
{
    public static class Time { public static float unscaledTime; }
    public static class Debug { public static void LogError(object message) => Console.Error.WriteLine(message); }
}
namespace ItemStatsSystem { public sealed class Item { } }
public static class ItemUtilities
{
    public static long Items = 5;
    public static long GetItemCount(int id) => Items;
}
namespace Duckov.Economy
{
    public static class EconomyManager { public static long Money = 100, Cash = 0; }
    public struct Cost
    {
        public struct ItemEntry { public int id; public long amount; }
        public long money;
        public ItemEntry[] items;
        public static bool TaskPending => false;
        public static bool FailAfterMoney;
        public static int Pays, Refunds;
        public bool Enough => EconomyManager.Money + EconomyManager.Cash >= money && (items ?? Array.Empty<ItemEntry>()).All(i => ItemUtilities.Items >= i.amount);
        public bool IsFree => money == 0 && (items == null || items.Length == 0);
        public bool Pay()
        {
            Pays++;
            if (!Enough) return false;
            EconomyManager.Money -= money;
            if (FailAfterMoney) return false;
            foreach (var item in items ?? Array.Empty<ItemEntry>()) ItemUtilities.Items -= item.amount;
            return true;
        }
        internal Task Return(bool buffer, bool inventory, int factor, List<ItemStatsSystem.Item> generated)
        {
            Refunds++;
            EconomyManager.Money += money;
            foreach (var item in items) ItemUtilities.Items += item.amount;
            return Task.CompletedTask;
        }
    }
}
namespace Duckov.UI
{
    public sealed class MapSelectionEntry
    {
        public string SceneID = "map";
        public int BeaconIndex = 2;
        public bool ConditionsSatisfied = true;
        public Cost Cost = new() { money = 10, items = new[] { new Cost.ItemEntry { id = 1, amount = 3 } } };
    }
    public sealed class MapSelectionView
    {
        public static MapSelectionView Instance = new();
        public MapSelectionEntry Entry = new();
        public T[] GetComponentsInChildren<T>(bool includeInactive)
        {
            if (!includeInactive) throw new Exception("Hidden teleporter entries must be included");
            return new[] { (T)(object)Entry };
        }
    }
}
public sealed class CharacterMainControl
{
    public static CharacterMainControl Main = new();
    public Health Health = new();
}
public sealed class Health { public bool IsDead = false; }
public static class SceneLoader { public static bool IsSceneLoading = false; }
namespace EscapeFromDuckovCoopMod
{
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    }
    public static class TaskExtension { public static void Forget(this Task task) => task.GetAwaiter().GetResult(); }
    public enum ConnectionState { Connected }
    public sealed class CoopPeer { public string Id = "client"; public ConnectionState ConnectionState => ConnectionState.Connected; }
    public sealed class PlayerStatus { public string PlayerName = "Client"; }
    public sealed class NetService
    {
        public static NetService Instance = new();
        public bool IsServer = true, networkStarted = true;
        public CoopPeer connectedPeer;
        public Dictionary<CoopPeer, PlayerStatus> playerStatuses = new();
        public string GetSelfNetworkId() => "host";
        public string GetPlayerId(CoopPeer peer) => peer.Id;
        public string ResolveLocalPlayerName() => "Host";
        public bool TryGetPeerByPlayerId(string id, out CoopPeer peer) { peer = playerStatuses.Keys.FirstOrDefault(p => p.Id == id); return peer != null; }
    }
    public readonly struct RpcContext
    {
        public RpcContext(CoopPeer sender) => Sender = sender;
        public NetService Service => NetService.Instance;
        public bool IsServer => Service.IsServer;
        public CoopPeer Sender { get; }
    }
    public enum TeleporterFlowKind : byte { Request, Check, Checked, ShowDeparture, Confirmed, Pay, Paid, Cancel, Aborted }
    public struct TeleporterFlowRpc
    {
        public TeleporterFlowKind Kind;
        public string Token, SceneId, Reason, PlayerName;
        public int BeaconIndex;
        public bool Success;
    }
    public static class CoopTool
    {
        public static readonly List<TeleporterFlowRpc> Messages = new();
        public static void SendRpc<T>(in T value) => Messages.Add((TeleporterFlowRpc)(object)value);
    }
    public static class CoopLocalization
    {
        public static string Language = "en-US";
        public static string Get(string key, params object[] args)
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Localization", Language + ".json")));
            var entry = json.RootElement.GetProperty("translations").EnumerateArray().First(e => e.GetProperty("key").GetString() == key);
            return string.Format(entry.GetProperty("value").GetString(), args);
        }
    }
    public sealed class SceneNet
    {
        public static SceneNet Instance = new();
        public bool sceneVoteActive;
        public int Loads;
        public void Host_BeginSceneVote_Simple(string id, string curtain, bool evac, bool save, bool location, string name, int beacon) { }
        public void StartValidatedTeleporterLoad() => Loads++;
        public void ResetTeleporterVote() => sceneVoteActive = false;
    }
    public static class TravelNotice
    {
        public static string Message;
        public static void Show(string message) => Message = message;
    }
    public static class TeleporterDepartureView
    {
        public static int Shows;
        public static bool WasClosed;
        public static bool Show(Duckov.UI.MapSelectionEntry entry) { Shows++; return true; }
        public static void Close() { }
        public static void LockCancellation() { }
    }
}
