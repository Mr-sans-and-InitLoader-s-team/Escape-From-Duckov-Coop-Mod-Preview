using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Saves;

namespace EscapeFromDuckovCoopMod;

public static class SharedHome
{
    private static string _session;
    private static long _revision;
    private static long _requestSerial;
    private static bool _remoteEnabled;
    private static bool _initialized;
    private static bool _dirty;
    private static float _nextUpdate;
    private static float _nextSave;
    private static readonly Dictionary<NetPeer, long> Requests = new();
    private static readonly HashSet<NetPeer> NeedsFull = new();
    private static byte[] _pendingState;
    private static bool _hostEnabled;
    internal static bool Applying { get; private set; }
    internal static bool Reloading { get; set; }
    public static bool Enabled => NetService.Instance?.networkStarted == true && !NetService.Instance.IsServer
        ? _remoteEnabled : CoopAISettings.ActiveGeneral.PrivateSharedHome;
    public static bool Active => NetService.Instance?.networkStarted == true && Enabled &&
        (NetService.Instance.IsServer || SharedHomeCompatibility.ClientReady);
    public static bool Guest => Active && !NetService.Instance.IsServer;

    public static void Init()
    {
        if (_initialized) return;
        _initialized = true;
        SharedHomeProgress.Init();
        SharedHomeQuests.Init();
        SharedHomeBuildings.Init();
        SharedHomeWorld.Init();
    }
    public static void SetEnabled(bool enabled)
    {
        var service = NetService.Instance;
        if (service?.networkStarted == true) return;
        var settings = CoopAISettings.ActiveGeneral.Clone();
        settings.PrivateSharedHome = enabled;
        CoopAISettings.Instance?.ApplyGeneral(settings);
        AISyncSettingsPersistence.Save(CoopAISettings.Active, settings, CoopLootSettings.Active,
            DifficultyManager.Selected, DifficultyManager.GetCustomSettings());
    }
    public static void OnSettingsApplied()
    {
        var service = NetService.Instance;
        if (service?.networkStarted != true || !service.IsServer) return;
        if (_hostEnabled == Enabled) return;
        // Inventory transactions must finish before changing the room's save owner.
        CoopAISettings.ActiveGeneral.PrivateSharedHome = _hostEnabled;
    }
    public static void NetworkStarted()
    {
        Init();
        _session = Guid.NewGuid().ToString("N");
        _revision = 0;
        _requestSerial = 0;
        _remoteEnabled = false;
        _hostEnabled = Enabled;
        Requests.Clear();
        NeedsFull.Clear();
        SharedHomeCompatibility.Reset();
        SharedHomeEntry.Reset();
        SharedHomeSave.ResetPublished();
        if (NetService.Instance.IsServer) MarkDirty();
    }
    public static void PeerConnected(NetPeer peer)
    {
        if (!NetService.Instance.IsServer) return;
        Requests.Remove(peer);
        SharedHomeCompatibility.PeerConnected(peer);
        var message = new SharedHomeRpc { Kind = 0, Session = _session, Data = new[] { Enabled ? (byte)1 : (byte)0 } };
        CoopTool.SendRpcTo(peer, in message);
    }
    internal static void PeerValidated(NetPeer peer)
    {
        NeedsFull.Add(peer);
        MarkDirty();
    }
    public static void Reset()
    {
        var host = Active && NetService.Instance.IsServer;
        _pendingState = null;
        SharedHomeCharacterCreation.Reset();
        SharedHomeLoadouts.Reset();
        SharedHomeInventoryTransaction.Reset();
        _remoteEnabled = false;
        SharedHomeStorage.Reset();
        if (host)
        {
            SavesSystem.CollectSaveData();
            SavesSystem.SaveFile(false);
        }
        SharedHomeSave.Restore();
        _session = null;
        _dirty = false;
        Requests.Clear();
        NeedsFull.Clear();
        SharedHomeCompatibility.Reset();
        SharedHomeEntry.Reset();
        SharedHomeSave.ResetPublished();
    }
    public static void MarkDirty()
    {
        if (Active && NetService.Instance.IsServer) _dirty = true;
    }
    public static void Tick()
    {
        SharedHomeCompatibility.Tick();
        if (Guest && _pendingState != null && SharedHomeEntry.AtMainMenu)
            Apply(() => SharedHomeSave.Apply(_pendingState, false));
        SharedHomeEntry.Tick();
        if (!Active || SceneLoader.IsSceneLoading || !LevelManager.LevelInited) return;
        if (Guest && _pendingState != null && !Reloading && !SharedHomeLoadouts.Restoring)
        {
            if (!SharedHomeSave.OverlayActive)
            {
                // Re-enter through the menu so the personal scene cannot retain NPCs or inventory slots.
                Apply(SharedHomeSave.Begin);
                SceneLoader.LoadMainMenu();
                return;
            }
            var state = _pendingState;
            _pendingState = null;
            Apply(() => SharedHomeSave.Apply(state));
        }
        SharedHomeLoadouts.Tick();
        SharedHomeStorage.Ensure();
        if (!NetService.Instance.IsServer || Time.unscaledTime < _nextUpdate) return;
        _nextUpdate = Time.unscaledTime + .25f;
        if (!_dirty) return;
        _dirty = false;
        if (NeedsFull.Count > 0)
        {
            var full = new SharedHomeRpc { Kind = 1, Session = _session, Revision = ++_revision, Data = SharedHomeSave.Capture(true) };
            foreach (var peer in NeedsFull)
                if (peer.ConnectionState == ConnectionState.Connected) CoopTool.SendRpcTo(peer, in full);
            NeedsFull.Clear();
        }
        var data = SharedHomeSave.Capture();
        if (data != null)
        {
            var message = new SharedHomeRpc { Kind = 1, Session = _session, Revision = ++_revision, Data = data };
            foreach (var peer in NetService.Instance.netManager.ConnectedPeerList)
                if (SharedHomeCompatibility.Allows(peer)) CoopTool.SendRpcTo(peer, in message);
        }
        if (Time.unscaledTime >= _nextSave)
        {
            _nextSave = Time.unscaledTime + 5f;
            SavesSystem.SaveFile(false);
        }
    }
    internal static void Apply(Action action)
    {
        var previous = Applying;
        Applying = true;
        try { action(); }
        finally { Applying = previous; }
    }
    internal static bool Request(string action, object arguments)
    {
        if (!Guest || Applying || string.IsNullOrEmpty(_session) ||
            NetService.Instance.connectedPeer?.ConnectionState != ConnectionState.Connected) return false;
        if (action != "storageTaken" && (!SharedHomeLoadouts.Ready || SharedHomeCharacterCreation.Joining)) return false;
        var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { action, arguments }));
        var message = new SharedHomeRpc { Kind = 2, Session = _session, Revision = ++_requestSerial, Data = data };
        CoopTool.SendRpc(in message);
        return true;
    }
    internal static void SendPrivate(byte kind, byte[] data, NetPeer peer = null)
    {
        var message = new SharedHomeRpc { Kind = kind, Session = _session, Data = data };
        if (peer != null) CoopTool.SendRpcTo(peer, in message);
        else CoopTool.SendRpc(in message);
    }
    internal static void Reply(NetPeer peer, byte[] data)
    {
        var message = new SharedHomeRpc { Kind = 3, Session = _session, Data = data };
        CoopTool.SendRpcTo(peer, in message);
    }
    public static void Handle(RpcContext context, SharedHomeRpc message)
    {
        if (Enabled && message.Session == _session && SharedHomeCompatibility.Receive(context.Sender, message.Kind, message.Data)) return;
        if (context.IsServer && !SharedHomeCompatibility.Allows(context.Sender)) return;
        if (Active && message.Session == _session && (message.Kind >= 4 && message.Kind <= 6 || message.Kind == 11))
        {
            if (context.IsServer && context.Sender == null) return;
            SharedHomeLoadouts.Receive(context.Sender, message.Kind, message.Data);
            return;
        }
        if (context.IsServer)
        {
            if (!Active || message.Kind != 2 || context.Sender == null || message.Session != _session) return;
            if (Requests.TryGetValue(context.Sender, out var last) && message.Revision <= last) return;
            Requests[context.Sender] = message.Revision;
            var request = JObject.Parse(Encoding.UTF8.GetString(message.Data));
            SharedHomeActions.Execute((string)request["action"], (JObject)request["arguments"], context.Sender);
            MarkDirty();
            return;
        }
        if (message.Kind == 0)
        {
            if (message.Data?.Length != 1) return;
            if (_session != message.Session) Reset();
            _session = message.Session;
            _remoteEnabled = message.Data[0] != 0;
            _revision = 0;
            if (!_remoteEnabled) Reset();
            else SharedHomeCompatibility.BeginClient();
            return;
        }
        if (Guest && message.Kind == 12 && message.Session == _session)
        {
            SharedHomeInventoryTransaction.Receive(message.Data);
            return;
        }
        if (Guest && message.Kind == 3 && message.Session == _session)
        {
            SharedHomeStorage.ReceiveTake(message.Data);
            return;
        }
        if (!Guest || message.Kind != 1 || message.Session != _session || message.Revision <= _revision) return;
        _revision = message.Revision;
        _pendingState = SharedHomeSave.Combine(_pendingState, message.Data);
    }
}
