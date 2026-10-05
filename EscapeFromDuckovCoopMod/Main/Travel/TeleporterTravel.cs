using Duckov.Economy;
using Duckov.UI;

namespace EscapeFromDuckovCoopMod;

internal static class TeleporterTravel
{
    private static string _token;
    private static string _sceneId;
    private static int _beaconIndex;
    private static TravelBarrier _barrier;
    private static TravelStage _localStage;
    private static TeleporterCost _cost;
    private static float _deadline;
    private static bool _confirmed;
    private static bool _cancelRequested;
    private static MapSelectionEntry _entry;
    private static MapSelectionEntry Entry => _entry != null ? _entry : (_entry = TeleporterCost.Resolve(_sceneId, _beaconIndex));
    public static bool IsActive => !string.IsNullOrEmpty(_token);
    private static NetService Service => NetService.Instance;

    public static void Request(string sceneId, int beaconIndex)
    {
        if (IsActive || Cost.TaskPending)
        {
            TravelNotice.Show(CoopLocalization.Get("ui.teleporter.busy"));
            return;
        }
        if (Service.IsServer) StartHostVote(sceneId, beaconIndex);
        else CoopTool.SendRpc(new TeleporterFlowRpc { Kind = TeleporterFlowKind.Request, SceneId = sceneId, BeaconIndex = beaconIndex });
    }

    private static void StartHostVote(string sceneId, int beaconIndex)
    {
        if (IsActive || Cost.TaskPending || string.IsNullOrEmpty(sceneId)) return;
        SceneNet.Instance.Host_BeginSceneVote_Simple(sceneId, null, false, false, false, "OnPointerClick", beaconIndex);
    }

    public static string BeginHost(string sceneId, int beaconIndex, IEnumerable<string> participants)
    {
        BeginLocal(Guid.NewGuid().ToString("N"), sceneId, beaconIndex);
        _barrier = new TravelBarrier(participants);
        return _token;
    }

    public static void BeginLocal(string token, string sceneId, int beaconIndex)
    {
        Reset();
        _token = token;
        _sceneId = sceneId;
        _beaconIndex = beaconIndex;
        _localStage = TravelStage.Voting;
        _cost = new TeleporterCost();
    }

    public static void VotesReady()
    {
        if (!Service.IsServer || _barrier == null || !_barrier.BeginCheck()) return;
        SendCommand(TeleporterFlowKind.Check);
    }

    public static bool WithdrawReady(string playerId)
    {
        if (_barrier?.Stage != TravelStage.Checking || !_barrier.Contains(playerId)) return false;
        Abort(playerId, "ui.teleporter.reason.readyWithdrawn");
        return true;
    }

    public static void Handle(RpcContext context, TeleporterFlowRpc message)
    {
        if (context.Service == null || !context.Service.networkStarted) return;
        if (context.IsServer && message.Kind == TeleporterFlowKind.Request)
        {
            if (context.Sender?.ConnectionState == ConnectionState.Connected) StartHostVote(message.SceneId, message.BeaconIndex);
            return;
        }
        if (!IsActive || message.Token != _token) return;
        if (context.IsServer)
        {
            if (context.Sender == null) return;
            HandleReply(Service.GetPlayerId(context.Sender), message);
        }
        else if (context.Sender == Service.connectedPeer) HandleCommand(message);
    }

    private static void SendCommand(TeleporterFlowKind kind, string reason = null, string playerName = null)
    {
        var message = new TeleporterFlowRpc { Kind = kind, Token = _token, Reason = reason, PlayerName = playerName };
        CoopTool.SendRpc(in message);
        HandleCommand(message);
    }

    private static void Reply(TeleporterFlowKind kind, bool success, string reason = null)
    {
        var reply = new TeleporterFlowRpc { Kind = kind, Token = _token, Success = success, Reason = reason };
        if (Service.IsServer) HandleReply(Service.GetSelfNetworkId(), reply);
        else CoopTool.SendRpc(in reply);
    }

    private static void HandleCommand(TeleporterFlowRpc message)
    {
        try
        {
            switch (message.Kind)
            {
                case TeleporterFlowKind.Check:
                    if (_localStage != TravelStage.Voting) return;
                    _localStage = TravelStage.Checking;
                    _deadline = Time.unscaledTime + 20f;
                    var enough = TeleporterCost.Check(Entry, out var reason);
                    Reply(TeleporterFlowKind.Checked, enough, reason);
                    break;
                case TeleporterFlowKind.ShowDeparture:
                    if (_localStage != TravelStage.Checking) return;
                    _localStage = TravelStage.Departure;
                    _deadline = Time.unscaledTime + 120f;
                    SceneNet.Instance.sceneVoteActive = false;
                    var entry = Entry;
                    if (!TeleporterCost.Check(entry, out var failure) || !TeleporterDepartureView.Show(entry))
                        CancelLocal(failure ?? "ui.teleporter.reason.openFailed");
                    break;
                case TeleporterFlowKind.Pay:
                    if (_localStage != TravelStage.Departure || !_confirmed) return;
                    _localStage = TravelStage.Paying;
                    _deadline = Time.unscaledTime + 20f;
                    TeleporterDepartureView.LockCancellation();
                    var paid = _cost.TryPay(Entry, out var payFailure);
                    Reply(TeleporterFlowKind.Paid, paid, payFailure);
                    break;
                case TeleporterFlowKind.Aborted:
                    var notice = TravelMessages.Cancellation(message.PlayerName, message.Reason);
                    Reset();
                    SceneNet.Instance.ResetTeleporterVote();
                    TravelNotice.Show(notice);
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Teleporter] {message.Kind} failed: {ex}");
            CancelLocal("ui.teleporter.reason.checkFailed");
        }
    }

    private static void HandleReply(string playerId, TeleporterFlowRpc message)
    {
        if (_barrier == null || !_barrier.Contains(playerId)) return;
        if (message.Kind == TeleporterFlowKind.Cancel)
        {
            Abort(playerId, message.Reason ?? "ui.teleporter.reason.cancelled");
            return;
        }
        var expected = message.Kind switch
        {
            TeleporterFlowKind.Checked => TravelStage.Checking,
            TeleporterFlowKind.Confirmed => TravelStage.Departure,
            TeleporterFlowKind.Paid => TravelStage.Paying,
            _ => TravelStage.Cancelled
        };
        if (_barrier.Stage != expected) return;
        if (!message.Success) { Abort(playerId, message.Reason ?? "ui.teleporter.reason.requirementsFailed"); return; }
        if (!_barrier.Reply(playerId, expected, true)) return;
        if (expected == TravelStage.Checking && _barrier.Advance(expected, TravelStage.Departure))
            SendCommand(TeleporterFlowKind.ShowDeparture);
        else if (expected == TravelStage.Departure && _barrier.Advance(expected, TravelStage.Paying))
            SendCommand(TeleporterFlowKind.Pay);
        else if (expected == TravelStage.Paying && _barrier.Advance(expected, TravelStage.Committed))
        {
            // All owners have successfully paid. SceneBeginLoad is the commit
            // message; no individual confirmation is allowed to load a map.
            SceneNet.Instance.StartValidatedTeleporterLoad();
        }
    }

    public static void ConfirmLocal()
    {
        if (!IsActive || _localStage != TravelStage.Departure || _confirmed) return;
        var enough = TeleporterCost.Check(Entry, out var reason);
        _confirmed = enough;
        Reply(TeleporterFlowKind.Confirmed, enough, reason);
    }

    public static void CancelLocal(string reason)
    {
        if (!IsActive || _cancelRequested) return;
        if (Service.IsServer) Abort(Service.GetSelfNetworkId(), reason);
        else
        {
            _cancelRequested = true;
            _deadline = Time.unscaledTime + 10f;
            Reply(TeleporterFlowKind.Cancel, false, reason);
        }
    }

    private static void Abort(string playerId, string reason)
    {
        if (_barrier == null || _barrier.Stage == TravelStage.Committed) return;
        _barrier.Cancel();
        var name = playerId == Service.GetSelfNetworkId() ? Service.ResolveLocalPlayerName() : playerId;
        if (Service.TryGetPeerByPlayerId(playerId, out var peer) && Service.playerStatuses.TryGetValue(peer, out var status))
            name = status.PlayerName;
        SendCommand(TeleporterFlowKind.Aborted, reason, name);
    }

    public static bool AcceptCommit(string token)
    {
        if (!IsActive || token != _token || _localStage != TravelStage.Paying || _cost?.Paid != true) return false;
        _cost.Commit();
        TeleporterDepartureView.Close();
        _cost = null;
        _barrier = null;
        _token = null;
        return true;
    }

    public static void PeerDisconnected(CoopPeer peer)
    {
        if (!IsActive) return;
        if (Service.IsServer)
        {
            var id = Service.GetPlayerId(peer);
            if (_barrier?.Contains(id) == true) Abort(id, "ui.teleporter.reason.disconnected");
        }
        else { Reset(); SceneNet.Instance.ResetTeleporterVote(); TravelNotice.Show(CoopLocalization.Get("ui.teleporter.disconnected")); }
    }

    public static void Tick()
    {
        if (!IsActive) return;
        if (Service == null || !Service.networkStarted) { Reset(); return; }
        if (_cancelRequested)
        {
            if (Time.unscaledTime > _deadline)
            {
                Reset();
                SceneNet.Instance.ResetTeleporterVote();
                TravelNotice.Show(CoopLocalization.Get("ui.teleporter.cancelTimeout"));
            }
            return;
        }
        if (_localStage == TravelStage.Departure)
        {
            if (TeleporterDepartureView.WasClosed) { CancelLocal("ui.teleporter.reason.closed"); return; }
            if (!TeleporterCost.Check(Entry, out var reason))
            { CancelLocal(reason); return; }
        }
        if (_localStage != TravelStage.Voting && Time.unscaledTime > _deadline)
        {
            if (Service.IsServer) Abort(Service.GetSelfNetworkId(), "ui.teleporter.reason.timeout");
            else { CancelLocal("ui.teleporter.reason.timeout"); Reset(); SceneNet.Instance.ResetTeleporterVote(); }
        }
    }

    public static void Reset()
    {
        TeleporterDepartureView.Close();
        _cost?.Rollback();
        _cost = null;
        _barrier = null;
        _token = null;
        _confirmed = false;
        _cancelRequested = false;
        _entry = null;
    }
}
