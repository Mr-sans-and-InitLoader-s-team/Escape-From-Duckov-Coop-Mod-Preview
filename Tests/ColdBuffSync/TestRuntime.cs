global using UnityEngine;
using Duckov.Buffs;

namespace UnityEngine
{
    public class GameObject
    {
        public CharacterMainControl Character = new();
        public T GetComponent<T>() where T : class => Character as T;
    }
}
namespace Duckov.Buffs
{
    public sealed class Buff
    {
        public int ID;
        public int CurrentLayers = 1;
        public int MaxLayers = 20;
        public float RemainingTime = 5;
    }
    public sealed class CharacterBuffManager { public List<Buff> Buffs = new(); }
}
namespace Duckov.Utilities
{
    public static class GameplayDataSettings
    {
        public static BuffData Buffs = new();
        public sealed class BuffData
        {
            public Buff Cold = new() { ID = 101 };
            public Buff SuperCold = new() { ID = 102 };
        }
    }
}
public sealed class CharacterMainControl
{
    public static CharacterMainControl Main;
    public bool IsMainCharacter => ReferenceEquals(Main, this);
    public Health Health = new();
    public float ColdProtection = 0;
    private readonly CharacterBuffManager _manager = new();
    public CharacterBuffManager GetBuffManager() => _manager;
    public void AddBuff(Buff prefab, CharacterMainControl from, int weapon)
    {
        var buff = _manager.Buffs.FirstOrDefault(b => b.ID == prefab.ID);
        if (buff == null) _manager.Buffs.Add(new Buff { ID = prefab.ID });
        else buff.CurrentLayers++;
    }
    public void RemoveBuff(int id, bool oneLayer) => _manager.Buffs.RemoveAll(b => b.ID == id);
}
public sealed class Health { public bool Invincible = false; }
public static class TimeOfDayController { public static float coldLevel = 0; }
namespace EscapeFromDuckovCoopMod
{
    public sealed class CoopPeer { public string Id; }
    public sealed class NetService
    {
        public static NetService Instance;
        public bool networkStarted = true;
        public bool IsServer;
        public string SelfId = "host";
        public Dictionary<CoopPeer, GameObject> remoteCharacters = new();
        public Dictionary<string, GameObject> clientRemoteCharacters = new();
        public bool IsSelfId(string id) => SelfId == id;
        public string GetSelfNetworkId() => SelfId;
        public string GetPlayerId(CoopPeer peer) => peer.Id;
        public bool TryGetPeerByPlayerId(string id, out CoopPeer peer)
        {
            peer = remoteCharacters.Keys.FirstOrDefault(p => p.Id == id);
            return peer != null;
        }
    }
    public static class Buff_ { public static bool ApplyingNetworkBuff; }
    public struct PlayerBuffReportRpc { public string TargetPlayerId; public int BuffId; public int? ColdLayers; }
    public struct PlayerBuffBroadcastRpc { public string PlayerId; public int BuffId; public int? ColdLayers; }
    public static class CoopTool
    {
        public static readonly List<object> Sent = new();
        public static void SendRpc<T>(T message, CoopPeer exclude = null) => Sent.Add(message);
    }
    public static class CoopLogSystem { public static void WriteNetworkDiagnostic(string message) { } }
}
