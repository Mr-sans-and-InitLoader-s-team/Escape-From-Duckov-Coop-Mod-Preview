using System.Collections.Generic;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public static class RPCVehicle
{
    private const float MaxVehicleSyncDistance = 20f;
    private const float MinMovingSpeed = 0.08f;
    private static readonly Dictionary<int, VehicleAnimHistory> VehicleAnimHistories = new();

    private struct VehicleAnimHistory
    {
        public Vector3 Position;
        public double Time;
        public float Speed;
        public bool HasValue;
    }

    public static void HandleVehicleTransformSync(RpcContext context, VehicleTransformSyncRpc message)
    {
        var service = context.Service;
        if (service == null) return;

        if (context.IsServer)
        {
            if (!CoopSyncDatabase.AI.TryGet(message.VehicleId, out var entry) || entry == null || !entry.IsVehicle)
                return;

            var playerId = service.GetPlayerId(context.Sender);
            if (!RPCPlayer.TryEnsurePrimaryVehicleRider(playerId, message.VehicleId) &&
                !RPCPlayer.IsPrimaryVehicleRider(playerId, message.VehicleId))
                return;

            if (service.playerStatuses != null && service.playerStatuses.TryGetValue(context.Sender, out var status) && status != null)
            {
                var distSqr = (status.Position - message.Position).sqrMagnitude;
                if (distSqr > MaxVehicleSyncDistance * MaxVehicleSyncDistance)
                    return;
            }

            message.PlayerId = playerId;

            entry.LastKnownPosition = message.Position;
            entry.LastKnownRotation = message.Rotation;
            entry.LastKnownVelocity = message.Velocity;
            entry.LastKnownRemoteTime = message.Timestamp;
            entry.LastStateReceivedTime = Time.unscaledTime;
            entry.LastAnimSample = BuildVehicleAnimSample(message.VehicleId, entry, message);

            var vehicle = COOPManager.AI?.TryGetCharacter(message.VehicleId);
            if (vehicle)
            {
                vehicle.transform.SetPositionAndRotation(message.Position, message.Rotation);
                if (vehicle.characterModel)
                    vehicle.characterModel.transform.SetPositionAndRotation(message.Position, message.Rotation);
                EnsureVehicleAnimatorDriver(vehicle, entry);
            }

            CoopTool.SendRpc(in message, context.Sender);
            return;
        }

        if (service.IsSelfId(message.PlayerId)) return;

        AISyncEntry clientEntry = null;
        if (CoopSyncDatabase.AI.TryGet(message.VehicleId, out clientEntry) && clientEntry != null && clientEntry.IsVehicle)
            clientEntry.LastAnimSample = BuildVehicleAnimSample(message.VehicleId, clientEntry, message);

        SendLocalVehicleStatus.Instance?.RecordVehicleAuthority(message.VehicleId, message.PlayerId);
        COOPManager.AI?.Client_HandleVehicleTransform(message);

        var clientVehicle = COOPManager.AI?.TryGetCharacter(message.VehicleId);
        if (clientVehicle)
        {
            EnsureVehicleAnimatorDriver(clientVehicle, clientEntry);
        }
    }





    private static void EnsureVehicleAnimatorDriver(CharacterMainControl vehicle, AISyncEntry entry)
    {
        if (!vehicle) return;

        var driver = vehicle.gameObject.GetComponent<VehicleMovementAnimatorDriver>();
        if (driver == null)
            driver = vehicle.gameObject.AddComponent<VehicleMovementAnimatorDriver>();

        var vehicleType = entry != null && entry.VehicleAnimationType > 0
            ? entry.VehicleAnimationType
            : vehicle.vehicleAnimationType;
        driver.Bind(vehicle, vehicleType);

        var interp = vehicle.GetComponentInChildren<AnimParamInterpolator>(true);
        if (interp != null && interp.enabled)
            interp.enabled = false;
    }

    private static float ResolveSyncSpeed(int vehicleId, Vector3 position, Vector3 velocity, double timestamp)
    {
        var speed = velocity.magnitude;
        var now = timestamp > 0d ? timestamp : Time.unscaledTimeAsDouble;

        if (vehicleId != 0)
        {
            if (VehicleAnimHistories.TryGetValue(vehicleId, out var history) && history.HasValue)
            {
                var sameSample = Mathf.Abs((float)(now - history.Time)) < 1e-6f &&
                                 (position - history.Position).sqrMagnitude < 1e-6f;
                if (sameSample)
                    return history.Speed;

                var dt = now - history.Time;
                if (dt > 1e-4)
                {
                    var distance = (position - history.Position).magnitude;
                    var derived = distance / (float)dt;
                    if (derived > speed)
                        speed = derived;

                }
            }

            VehicleAnimHistories[vehicleId] = new VehicleAnimHistory
            {
                Position = position,
                Time = now,
                Speed = speed,
                HasValue = true
            };
        }

        return speed;
    }

    private static AnimSample BuildVehicleAnimSample(int vehicleId, AISyncEntry entry, VehicleTransformSyncRpc message)
    {
        var speed = ResolveSyncSpeed(vehicleId, message.Position, message.Velocity, message.Timestamp);
        var moving = speed > MinMovingSpeed;
        var vehicleType = entry.VehicleAnimationType > 0 ? entry.VehicleAnimationType : 1;

        return new AnimSample
        {
            t = message.Timestamp > 0d ? message.Timestamp : Time.unscaledTimeAsDouble,
            speed = moving ? Mathf.Clamp(speed, 0f, 6f) : 0f,
            dirX = 0f,
            dirY = moving ? 1f : 0f,
            hand = 0,
            vehicleType = vehicleType,
            gunReady = false,
            dashing = false,
            attack = false,
            stateHash = 0,
            normTime = 0f
        };
    }
}
