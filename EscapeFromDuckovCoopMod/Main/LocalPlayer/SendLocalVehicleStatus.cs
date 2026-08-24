using System.Collections.Generic;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public class SendLocalVehicleStatus : MonoBehaviour
{
    public static SendLocalVehicleStatus Instance;

    private const float MaxVehicleFindDistance = 8f;
    private const float MinPositionDeltaSqr = 0.0025f;
    private const float MinRotationDelta = 1.5f;
    private const float MinSendInterval = 0.045f;
    private const float MaxSendInterval = 0.12f;
    private const float ForceSendPositionDeltaSqr = 0.25f;
    private const float ForceSendRotationDelta = 8f;
    private const float MaxPacketsPerSecond = 20f;
    private const float VehicleItemStateSendInterval = 0.75f;
    private const float NearbyVehicleItemStateDistance = 6f;

    private NetService Service => NetService.Instance;
    private bool IsServer => Service != null && Service.IsServer;
    private bool networkStarted => Service != null && Service.networkStarted;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;

    private static readonly Dictionary<int, string> VehicleAuthorities = new();
    private static readonly Dictionary<int, string> VehiclePendingRequesters = new();
    private Vector3 _lastSentPosition;
    private Quaternion _lastSentRotation = Quaternion.identity;
    private double _lastSentTime;
    private bool _hasLastSent;
    private int _lastVehicleId;
    private float _sendBudget = 1f;
    private float _lastBudgetUpdateTime = -1f;
    private int _lastClaimRequestVehicleId;
    private float _nextClaimRequestTime;
    private bool _forceNextVehicleSend;
    private int _lastVehicleItemStateVehicleId;
    private int _lastVehicleItemStateHash;
    private float _nextNearbyVehicleItemStateScanTime;
    private readonly Dictionary<int, int> _observedVehicleItemHashes = new();

    public void Init()
    {
        Instance = this;
    }

    public void SendVehicleTransformUpdate()
    {
        if (localPlayerStatus == null || !networkStarted) return;

        TrySendNearbyVehicleItemState();

        if (!TryGetLocalVehicle(out var vehicleId, out var vehicle))
            return;

        if (!CanLocalSendForVehicle(vehicleId))
        {
            TrySendInitialAuthorityClaim(vehicleId);
            return;
        }

        if (!HasVehicleAuthority(vehicleId))
        {
            if (IsServer)
                RPCVehicle.ServerAssignVehicleAuthority(vehicleId, localPlayerStatus.EndPoint);
            else
                SetVehicleAuthority(vehicleId, localPlayerStatus.EndPoint, false);
        }

        TrySendVehicleItemState(vehicleId, vehicle, true);

        if (_hasLastSent && _lastVehicleId != vehicleId)
            _hasLastSent = false;

        UpdateSendBudget();
        var forceSend = _forceNextVehicleSend;

        var position = vehicle.characterModel != null
            ? vehicle.characterModel.transform.position
            : vehicle.transform.position;
        var rotation = vehicle.characterModel != null
            ? vehicle.characterModel.transform.rotation
            : vehicle.transform.rotation;

        var now = Time.unscaledTimeAsDouble;
        var velocity = Vector3.zero;
        if (_lastSentTime > 0d)
        {
            var dt = now - _lastSentTime;
            if (dt > 1e-6)
                velocity = (position - _lastSentPosition) / (float)dt;
        }

        if (_hasLastSent)
        {
            var posDeltaSqr = (position - _lastSentPosition).sqrMagnitude;
            var rotDelta = Quaternion.Angle(rotation, _lastSentRotation);
            var sinceLast = now - _lastSentTime;
            var shouldForce = forceSend || posDeltaSqr >= ForceSendPositionDeltaSqr || rotDelta >= ForceSendRotationDelta;

            if (!shouldForce && sinceLast < MinSendInterval)
                return;

            if (posDeltaSqr < MinPositionDeltaSqr && rotDelta < MinRotationDelta && sinceLast < MaxSendInterval)
                return;

            if (!shouldForce && !TryConsumeSendBudget())
                return;
        }
        else if (!forceSend && !TryConsumeSendBudget())
        {
            return;
        }

        _lastSentPosition = position;
        _lastSentRotation = rotation;
        _lastSentTime = now;
        _hasLastSent = true;
        _lastVehicleId = vehicleId;
        _forceNextVehicleSend = false;

        if (IsServer && CoopSyncDatabase.AI.TryGet(vehicleId, out var entry) && entry != null && entry.IsVehicle)
        {
            entry.LastKnownPosition = position;
            entry.LastKnownRotation = rotation;
            entry.LastKnownVelocity = velocity;
            entry.LastKnownRemoteTime = now;
            entry.LastStateReceivedTime = Time.unscaledTime;
        }

        var rpc = new VehicleTransformSyncRpc
        {
            PlayerId = localPlayerStatus.EndPoint,
            VehicleId = vehicleId,
            Position = position,
            Rotation = rotation,
            Velocity = velocity,
            Timestamp = now
        };

        CoopTool.SendRpc(in rpc);
    }

    private void UpdateSendBudget()
    {
        var now = Time.unscaledTime;
        if (_lastBudgetUpdateTime < 0f)
        {
            _lastBudgetUpdateTime = now;
            _sendBudget = 1f;
            return;
        }

        var elapsed = now - _lastBudgetUpdateTime;
        if (elapsed <= 0f)
            return;

        _lastBudgetUpdateTime = now;
        _sendBudget = Mathf.Min(1f, _sendBudget + elapsed * MaxPacketsPerSecond);
    }

    private bool TryConsumeSendBudget()
    {
        if (_sendBudget < 1f)
            return false;

        _sendBudget -= 1f;
        return true;
    }


    public void RecordVehicleAuthority(int vehicleId, string playerId)
    {
        SetVehicleAuthority(vehicleId, playerId, false);
    }

    public void SetVehicleAuthority(int vehicleId, string playerId, bool clearPending)
    {
        if (vehicleId == 0 || string.IsNullOrEmpty(playerId))
            return;

        VehicleAuthorities.TryGetValue(vehicleId, out var previous);
        VehicleAuthorities[vehicleId] = playerId;
        if (clearPending)
            VehiclePendingRequesters.Remove(vehicleId);

        if (!string.Equals(previous, playerId, StringComparison.Ordinal))
            HandleVehicleAuthorityChanged(vehicleId, playerId);
    }

    public void ClearVehicleAuthority(int vehicleId)
    {
        if (vehicleId == 0)
            return;

        var hadAuthority = VehicleAuthorities.ContainsKey(vehicleId);
        VehicleAuthorities.Remove(vehicleId);
        VehiclePendingRequesters.Remove(vehicleId);

        if (hadAuthority)
            HandleVehicleAuthorityChanged(vehicleId, string.Empty);
    }

    private void HandleVehicleAuthorityChanged(int vehicleId, string authorityId)
    {
        if (vehicleId == 0)
            return;

        if (_lastVehicleId == vehicleId)
            _hasLastSent = false;

        _sendBudget = 1f;
        _lastBudgetUpdateTime = Time.unscaledTime;

        if (_lastClaimRequestVehicleId == vehicleId)
        {
            _lastClaimRequestVehicleId = 0;
            _nextClaimRequestTime = 0f;
        }

        var localId = localPlayerStatus?.EndPoint;
        _forceNextVehicleSend = !string.IsNullOrEmpty(localId) &&
                                string.Equals(authorityId, localId, StringComparison.Ordinal);

        if (_lastVehicleItemStateVehicleId == vehicleId)
        {
            _lastVehicleItemStateVehicleId = 0;
            _lastVehicleItemStateHash = 0;
        }
    }

    public bool HasVehicleAuthority(int vehicleId)
    {
        return vehicleId != 0 &&
               VehicleAuthorities.TryGetValue(vehicleId, out var current) &&
               !string.IsNullOrEmpty(current);
    }

    public string GetVehicleAuthority(int vehicleId)
    {
        return vehicleId != 0 && VehicleAuthorities.TryGetValue(vehicleId, out var current)
            ? current
            : string.Empty;
    }

    public bool IsAuthorityPlayer(int vehicleId, string playerId)
    {
        return vehicleId != 0 &&
               !string.IsNullOrEmpty(playerId) &&
               VehicleAuthorities.TryGetValue(vehicleId, out var current) &&
               string.Equals(current, playerId, StringComparison.Ordinal);
    }

    public void SetPendingAuthorityRequest(int vehicleId, string requesterId)
    {
        if (vehicleId == 0)
            return;

        if (string.IsNullOrEmpty(requesterId))
            VehiclePendingRequesters.Remove(vehicleId);
        else
            VehiclePendingRequesters[vehicleId] = requesterId;
    }

    public string GetPendingAuthorityRequester(int vehicleId)
    {
        return vehicleId != 0 && VehiclePendingRequesters.TryGetValue(vehicleId, out var requester)
            ? requester
            : string.Empty;
    }

    public bool TryGetLocalVehicleInfo(out int vehicleId, out CharacterMainControl vehicle)
    {
        return TryGetLocalVehicle(out vehicleId, out vehicle);
    }

    public bool IsLocalAuthorityForVehicle(CharacterMainControl vehicle)
    {
        var vehicleId = ResolveVehicleId(vehicle);
        return IsLocalAuthorityForVehicle(vehicleId);
    }

    public bool IsLocalAuthorityForVehicle(int vehicleId)
    {
        if (vehicleId == 0)
            return IsServer;

        var playerId = localPlayerStatus?.EndPoint;
        if (string.IsNullOrEmpty(playerId))
            return IsServer;

        return VehicleAuthorities.TryGetValue(vehicleId, out var current) &&
               string.Equals(current, playerId, StringComparison.Ordinal);
    }

    public bool CanLocalSendForVehicle(int vehicleId)
    {
        var playerId = localPlayerStatus?.EndPoint;
        if (vehicleId == 0 || string.IsNullOrEmpty(playerId))
            return false;

        if (!VehicleAuthorities.TryGetValue(vehicleId, out var current) || string.IsNullOrEmpty(current))
            return IsServer;

        return string.Equals(current, playerId, StringComparison.Ordinal);
    }

    private void TrySendInitialAuthorityClaim(int vehicleId)
    {
        if (IsServer || vehicleId == 0 || HasVehicleAuthority(vehicleId))
            return;

        var now = Time.unscaledTime;
        if (_lastClaimRequestVehicleId == vehicleId && now < _nextClaimRequestTime)
            return;

        _lastClaimRequestVehicleId = vehicleId;
        _nextClaimRequestTime = now + 1.5f;
        RPCVehicle.RequestVehicleControl(vehicleId, true);
    }

    public void RecordVehicleItemState(int vehicleId, ItemSnapshot snapshot)
    {
        if (vehicleId == 0 || snapshot.TypeId == 0)
            return;

        var hash = ItemTool.ComputeSnapshotHash(snapshot);
        _observedVehicleItemHashes[vehicleId] = hash;
        _lastVehicleItemStateVehicleId = vehicleId;
        _lastVehicleItemStateHash = hash;
    }

    private void TrySendNearbyVehicleItemState()
    {
        var now = Time.unscaledTime;
        if (now < _nextNearbyVehicleItemStateScanTime)
            return;

        _nextNearbyVehicleItemStateScanTime = now + VehicleItemStateSendInterval;

        var mainControl = CharacterMainControl.Main;
        if (mainControl == null)
            return;

        var vehicleId = FindNearestVehicleId(mainControl.transform.position, NearbyVehicleItemStateDistance);
        if (vehicleId == 0)
            return;

        var vehicle = COOPManager.AI?.TryGetCharacter(vehicleId);
        TrySendVehicleItemState(vehicleId, vehicle, false);
    }

    private void TrySendVehicleItemState(int vehicleId, CharacterMainControl vehicle, bool allowInitialSend)
    {
        if (vehicleId == 0 || vehicle == null || !vehicle.isVehicle)
            return;

        var item = vehicle.CharacterItem;
        if (item == null)
            return;

        var snapshot = ItemTool.MakeSnapshot(item);
        if (snapshot.TypeId == 0)
            return;

        var hash = ItemTool.ComputeSnapshotHash(snapshot);
        var hadObserved = _observedVehicleItemHashes.TryGetValue(vehicleId, out var previousHash);
        _observedVehicleItemHashes[vehicleId] = hash;

        if (!allowInitialSend && (!hadObserved || previousHash == hash))
            return;

        if (_lastVehicleItemStateVehicleId == vehicleId && _lastVehicleItemStateHash == hash)
            return;

        _lastVehicleItemStateVehicleId = vehicleId;
        _lastVehicleItemStateHash = hash;
        RPCVehicle.SendVehicleItemState(vehicleId, snapshot);
    }

    private bool TryGetLocalVehicle(out int vehicleId, out CharacterMainControl vehicle)
    {
        vehicleId = 0;
        vehicle = null;

        var level = LevelManager.Instance;
        var controlling = level != null ? level.ControllingCharacter : null;
        if (controlling != null && controlling.isVehicle)
        {
            var resolvedId = ResolveVehicleId(controlling);
            if (resolvedId != 0)
            {
                vehicleId = resolvedId;
                vehicle = controlling;
                return true;
            }
        }

        var mainControl = CharacterMainControl.Main;
        if (mainControl == null || mainControl.modelRoot == null)
            return false;

        var model = mainControl.modelRoot.Find("0_CharacterModel_Custom_Template(Clone)");
        if (model == null)
            return false;

        var animCtrl = model.GetComponent<CharacterAnimationControl_MagicBlend>();
        if (animCtrl == null || animCtrl.animator == null)
            return false;

        if (animCtrl.animator.GetInteger("VehicleType") <= 0)
            return false;

        vehicleId = FindNearestVehicleId(mainControl.transform.position, MaxVehicleFindDistance);
        if (vehicleId == 0)
            return false;

        vehicle = COOPManager.AI?.TryGetCharacter(vehicleId);
        return vehicle != null;
    }

    public static int ResolveVehicleId(CharacterMainControl vehicle)
    {
        if (vehicle == null)
            return 0;

        foreach (var entry in CoopSyncDatabase.AI.Entries)
        {
            if (entry == null || !entry.IsVehicle || entry.Status == AIStatus.Dead)
                continue;

            var cmc = COOPManager.AI?.TryGetCharacter(entry.Id);
            if (cmc == vehicle)
                return entry.Id;

        }

        return 0;
    }

    public static int FindNearestVehicleId(Vector3 riderPos, float maxDistance)
    {
        var maxSqr = maxDistance * maxDistance;
        var bestId = 0;
        var bestSqr = float.MaxValue;
        foreach (var entry in CoopSyncDatabase.AI.Entries)
        {
            if (entry == null || !entry.IsVehicle || entry.Status == AIStatus.Dead)
                continue;

            var pos = entry.LastKnownPosition != Vector3.zero ? entry.LastKnownPosition : entry.SpawnPosition;
            var distSqr = (pos - riderPos).sqrMagnitude;
            if (distSqr > maxSqr || distSqr >= bestSqr)
                continue;

            bestSqr = distSqr;
            bestId = entry.Id;
        }

        return bestId;
    }
}
