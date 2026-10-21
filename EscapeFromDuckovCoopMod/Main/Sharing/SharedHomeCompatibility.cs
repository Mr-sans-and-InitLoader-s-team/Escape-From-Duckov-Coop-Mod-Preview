using System.IO;
using System.Security.Cryptography;
using System.Text;
using Duckov.Modding;
using Newtonsoft.Json;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeCompatibility
{
    private sealed class Pending
    {
        internal float Deadline = Time.unscaledTime + 60f;
        internal bool Rejected;
    }
    private sealed class Failure
    {
        public string Player;
        public List<SharedHomeCompatibilityIssue> Issues;
    }
    private static readonly Dictionary<NetPeer, Pending> Waiting = new();
    private static readonly HashSet<NetPeer> Accepted = new();
    private static bool _clientReady;
    private static float _clientDeadline;
    internal static bool ClientReady => _clientReady;
    internal static bool Allows(NetPeer peer) => !SharedHome.Enabled ||
        (NetService.Instance.IsServer ? peer != null && Accepted.Contains(peer) : _clientReady);

    internal static SharedHomeManifest Capture()
    {
        var manifest = new SharedHomeManifest
        {
            Game = Duckov.GameMetaData.Instance ? Duckov.GameMetaData.Instance.Version.ToString() : string.Empty,
            Coop = BuildInfo.ModVersion,
            Player = NetService.Instance?.localPlayerStatus?.PlayerName ?? string.Empty
        };
        var active = AccessTools.Field(typeof(ModManager), "activeMods").GetValue(ModManager.Instance)
            as Dictionary<string, Duckov.Modding.ModBehaviour>;
        if (active == null) throw new InvalidOperationException("Active mod list unavailable");
        foreach (var behaviour in active.Values.Where(m => m))
        {
            var info = behaviour.info;
            var entry = new SharedHomeModEntry { Name = info.name, Version = info.version ?? string.Empty, Hash = string.Empty };
            if (info.dllFound)
            {
                try
                {
                    using var file = File.OpenRead(info.dllPath);
                    using var hash = SHA256.Create();
                    entry.Hash = BitConverter.ToString(hash.ComputeHash(file)).Replace("-", string.Empty);
                }
                catch (Exception) { entry.Unreadable = true; }
            }
            manifest.Mods.Add(entry);
        }
        return manifest;
    }
    internal static void Reset()
    {
        Waiting.Clear();
        Accepted.Clear();
        _clientReady = false;
        _clientDeadline = 0;
    }
    internal static void PeerConnected(NetPeer peer)
    {
        if (SharedHome.Enabled) Waiting[peer] = new Pending();
    }
    internal static void PeerDisconnected(NetPeer peer)
    {
        Waiting.Remove(peer);
        Accepted.Remove(peer);
    }
    internal static void BeginClient()
    {
        _clientReady = false;
        _clientDeadline = Time.unscaledTime + 60f;
        try { SharedHome.SendPrivate(7, Encode(Capture())); }
        catch (Exception e)
        {
            ConnectionRecovery.SkipNextDisconnect();
            Debug.LogWarning($"[SharedHome] Compatibility: {e.Message}");
            Show(new Failure { Issues = new() { new() { Kind = "invalid" } } }, false);
            NetService.Instance.connectedPeer?.Disconnect();
        }
    }
    internal static bool Receive(NetPeer peer, byte kind, byte[] data)
    {
        if (kind < 7 || kind > 10) return false;
        var server = NetService.Instance.IsServer;
        if (server && kind == 7 && peer != null && Waiting.TryGetValue(peer, out var pending) && !pending.Rejected)
        {
            SharedHomeManifest client = null;
            List<SharedHomeCompatibilityIssue> issues;
            try
            {
                client = Decode<SharedHomeManifest>(data);
                issues = Capture().Compare(client);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SharedHome] Compatibility: {e.Message}");
                issues = new() { new() { Kind = "invalid" } };
            }
            if (issues.Count > 0) Reject(peer, issues, client?.Player);
            else
            {
                Waiting.Remove(peer);
                Accepted.Add(peer);
                SharedHome.SendPrivate(8, Array.Empty<byte>(), peer);
                SharedHome.PeerValidated(peer);
            }
        }
        else if (server && kind == 10 && peer != null && Waiting.TryGetValue(peer, out pending) && pending.Rejected)
            peer.Disconnect();
        else if (!server && kind == 8 && _clientDeadline > 0 && !_clientReady)
        {
            _clientReady = true;
            _clientDeadline = 0;
            Send_ClientStatus.Instance.SendClientStatusUpdate();
            LocalPlayerManager.Instance?.RequestFullLoadoutSync();
            if (!SceneLoader.IsSceneLoading && LevelManager.LevelInited) COOPManager.ItemNet?.Client_RequestDropSnapshot();
        }
        else if (!server && kind == 9 && _clientDeadline > 0)
        {
            _clientDeadline = 0;
            _clientReady = false;
            ConnectionRecovery.SkipNextDisconnect();
            Show(Decode<Failure>(data), false);
            SharedHome.SendPrivate(10, Array.Empty<byte>());
            // The host disconnects after the reliable acknowledgement; the dialog survives teardown.
        }
        return true;
    }
    internal static void Tick()
    {
        if (NetService.Instance?.networkStarted != true || !SharedHome.Enabled) return;
        if (NetService.Instance.IsServer)
        {
            foreach (var pair in Waiting.ToArray())
            {
                if (Time.unscaledTime < pair.Value.Deadline) continue;
                if (pair.Value.Rejected) { pair.Key.Disconnect(); Waiting.Remove(pair.Key); }
                else Reject(pair.Key, new() { new() { Kind = "timeout" } }, null);
            }
        }
        else if (_clientDeadline > 0 && Time.unscaledTime >= _clientDeadline)
        {
            _clientDeadline = 0;
            Show(new Failure { Issues = new() { new() { Kind = "timeout" } } }, false);
            NetService.Instance.connectedPeer?.Disconnect();
        }
    }
    private static void Reject(NetPeer peer, List<SharedHomeCompatibilityIssue> issues, string player)
    {
        var name = NetService.Instance.ResolvePeerDisplayName(peer, player);
        if (string.IsNullOrEmpty(name)) name = $"Player_{peer.Id}";
        if (name.Length > 128) name = name.Substring(0, 128);
        var failure = new Failure { Player = name, Issues = issues };
        Waiting[peer] = new Pending { Rejected = true, Deadline = Time.unscaledTime + 15f };
        Show(failure, true);
        SharedHome.SendPrivate(9, Encode(failure), peer);
    }
    private static void Show(Failure failure, bool server)
    {
        var title = CoopLocalization.Get(server ? "net.sharedHome.rejectedHost" : "net.sharedHome.rejectedClient", failure.Player);
        var details = string.Join("\n\n", failure.Issues.Select(i =>
            CoopLocalization.Get("net.sharedHome." + i.Kind, i.Mod ?? "", i.Host ?? "", i.Client ?? "")));
        NetService.Instance.status = title;
        Debug.LogWarning($"[SharedHome] {title}\n{details}");
        MModUI.Instance?.ShowConnectionError(title, details);
    }
    private static byte[] Encode(object value) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
    private static T Decode<T>(byte[] data) => JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(data),
        new JsonSerializerSettings { MaxDepth = 16 });
}
