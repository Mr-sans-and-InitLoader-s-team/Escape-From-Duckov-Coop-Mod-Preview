namespace EscapeFromDuckovCoopMod;

internal static class KazooSync
{
    private sealed class RemoteVoice
    {
        public readonly KazooPlaybackState State = new();
        public KazooRemoteEmitter Emitter;
        public float RetryAt;
    }

    private static readonly Dictionary<string, RemoteVoice> Voices = new(StringComparer.Ordinal);
    private static readonly Dictionary<CoopPeer, uint> ClientSequences = new();
    private static ItemAgent_Kazoo _source;
    private static bool _playing;
    private static float _pitch;
    private static float _intensity;
    private static float _lastPitch;
    private static float _lastIntensity;
    private static float _lastSentAt;
    private static string _localScene;
    private static uint _sequence;

    private static bool IsLocal(ItemAgent_Kazoo agent) => agent != null && agent.Holder != null && agent.Holder == CharacterMainControl.Main;
    private static bool CanSend => NetService.Instance != null && NetService.Instance.networkStarted &&
        (NetService.Instance.IsServer || NetService.Instance.connectedPeer?.ConnectionState == ConnectionState.Connected);

    internal static void CaptureParameter(ItemAgent_Kazoo agent, string key, float value)
    {
        if (!IsLocal(agent) || !CanSend) return;
        if (_source != agent)
        {
            StopLocal(_source);
            _source = agent;
            _pitch = _intensity = 0;
        }
        if (key == "Kazoo/Pitch") _pitch = value;
        else _intensity = value;
    }

    internal static void Observe(ItemAgent_Kazoo agent, bool playing)
    {
        if (!IsLocal(agent) || !CanSend) return;
        if (_source != agent) { StopLocal(_source); _source = agent; }
        var main = CharacterMainControl.Main;
        playing &= agent.isActiveAndEnabled && main.Health != null && main.Health.CurrentHealth > 0;
        if (!LocalPlayerManager.Instance.ComputeIsInGame(out var scene)) playing = false;
        var transition = playing != _playing || (playing && _localScene != scene);
        var now = Time.realtimeSinceStartup;
        if (!transition && (!playing || now - _lastSentAt < 0.1f)) return;
        if (!transition && Mathf.Abs(_pitch - _lastPitch) < 0.01f && Mathf.Abs(_intensity - _lastIntensity) < 0.01f && now - _lastSentAt < 0.25f) return;
        _playing = playing;
        _localScene = scene;
        _lastSentAt = now;
        _lastPitch = _pitch;
        _lastIntensity = _intensity;
        Send(new KazooStateRpc
        {
            PlayerId = NetService.Instance.GetSelfNetworkId(), SceneId = scene,
            Sequence = ++_sequence, Playing = playing, Transition = transition,
            Pitch = _pitch, Intensity = _intensity
        });
    }

    internal static void StopLocal(ItemAgent_Kazoo agent)
    {
        if (!ReferenceEquals(agent, _source) || !_playing) return;
        _playing = false;
        if (CanSend)
            Send(new KazooStateRpc { PlayerId = NetService.Instance.GetSelfNetworkId(), SceneId = _localScene, Sequence = ++_sequence, Transition = true });
    }

    public static void Handle(RpcContext context, KazooStateRpc message)
    {
        var service = context.Service;
        if (service == null || !service.networkStarted || context.Sender == null) return;
        if (float.IsNaN(message.Pitch) || float.IsInfinity(message.Pitch) || float.IsNaN(message.Intensity) || float.IsInfinity(message.Intensity)) return;
        if (context.IsServer)
        {
            if (ClientSequences.TryGetValue(context.Sender, out var previous) && !KazooPlaybackState.IsNewer(message.Sequence, previous)) return;
            ClientSequences[context.Sender] = message.Sequence;
            message.PlayerId = service.GetPlayerId(context.Sender);
            // The host numbers its broadcasts, so a reconnecting client's reset
            // counter cannot be confused with delayed data from its old session.
            message.Sequence = ++_sequence;
        }
        else if (context.Sender != service.connectedPeer) return;
        if (string.IsNullOrEmpty(message.PlayerId) || service.IsSelfId(message.PlayerId)) return;
        if (!Voices.TryGetValue(message.PlayerId, out var voice)) Voices[message.PlayerId] = voice = new RemoteVoice();
        if (!voice.State.Apply(message.Sequence, message.Playing, message.SceneId, message.Pitch, message.Intensity, Time.realtimeSinceStartup)) return;
        if (!message.Playing) StopVoice(voice);
        else if (voice.Emitter != null) voice.Emitter.Refresh(voice.State.Pitch, voice.State.Intensity);
        if (context.IsServer) Send(message, context.Sender);
    }

    private static void Send(KazooStateRpc message, CoopPeer exclude = null)
    {
        if (!CanSend) return;
        var writer = RpcWriterPool.Rent();
        try
        {
            writer.Put((byte)Op.KAZOO_STATE);
            message.Serialize(writer);
            // Edges must arrive; continuous pitch/heartbeat samples must not queue
            // behind retransmissions. Sequence checks order the two delivery paths.
            var delivery = message.Transition ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable;
            var service = NetService.Instance;
            if (service.IsServer) service.netManager.SendToAll(writer, delivery, exclude);
            else service.connectedPeer.Send(writer, delivery);
        }
        finally { RpcWriterPool.Return(writer); }
    }

    internal static void Tick()
    {
        var service = NetService.Instance;
        if (service == null || !service.networkStarted) { Reset(); return; }
        if (_playing && (_source == null || !_source.isActiveAndEnabled || !IsLocal(_source) ||
            CharacterMainControl.Main.Health == null || CharacterMainControl.Main.Health.CurrentHealth <= 0)) StopLocal(_source);
        string scene = null;
        if (CharacterMainControl.Main != null && LevelManager.Instance != null)
            LocalPlayerManager.Instance?.ComputeIsInGame(out scene);
        foreach (var entry in Voices)
        {
            var voice = entry.Value;
            var target = ResolvePlayer(service, entry.Key);
            if (!voice.State.IsAudible(Time.realtimeSinceStartup, scene) || target == null || !target.activeInHierarchy)
            {
                StopVoice(voice);
                continue;
            }
            if (voice.Emitter != null && !voice.Emitter.IsPlaying) StopVoice(voice);
            if (voice.Emitter == null && Time.realtimeSinceStartup >= voice.RetryAt)
            {
                voice.Emitter = KazooRemoteEmitter.Create(target.transform, voice.State.Pitch, voice.State.Intensity);
                voice.RetryAt = voice.Emitter == null ? Time.realtimeSinceStartup + 1f : 0f;
            }
        }
    }

    private static GameObject ResolvePlayer(NetService service, string playerId)
    {
        if (!service.IsServer) return service.clientRemoteCharacters.TryGetValue(playerId, out var go) ? go : null;
        return service.TryGetPeerByPlayerId(playerId, out var peer) && service.remoteCharacters.TryGetValue(peer, out var remote) ? remote : null;
    }

    internal static void PeerDisconnected(CoopPeer peer)
    {
        var service = NetService.Instance;
        if (!service.IsServer) { Reset(); return; }
        ClientSequences.Remove(peer);
        var playerId = service.GetPlayerId(peer);
        if (Voices.TryGetValue(playerId, out var voice)) { StopVoice(voice); Voices.Remove(playerId); }
        Send(new KazooStateRpc { PlayerId = playerId, Sequence = ++_sequence, Transition = true }, peer);
    }

    internal static void SceneUnloaded()
    {
        StopLocal(_source);
        foreach (var voice in Voices.Values) { voice.State.Expire(); StopVoice(voice); }
    }

    internal static void Reset()
    {
        StopLocal(_source);
        foreach (var voice in Voices.Values) StopVoice(voice);
        Voices.Clear();
        ClientSequences.Clear();
        _source = null;
        _playing = false;
    }

    private static void StopVoice(RemoteVoice voice)
    {
        if (voice.Emitter != null) voice.Emitter.Shutdown();
        voice.Emitter = null;
    }
}
