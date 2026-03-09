using System.Collections.Generic;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public class SendLocalVehicleStatus : MonoBehaviour
{
    public static SendLocalVehicleStatus Instance;

    private const float MaxVehicleFindDistance = 8f;
    private const float MinPositionDeltaSqr = 0.0025f;
    private const float MinRotationDelta = 1.5f;
    private const float MinSendInterval = 0.08f;
    private const float MaxSendInterval = 0.25f;
    private const float ForceSendPositionDeltaSqr = 0.25f;
    private const float ForceSendRotationDelta = 8f;
    private const float MaxPacketsPerSecond = 12f;

    private NetService Service => NetService.Instance;
    private bool IsServer => Service != null && Service.IsServer;
    private bool networkStarted => Service != null && Service.networkStarted;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;

    private static readonly Dictionary<int, string> VehicleAuthorities = new();
    private Vector3 _lastSentPosition;
    private Quaternion _lastSentRotation = Quaternion.identity;
    private double _lastSentTime;
    private bool _hasLastSent;
    private int _lastVehicleId;
    private float _sendBudget = 1f;
    private float _lastBudgetUpdateTime = -1f;

    public void Init()
    {
        Instance = this;
    }

    public void SendVehicleTransformUpdate()
    {
        if (IsServer || localPlayerStatus == null || !networkStarted) return;

        if (!TryGetLocalVehicle(out var vehicleId, out var vehicle))
            return;

        if (!CanSendForVehicle(vehicleId, localPlayerStatus.EndPoint))
            return;

        if (_hasLastSent && _lastVehicleId != vehicleId)
            _hasLastSent = false;

        UpdateSendBudget();

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
            var shouldForce = posDeltaSqr >= ForceSendPositionDeltaSqr || rotDelta >= ForceSendRotationDelta;

            if (!shouldForce && sinceLast < MinSendInterval)
                return;

            if (posDeltaSqr < MinPositionDeltaSqr && rotDelta < MinRotationDelta && sinceLast < MaxSendInterval)
                return;

            if (!shouldForce && !TryConsumeSendBudget())
                return;
        }
        else if (!TryConsumeSendBudget())
        {
            return;
        }

        _lastSentPosition = position;
        _lastSentRotation = rotation;
        _lastSentTime = now;
        _hasLastSent = true;
        _lastVehicleId = vehicleId;

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
        if (vehicleId == 0 || string.IsNullOrEmpty(playerId))
            return;

        VehicleAuthorities[vehicleId] = playerId;
    }

    private static bool CanSendForVehicle(int vehicleId, string playerId)
    {
        if (vehicleId == 0 || string.IsNullOrEmpty(playerId))
            return false;

        return !VehicleAuthorities.TryGetValue(vehicleId, out var current) ||
               string.Equals(current, playerId, StringComparison.Ordinal);
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

    private static int ResolveVehicleId(CharacterMainControl vehicle)
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

    private static int FindNearestVehicleId(Vector3 riderPos, float maxDistance)
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
