using System;
using System.Collections.Generic;
using LiteNetLib;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public static class RPCVehicle
{
    private const float MaxVehicleSyncDistance = 20f;
    private const float MinMovingSpeed = 0.08f;
    private const float RecentVehicleTransformWindow = 0.75f;
    private static readonly Dictionary<int, VehicleAnimHistory> VehicleAnimHistories = new();
    private static readonly Dictionary<int, float> RecentVehicleTransformTimes = new();
    private static readonly Dictionary<int, ItemSnapshot> VehicleItemSnapshots = new();

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

            var vehicleStatus = SendLocalVehicleStatus.Instance;
            if (vehicleStatus != null)
            {
                if (!vehicleStatus.HasVehicleAuthority(message.VehicleId))
                    ServerAssignVehicleAuthority(message.VehicleId, playerId);
                else if (!vehicleStatus.IsAuthorityPlayer(message.VehicleId, playerId))
                    return;
            }

            if (service.playerStatuses != null && service.playerStatuses.TryGetValue(context.Sender, out var status) && status != null)
            {
                var distSqr = (status.Position - message.Position).sqrMagnitude;
                if (distSqr > MaxVehicleSyncDistance * MaxVehicleSyncDistance)
                    return;
            }

            message.PlayerId = playerId;
            RecordVehicleTransform(message.VehicleId);

            entry.LastKnownPosition = message.Position;
            entry.LastKnownRotation = message.Rotation;
            entry.LastKnownVelocity = message.Velocity;
            entry.LastKnownRemoteTime = message.Timestamp;
            entry.LastStateReceivedTime = Time.unscaledTime;
            entry.LastAnimSample = BuildVehicleAnimSample(message.VehicleId, entry, message);

            var vehicle = COOPManager.AI?.TryGetCharacter(message.VehicleId);
            if (vehicle)
            {
                var interp = NetInterpUtil.Attach(vehicle.gameObject);
                if (interp != null)
                {
                    interp.enabled = true;
                    interp.driveModelPosition = true;
                    interp.interpolationBackTime = 0.1f;
                    interp.maxExtrapolate = 0.18f;
                    interp.hardSnapDistance = 12f;
                    interp.sendInterval = 0.05f;
                    interp.Push(message.Position, message.Rotation, message.Timestamp, message.Velocity);
                }
                else
                {
                    vehicle.transform.SetPositionAndRotation(message.Position, message.Rotation);
                    if (vehicle.characterModel)
                        vehicle.characterModel.transform.SetPositionAndRotation(message.Position, message.Rotation);
                }
                EnsureVehicleAnimatorDriver(vehicle, entry);
            }

            CoopTool.SendRpc(in message, context.Sender);
            return;
        }

        if (service.IsSelfId(message.PlayerId)) return;

        RecordVehicleTransform(message.VehicleId);

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

    public static void HandleVehicleControlRequest(RpcContext context, VehicleControlRequestRpc message)
    {
        var service = context.Service;
        if (service == null) return;

        if (context.IsServer)
        {
            var requesterId = service.GetPlayerId(context.Sender);
            ServerHandleVehicleControlRequest(message.VehicleId, requesterId, message.ClaimOnly);
            return;
        }

        if (message.ClaimOnly)
            return;

        SendLocalVehicleStatus.Instance?.SetPendingAuthorityRequest(message.VehicleId, message.RequesterId);
        MModUI.ShowTip(CoopLocalization.Get("vehicle.control.tip.incomingRequest", ResolvePlayerDisplayName(message.RequesterId)), 5f);
    }

    public static void HandleVehicleControlDecision(RpcContext context, VehicleControlDecisionRpc message)
    {
        var service = context.Service;
        if (service == null || !context.IsServer) return;

        var approverId = service.GetPlayerId(context.Sender);
        ServerHandleVehicleControlDecision(message.VehicleId, message.RequesterId, approverId, message.Approved);
    }

    public static void HandleVehicleAuthorityState(RpcContext context, VehicleAuthorityStateRpc message)
    {
        if (context.IsServer) return;

        var vehicleStatus = SendLocalVehicleStatus.Instance;
        if (vehicleStatus == null) return;

        if (string.IsNullOrEmpty(message.AuthorityPlayerId))
            vehicleStatus.ClearVehicleAuthority(message.VehicleId);
        else
            vehicleStatus.SetVehicleAuthority(message.VehicleId, message.AuthorityPlayerId, string.IsNullOrEmpty(message.PendingRequesterId));

        vehicleStatus.SetPendingAuthorityRequest(message.VehicleId, message.PendingRequesterId);
    }

    public static void HandleVehicleItemState(RpcContext context, VehicleItemStateRpc message)
    {
        var service = context.Service;
        if (service == null || message.VehicleId == 0 || message.Snapshot.TypeId == 0)
            return;

        if (context.IsServer)
        {
            var playerId = service.GetPlayerId(context.Sender);
            ServerHandleVehicleItemState(message.VehicleId, playerId, message.Snapshot, context.Sender);
            return;
        }

        if (service.IsSelfId(message.PlayerId))
            return;

        ApplyVehicleItemSnapshot(message.VehicleId, message.Snapshot);
    }

    public static void RequestVehicleControl(int vehicleId, bool claimOnly = false)
    {
        var service = NetService.Instance;
        if (service == null || vehicleId == 0) return;

        if (service.IsServer)
        {
            ServerHandleVehicleControlRequest(vehicleId, service.GetPlayerId(null), claimOnly);
            return;
        }

        var rpc = new VehicleControlRequestRpc { VehicleId = vehicleId, ClaimOnly = claimOnly };
        CoopTool.SendRpc(in rpc);
    }

    public static void ApproveVehicleControl(int vehicleId, string requesterId)
    {
        var service = NetService.Instance;
        if (service == null || vehicleId == 0 || string.IsNullOrEmpty(requesterId)) return;

        if (service.IsServer)
        {
            ServerHandleVehicleControlDecision(vehicleId, requesterId, service.GetPlayerId(null), true);
            return;
        }

        var rpc = new VehicleControlDecisionRpc
        {
            VehicleId = vehicleId,
            RequesterId = requesterId,
            Approved = true
        };
        CoopTool.SendRpc(in rpc);
    }

    public static void SendVehicleItemState(int vehicleId, ItemSnapshot snapshot)
    {
        var service = NetService.Instance;
        if (service == null || vehicleId == 0 || snapshot.TypeId == 0)
            return;

        var playerId = service.GetPlayerId(null);
        if (service.IsServer)
        {
            ServerHandleVehicleItemState(vehicleId, playerId, snapshot, null);
            return;
        }

        var rpc = new VehicleItemStateRpc
        {
            VehicleId = vehicleId,
            PlayerId = playerId,
            Snapshot = snapshot
        };
        CoopTool.SendRpc(in rpc);
    }

    public static void ServerAssignVehicleAuthority(int vehicleId, string playerId)
    {
        var service = NetService.Instance;
        if (service == null || !service.IsServer || vehicleId == 0 || string.IsNullOrEmpty(playerId))
            return;

        var vehicleStatus = SendLocalVehicleStatus.Instance;
        if (vehicleStatus == null)
            return;

        vehicleStatus.SetVehicleAuthority(vehicleId, playerId, true);
        vehicleStatus.SetPendingAuthorityRequest(vehicleId, string.Empty);
        BroadcastVehicleAuthorityState(vehicleId);
        CoopPerfLog.AppendEvent("vehicle-authority", $"vehicle={vehicleId} authority={playerId}");
    }

    private static void ServerHandleVehicleItemState(int vehicleId, string playerId, ItemSnapshot snapshot, NetPeer sender)
    {
        var service = NetService.Instance;
        if (service == null || !service.IsServer || vehicleId == 0 || string.IsNullOrEmpty(playerId) || snapshot.TypeId == 0)
            return;

        if (!CoopSyncDatabase.AI.TryGet(vehicleId, out var entry) || entry == null || !entry.IsVehicle)
            return;

        if (!IsVehicleItemStateSenderAllowed(entry, playerId, sender, snapshot))
            return;

        ApplyVehicleItemSnapshot(vehicleId, snapshot);

        var rpc = new VehicleItemStateRpc
        {
            VehicleId = vehicleId,
            PlayerId = playerId,
            Snapshot = snapshot
        };
        CoopTool.SendRpc(in rpc, sender);
    }

    private static bool IsVehicleItemStateSenderAllowed(AISyncEntry entry, string playerId, NetPeer sender, ItemSnapshot snapshot)
    {
        if (entry == null || !entry.IsVehicle || snapshot.TypeId == 0)
            return false;

        var service = NetService.Instance;
        if (service == null)
            return false;

        var vehicleStatus = SendLocalVehicleStatus.Instance;
        if (vehicleStatus != null && vehicleStatus.IsAuthorityPlayer(entry.Id, playerId))
            return true;

        if (sender == null && service.IsSelfId(playerId))
            return true;

        var vehicle = COOPManager.AI?.TryGetCharacter(entry.Id);
        var item = vehicle ? vehicle.CharacterItem : null;
        if (item != null && item.TypeID != snapshot.TypeId)
            return false;

        return IsPlayerNearVehicle(entry, sender);
    }

    private static bool IsPlayerNearVehicle(AISyncEntry entry, NetPeer sender)
    {
        if (entry == null)
            return false;

        var service = NetService.Instance;
        if (service == null)
            return false;

        PlayerStatus status = null;
        if (sender != null && service.playerStatuses != null)
            service.playerStatuses.TryGetValue(sender, out status);

        if (status == null && sender == null)
            status = service.localPlayerStatus;

        if (status == null || !status.IsInGame)
            return false;

        var anchor = entry.LastKnownPosition != Vector3.zero ? entry.LastKnownPosition : entry.SpawnPosition;
        return (status.Position - anchor).sqrMagnitude <= MaxVehicleSyncDistance * MaxVehicleSyncDistance;
    }

    private static void ApplyVehicleItemSnapshot(int vehicleId, ItemSnapshot snapshot)
    {
        if (vehicleId == 0 || snapshot.TypeId == 0)
            return;

        VehicleItemSnapshots[vehicleId] = snapshot;

        var vehicle = COOPManager.AI?.TryGetCharacter(vehicleId);
        if (TryApplyVehicleItemSnapshot(vehicle, snapshot))
            SendLocalVehicleStatus.Instance?.RecordVehicleItemState(vehicleId, snapshot);
    }

    public static void TryApplyCachedVehicleItemState(int vehicleId, CharacterMainControl vehicle)
    {
        if (vehicleId == 0 || !vehicle)
            return;

        if (!VehicleItemSnapshots.TryGetValue(vehicleId, out var snapshot) || snapshot.TypeId == 0)
            return;

        if (TryApplyVehicleItemSnapshot(vehicle, snapshot))
            SendLocalVehicleStatus.Instance?.RecordVehicleItemState(vehicleId, snapshot);
    }

    private static bool TryApplyVehicleItemSnapshot(CharacterMainControl vehicle, ItemSnapshot snapshot)
    {
        var item = vehicle ? vehicle.CharacterItem : null;
        if (item == null || item.TypeID != snapshot.TypeId)
            return false;

        return ItemTool.ApplySnapshot(item, snapshot);
    }

    public static bool HasRecentVehicleTransform(int vehicleId, float window = RecentVehicleTransformWindow)
    {
        if (vehicleId == 0 || !RecentVehicleTransformTimes.TryGetValue(vehicleId, out var lastTime))
            return false;

        if (Time.unscaledTime - lastTime <= window)
            return true;

        RecentVehicleTransformTimes.Remove(vehicleId);
        return false;
    }

    private static void RecordVehicleTransform(int vehicleId)
    {
        if (vehicleId != 0)
            RecentVehicleTransformTimes[vehicleId] = Time.unscaledTime;
    }

    public static void ServerHandleVehicleAuthorityDismounted(int vehicleId, string playerId)
    {
        var service = NetService.Instance;
        var vehicleStatus = SendLocalVehicleStatus.Instance;
        if (service == null || !service.IsServer || vehicleStatus == null || vehicleId == 0 || string.IsNullOrEmpty(playerId))
            return;

        if (!vehicleStatus.IsAuthorityPlayer(vehicleId, playerId))
            return;

        if (RPCPlayer.TryGetFirstRiderOnVehicle(vehicleId, playerId, out var nextRider))
        {
            ServerAssignVehicleAuthority(vehicleId, nextRider);
            return;
        }

        if (vehicleStatus.TryGetLocalVehicleInfo(out var localVehicleId, out _) &&
            localVehicleId == vehicleId &&
            !service.IsSelfId(playerId))
        {
            ServerAssignVehicleAuthority(vehicleId, service.GetPlayerId(null));
            return;
        }

        vehicleStatus.ClearVehicleAuthority(vehicleId);
        BroadcastVehicleAuthorityState(vehicleId);
    }

    private static void ServerHandleVehicleControlRequest(int vehicleId, string requesterId, bool claimOnly)
    {
        var service = NetService.Instance;
        var vehicleStatus = SendLocalVehicleStatus.Instance;
        if (service == null || !service.IsServer || vehicleStatus == null || vehicleId == 0 || string.IsNullOrEmpty(requesterId))
            return;

        if (!CoopSyncDatabase.AI.TryGet(vehicleId, out var entry) || entry == null || !entry.IsVehicle)
            return;

        RPCPlayer.TryEnsurePrimaryVehicleRider(requesterId, vehicleId);

        var authorityId = vehicleStatus.GetVehicleAuthority(vehicleId);
        if (string.IsNullOrEmpty(authorityId))
        {
            ServerAssignVehicleAuthority(vehicleId, requesterId);
            return;
        }

        if (string.Equals(authorityId, requesterId, StringComparison.Ordinal))
        {
            BroadcastVehicleAuthorityState(vehicleId);
            return;
        }

        if (IsVehicleAuthorityUnavailable(vehicleId, authorityId))
        {
            ServerAssignVehicleAuthority(vehicleId, requesterId);
            CoopPerfLog.AppendEvent("vehicle-authority-recover", $"vehicle={vehicleId} requester={requesterId} previous={authorityId}");
            return;
        }

        if (claimOnly)
        {
            BroadcastVehicleAuthorityState(vehicleId);
            return;
        }

        vehicleStatus.SetPendingAuthorityRequest(vehicleId, requesterId);
        BroadcastVehicleAuthorityState(vehicleId);

        if (service.IsSelfId(authorityId))
        {
            MModUI.ShowTip(CoopLocalization.Get("vehicle.control.tip.incomingRequest", ResolvePlayerDisplayName(requesterId)), 5f);
        }
        else if (service.TryGetPeerByPlayerId(authorityId, out var authorityPeer))
        {
            var prompt = new VehicleControlRequestRpc
            {
                VehicleId = vehicleId,
                RequesterId = requesterId
            };
            CoopTool.SendRpcTo(authorityPeer, in prompt);
        }

        CoopPerfLog.AppendEvent("vehicle-control-request", $"vehicle={vehicleId} requester={requesterId} authority={authorityId}");
    }

    private static bool IsVehicleAuthorityUnavailable(int vehicleId, string authorityId)
    {
        if (vehicleId == 0 || string.IsNullOrEmpty(authorityId))
            return true;

        if (!IsKnownPlayerInGame(authorityId))
            return true;

        if (!RPCPlayer.IsPrimaryVehicleRider(authorityId, vehicleId))
            return true;

        return !TryResolvePlayerCharacter(authorityId, out var character) || IsCharacterDead(character);
    }

    private static bool IsKnownPlayerInGame(string playerId)
    {
        var service = NetService.Instance;
        if (service == null || string.IsNullOrEmpty(playerId))
            return false;

        var local = service.localPlayerStatus;
        if (local != null && string.Equals(local.EndPoint, playerId, StringComparison.Ordinal))
            return local.IsInGame;

        if (service.clientPlayerStatuses != null &&
            service.clientPlayerStatuses.TryGetValue(playerId, out var clientStatus) &&
            clientStatus != null)
        {
            return clientStatus.IsInGame;
        }

        if (service.playerStatuses != null)
        {
            foreach (var kvp in service.playerStatuses)
            {
                var status = kvp.Value;
                if (status != null && string.Equals(status.EndPoint, playerId, StringComparison.Ordinal))
                    return status.IsInGame;
            }
        }

        return false;
    }

    private static bool TryResolvePlayerCharacter(string playerId, out CharacterMainControl character)
    {
        character = null;
        var service = NetService.Instance;
        if (service == null || string.IsNullOrEmpty(playerId))
            return false;

        if (service.IsSelfId(playerId))
        {
            character = LevelManager.Instance != null
                ? LevelManager.Instance.MainCharacter
                : CharacterMainControl.Main;
            if (!character)
                character = CharacterMainControl.Main;
            return character != null;
        }

        if (service.remoteCharacters != null)
        {
            foreach (var kvp in service.remoteCharacters)
            {
                var peerId = service.GetPlayerId(kvp.Key);
                if (!string.Equals(peerId, playerId, StringComparison.Ordinal))
                    continue;

                character = kvp.Value ? kvp.Value.GetComponentInChildren<CharacterMainControl>(true) : null;
                return character != null;
            }
        }

        if (service.clientRemoteCharacters != null &&
            service.clientRemoteCharacters.TryGetValue(playerId, out var remote) &&
            remote)
        {
            character = remote.GetComponentInChildren<CharacterMainControl>(true);
            return character != null;
        }

        return false;
    }

    private static bool IsCharacterDead(CharacterMainControl character)
    {
        if (!character)
            return true;

        var health = character.Health;
        if (!health)
            return true;

        try
        {
            return health.IsDead || health.CurrentHealth <= 0.001f;
        }
        catch
        {
            return health.IsDead;
        }
    }

    private static void ServerHandleVehicleControlDecision(int vehicleId, string requesterId, string approverId, bool approved)
    {
        var vehicleStatus = SendLocalVehicleStatus.Instance;
        if (vehicleStatus == null || vehicleId == 0 || string.IsNullOrEmpty(requesterId) || string.IsNullOrEmpty(approverId))
            return;

        var authorityId = vehicleStatus.GetVehicleAuthority(vehicleId);
        if (!string.Equals(authorityId, approverId, StringComparison.Ordinal))
            return;

        var pending = vehicleStatus.GetPendingAuthorityRequester(vehicleId);
        if (!string.Equals(pending, requesterId, StringComparison.Ordinal))
            return;

        if (approved)
            ServerAssignVehicleAuthority(vehicleId, requesterId);
        else
        {
            vehicleStatus.SetPendingAuthorityRequest(vehicleId, string.Empty);
            BroadcastVehicleAuthorityState(vehicleId);
        }
    }

    private static void BroadcastVehicleAuthorityState(int vehicleId)
    {
        var service = NetService.Instance;
        var vehicleStatus = SendLocalVehicleStatus.Instance;
        if (service == null || !service.IsServer || vehicleStatus == null || vehicleId == 0)
            return;

        var rpc = new VehicleAuthorityStateRpc
        {
            VehicleId = vehicleId,
            AuthorityPlayerId = vehicleStatus.GetVehicleAuthority(vehicleId),
            PendingRequesterId = vehicleStatus.GetPendingAuthorityRequester(vehicleId)
        };
        CoopTool.SendRpc(in rpc);
    }

    public static string ResolvePlayerDisplayName(string playerId)
    {
        var service = NetService.Instance;
        if (service == null || string.IsNullOrEmpty(playerId))
            return "Unknown";

        if (service.localPlayerStatus != null &&
            string.Equals(service.localPlayerStatus.EndPoint, playerId, StringComparison.Ordinal) &&
            !string.IsNullOrEmpty(service.localPlayerStatus.PlayerName))
        {
            return service.localPlayerStatus.PlayerName;
        }

        if (service.clientPlayerStatuses != null &&
            service.clientPlayerStatuses.TryGetValue(playerId, out var clientStatus) &&
            clientStatus != null &&
            !string.IsNullOrEmpty(clientStatus.PlayerName))
        {
            return clientStatus.PlayerName;
        }

        if (service.playerStatuses != null)
        {
            foreach (var kvp in service.playerStatuses)
            {
                var status = kvp.Value;
                if (status == null || !string.Equals(status.EndPoint, playerId, StringComparison.Ordinal))
                    continue;

                if (!string.IsNullOrEmpty(status.PlayerName))
                    return status.PlayerName;
            }
        }

        return playerId;
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
