using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;

namespace EscapeFromDuckovCoopMod
{
    public static class RPCPlayer
    {
        private static readonly Dictionary<string, int> _playerVehicleBindings = new(StringComparer.Ordinal);
        public static void HandleClientStatusUpdate(RpcContext context, ClientStatusUpdateRpc message)
        {
            var service = context.Service;
            if (service == null || !context.IsServer)
                return;

            var peer = context.Sender;
            var playerId = !string.IsNullOrEmpty(message.Player.PlayerId)
                ? message.Player.PlayerId
                : service.GetPlayerId(peer);
            if (!string.IsNullOrEmpty(playerId))
            {
                foreach (var kvp in service.playerStatuses)
                {
                    if (kvp.Key == peer) continue;
                    if (kvp.Value != null && string.Equals(kvp.Value.EndPoint, playerId, StringComparison.Ordinal))
                    {
                        playerId = service.GetPlayerId(peer);
                        break;
                    }
                }
            }

            var clientVersion = message.ClientVersion;
            if (string.IsNullOrEmpty(clientVersion))
            {
                service.status = CoopLocalization.Get("net.clientVersionUnknown");
                Debug.LogWarning(service.status);
                MModUI.ShowTip(service.status);
                peer?.Disconnect();
                return;
            }

            if (!string.Equals(clientVersion, BuildInfo.ModVersion, StringComparison.Ordinal))
            {
                service.status = CoopLocalization.Get("net.clientVersionMismatch", clientVersion, BuildInfo.ModVersion);
                Debug.LogWarning(service.status);
                MModUI.ShowTip(service.status);
                peer?.Disconnect();
                return;
            }

            if (!service.playerStatuses.TryGetValue(peer, out var st))
                st = service.playerStatuses[peer] = new PlayerStatus();

            st.EndPoint = playerId;

            var resolvedName = message.Player.PlayerName;
            resolvedName = service.ResolvePeerDisplayName(peer, resolvedName);
            if (string.IsNullOrEmpty(resolvedName))
                resolvedName = $"Player_{peer?.Id ?? 0}";
            st.PlayerName = resolvedName;
            st.Latency = peer?.Ping ?? 0;
            st.IsInGame = message.Player.IsInGame;
            st.LastIsInGame = message.Player.IsInGame;
            st.Position = message.Player.Position;
            st.Rotation = message.Player.Rotation;
            if (!string.IsNullOrEmpty(message.Player.CustomFaceJson))
                st.CustomFaceJson = message.Player.CustomFaceJson;
            st.SceneId = message.Player.SceneId;
            st.EquipmentList = message.Player.Equipment != null ? new List<EquipmentSyncData>(message.Player.Equipment) : new List<EquipmentSyncData>();
            st.WeaponList = message.Player.Weapons != null ? new List<WeaponSyncData>(message.Player.Weapons) : new List<WeaponSyncData>();

            if (message.Player.IsInGame && !service.remoteCharacters.ContainsKey(peer))
            {
                CreateRemoteCharacter.CreateRemoteCharacterAsync(peer, st.Position, st.Rotation, st.CustomFaceJson).Forget();
                foreach (var e in st.EquipmentList) COOPManager.HostPlayer_Apply.ApplyEquipmentUpdate(peer, e.SlotHash, e.ItemId).Forget();
                foreach (var w in st.WeaponList) COOPManager.HostPlayer_Apply.ApplyWeaponUpdate(peer, w.SlotHash, w.ItemId, w.Snapshot).Forget();
            }
            else if (message.Player.IsInGame)
            {
                if (service.remoteCharacters.TryGetValue(peer, out var go) && go != null)
                {
                    var mounted = IsMountedPlayer(playerId);
                    if (!mounted)
                    {
                        go.transform.position = st.Position;
                        go.GetComponentInChildren<CharacterMainControl>().modelRoot.transform.rotation = st.Rotation;
                    }
                }

                foreach (var e in st.EquipmentList) COOPManager.HostPlayer_Apply.ApplyEquipmentUpdate(peer, e.SlotHash, e.ItemId).Forget();
                foreach (var w in st.WeaponList) COOPManager.HostPlayer_Apply.ApplyWeaponUpdate(peer, w.SlotHash, w.ItemId, w.Snapshot).Forget();
            }

            service.playerStatuses[peer] = st;

            SendLocalPlayerStatus.Instance.SendPlayerStatusUpdate();
        }

        public static void HandlePlayerStatusUpdate(RpcContext context, PlayerStatusUpdateRpc message)
        {
            var service = context.Service;
            if (service == null || context.IsServer)
                return;

            service.clientPlayerStatuses.Clear();

            for (var i = 0; i < message.Players.Length; i++)
            {
                var payload = message.Players[i];
                if (service.IsSelfId(payload.PlayerId))
                    continue;

                if (!service.clientPlayerStatuses.TryGetValue(payload.PlayerId, out var st))
                    st = service.clientPlayerStatuses[payload.PlayerId] = new PlayerStatus();

                st.EndPoint = payload.PlayerId;
                st.PlayerName = payload.PlayerName;
                st.Latency = payload.Latency;
                st.IsInGame = payload.IsInGame;
                st.LastIsInGame = payload.IsInGame;
                st.Position = payload.Position;
                st.Rotation = payload.Rotation;
                if (!string.IsNullOrEmpty(payload.CustomFaceJson))
                    st.CustomFaceJson = payload.CustomFaceJson;
                st.EquipmentList = payload.Equipment != null ? new List<EquipmentSyncData>(payload.Equipment) : new List<EquipmentSyncData>();
                st.WeaponList = payload.Weapons != null ? new List<WeaponSyncData>(payload.Weapons) : new List<WeaponSyncData>();

                if (!string.IsNullOrEmpty(payload.SceneId))
                {
                    st.SceneId = payload.SceneId;
                    SceneNet.Instance._cliLastSceneIdByPlayer[payload.PlayerId] = payload.SceneId;
                }

                if (service.clientRemoteCharacters.TryGetValue(st.EndPoint, out var existing) && existing != null)
                    CustomFace.Client_ApplyFaceIfAvailable(st.EndPoint, existing, st.CustomFaceJson);

                if (payload.IsInGame && (!service.clientRemoteCharacters.TryGetValue(payload.PlayerId, out var remote) || remote == null))
                {
                    HandleClientSpawnAndLoadoutAsync(service, payload, st).Forget();
                    continue;
                }

                if (payload.IsInGame && service.clientRemoteCharacters.TryGetValue(payload.PlayerId, out var remoteObj) && remoteObj != null)
                {
                    if (!IsMountedPlayer(payload.PlayerId))
                    {
                        var ni = NetInterpUtil.Attach(remoteObj);
                        ni?.Push(st.Position, st.Rotation);
                    }
                    else
                    {
                        RefreshMountedRiderPose(payload.PlayerId, remoteObj, "status");
                    }

                    CoopTool.Client_ApplyPendingRemoteIfAny(payload.PlayerId, remoteObj);

                    foreach (var e in st.EquipmentList) COOPManager.ClientPlayer_Apply.ApplyEquipmentUpdate_Client(payload.PlayerId, e.SlotHash, e.ItemId).Forget();
                    foreach (var w in st.WeaponList) COOPManager.ClientPlayer_Apply.ApplyWeaponUpdate_Client(payload.PlayerId, w.SlotHash, w.ItemId, w.Snapshot).Forget();
                }
            }
        }

        public static void HandleFriendlyFireState(RpcContext context, PlayerFriendlyFireStateRpc message)
        {
            if (context.IsServer) return;
            COOPManager.FriendlyFire?.Client_HandleState(message);
        }

        private static async UniTask HandleClientSpawnAndLoadoutAsync(NetService service, PlayerStatusPayload payload, PlayerStatus st)
        {
            await CreateRemoteCharacter.CreateRemoteCharacterForClient(payload.PlayerId, payload.Position, payload.Rotation, payload.CustomFaceJson);

            if (service.clientRemoteCharacters.TryGetValue(payload.PlayerId, out var remote) && remote != null)
            {
                CoopTool.Client_ApplyPendingRemoteIfAny(payload.PlayerId, remote);

                foreach (var e in st.EquipmentList) COOPManager.ClientPlayer_Apply.ApplyEquipmentUpdate_Client(payload.PlayerId, e.SlotHash, e.ItemId).Forget();
                foreach (var w in st.WeaponList) COOPManager.ClientPlayer_Apply.ApplyWeaponUpdate_Client(payload.PlayerId, w.SlotHash, w.ItemId, w.Snapshot).Forget();
            }
        }

        public static void HandlePlayerPositionUpdate(RpcContext context, PlayerPositionUpdateRpc message)
        {
            var service = context.Service;
            if (service == null) return;

            var position = message.Position;
            if (!IsFinite(position)) return;

            var forward = message.Forward;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            if (!IsFinite(forward)) forward = Vector3.forward;
            var rotation = Quaternion.LookRotation(forward, Vector3.up);

            if (context.IsServer)
            {
                var playerId = service.GetPlayerId(context.Sender);

                if (service.playerStatuses.TryGetValue(context.Sender, out var st))
                {
                    st.Position = message.Position;
                    st.Rotation = rotation;
                    st.Velocity = message.Velocity;
                }

                if (service.remoteCharacters.TryGetValue(context.Sender, out var go) && go != null)
                {
                    if (!IsMountedPlayer(playerId))
                    {
                        var ni = NetInterpUtil.Attach(go);
                        ni?.Push(position, rotation, message.Timestamp, message.Velocity);
                    }
                    else
                    {
                        RefreshMountedRiderPose(playerId, go, "hostPos");
                    }
                }

                var broadcast = message;
                broadcast.EndPoint = playerId;
                broadcast.Forward = forward;
                broadcast.Position = position;
                CoopTool.SendRpc(in broadcast, context.Sender);
                return;
            }

            if (service.IsSelfId(message.EndPoint)) return;

            if (!service.clientPlayerStatuses.TryGetValue(message.EndPoint, out var clientStatus))
            {
                clientStatus = service.clientPlayerStatuses[message.EndPoint] = new PlayerStatus
                {
                    EndPoint = message.EndPoint,
                    IsInGame = true
                };
            }

            clientStatus.Position = position;
            clientStatus.Rotation = rotation;
            clientStatus.Velocity = message.Velocity;

            if (service.clientRemoteCharacters.TryGetValue(message.EndPoint, out var remote) && remote != null)
            {
                if (!IsMountedPlayer(message.EndPoint))
                {
                    var ni = NetInterpUtil.Attach(remote);
                    ni?.Push(clientStatus.Position, clientStatus.Rotation, message.Timestamp, message.Velocity);

                    var cmc = remote.GetComponentInChildren<CharacterMainControl>(true);
                    if (cmc && cmc.modelRoot)
                    {
                        var euler = clientStatus.Rotation.eulerAngles;
                        cmc.modelRoot.transform.rotation = Quaternion.Euler(0f, euler.y, 0f);
                    }
                }
                else
                {
                    RefreshMountedRiderPose(message.EndPoint, remote, "clientPos");
                }
            }
            else
            {
                CreateRemoteCharacter.CreateRemoteCharacterForClient(
                    message.EndPoint,
                    clientStatus.Position,
                    clientStatus.Rotation,
                    clientStatus.CustomFaceJson).Forget();
            }
        }

        public static void HandleEquipmentUpdate(RpcContext context, EquipmentUpdateRpc message)
        {
            var service = context.Service;
            if (service == null) return;

            if (context.IsServer)
            {
                var playerId = service.GetPlayerId(context.Sender);

                COOPManager.HostPlayer_Apply.ApplyEquipmentUpdate(context.Sender, message.SlotHash, message.ItemId).Forget();

                if (service.playerStatuses.TryGetValue(context.Sender, out var st))
                    UpsertEquipment(st.EquipmentList, message.SlotHash, message.ItemId);

                var broadcast = message;
                broadcast.PlayerId = playerId;
                CoopTool.SendRpc(in broadcast, context.Sender);
                return;
            }

            if (service.IsSelfId(message.PlayerId)) return;

            if (service.clientPlayerStatuses.TryGetValue(message.PlayerId, out var clientStatus) && clientStatus.EquipmentList != null)
                UpsertEquipment(clientStatus.EquipmentList, message.SlotHash, message.ItemId);

            COOPManager.ClientPlayer_Apply.ApplyEquipmentUpdate_Client(message.PlayerId, message.SlotHash, message.ItemId).Forget();
        }

        public static void HandleWeaponUpdate(RpcContext context, WeaponUpdateRpc message)
        {
            var service = context.Service;
            if (service == null) return;

            if (context.IsServer)
            {
                var playerId = service.GetPlayerId(context.Sender);

                COOPManager.HostPlayer_Apply.ApplyWeaponUpdate(context.Sender, message.SlotHash, message.ItemId, message.Snapshot).Forget();

                if (service.playerStatuses.TryGetValue(context.Sender, out var st))
                    UpsertWeapon(st.WeaponList, message.SlotHash, message.ItemId, message.Snapshot);

                var broadcast = message;
                broadcast.PlayerId = playerId;
                CoopTool.SendRpc(in broadcast, context.Sender);
                return;
            }

            if (service.IsSelfId(message.PlayerId)) return;

            if (service.clientPlayerStatuses.TryGetValue(message.PlayerId, out var clientStatus) && clientStatus.WeaponList != null)
                UpsertWeapon(clientStatus.WeaponList, message.SlotHash, message.ItemId, message.Snapshot);

            COOPManager.ClientPlayer_Apply.ApplyWeaponUpdate_Client(message.PlayerId, message.SlotHash, message.ItemId, message.Snapshot).Forget();
        }

        private static bool IsFinite(Vector3 value)
        {
            return !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) ||
                     float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z));
        }

        public static void HandlePlayerAnimationSync(RpcContext context, PlayerAnimationSyncRpc message)
        {
            var service = context.Service;
            if (service == null) return;

            if (context.IsServer)
            {
                var playerId = service.GetPlayerId(context.Sender);

                if (service.remoteCharacters.TryGetValue(context.Sender, out var remote) && remote != null)
                {
                    var ai = AnimInterpUtil.Attach(remote);
                    ai?.Push(message.ToSample());
                    TryBindMountedRider(playerId, remote, message.VehicleType);
                }
                var broadcast = message;
                broadcast.PlayerId = playerId;
                CoopTool.SendRpc(in broadcast, context.Sender);
                return;
            }

            if (service.IsSelfId(message.PlayerId)) return;

            if (!service.clientRemoteCharacters.TryGetValue(message.PlayerId, out var remoteObj) || remoteObj == null)
                return;

            var anim = AnimInterpUtil.Attach(remoteObj);
            anim?.Push(message.ToSample());
            TryBindMountedRider(message.PlayerId, remoteObj, message.VehicleType);
        }

        public static bool TryGetPrimaryVehicleRider(int vehicleId, out string playerId)
        {
            if (vehicleId == 0)
            {
                playerId = null;
                return false;
            }

            foreach (var kvp in _playerVehicleBindings)
            {
                if (kvp.Value == vehicleId)
                {
                    playerId = kvp.Key;
                    return !string.IsNullOrEmpty(playerId);
                }
            }

            playerId = null;
            return false;
        }

        public static bool TryEnsurePrimaryVehicleRider(string playerId, int vehicleId)
        {
            if (string.IsNullOrEmpty(playerId) || vehicleId == 0)
                return false;

            if (_playerVehicleBindings.TryGetValue(playerId, out var bound) && bound == vehicleId)
                return true;

            _playerVehicleBindings[playerId] = vehicleId;
            return true;
        }

        public static bool IsPrimaryVehicleRider(string playerId, int vehicleId)
        {
            return !string.IsNullOrEmpty(playerId) &&
                   vehicleId != 0 &&
                   _playerVehicleBindings.TryGetValue(playerId, out var bound) &&
                   bound == vehicleId;
        }

        private static bool IsMountedPlayer(string playerId)
        {
            return !string.IsNullOrEmpty(playerId) &&
                   _playerVehicleBindings.TryGetValue(playerId, out var vehicleId) &&
                   vehicleId != 0;
        }

        private static void RefreshMountedRiderPose(string playerId, GameObject riderObj, string source)
        {
            if (string.IsNullOrEmpty(playerId) || riderObj == null)
                return;

            if (!_playerVehicleBindings.TryGetValue(playerId, out var vehicleId) || vehicleId == 0)
                return;

            var vehicle = COOPManager.AI?.TryGetCharacter(vehicleId);
            if (!vehicle)
                return;

            ApplyMountedPoseLikeControlOtherCharacter(riderObj, vehicle);
        }

        private static void TryBindMountedRider(string playerId, GameObject riderObj, int vehicleType)
        {
            if (string.IsNullOrEmpty(playerId) || riderObj == null)
                return;

            if (vehicleType <= 0)
            {
                if (_playerVehicleBindings.TryGetValue(playerId, out var oldVehicleId) && oldVehicleId != 0)
                _playerVehicleBindings.Remove(playerId);

                var rider = riderObj.GetComponentInChildren<CharacterMainControl>();
                if (rider)
                {
                    rider.ridingVehicleType = 0;
                    if (rider.movementControl != null)
                        rider.movementControl.MovementEnabled = true;
                }

                var lockComp = riderObj.GetComponent<MountedRiderLock>();
                if (lockComp != null)
                    lockComp.Unbind();
                return;
            }

            var hadBoundVehicle = _playerVehicleBindings.TryGetValue(playerId, out var vehicleId) && vehicleId != 0;
            if (!hadBoundVehicle)
            {
                vehicleId = FindNearestVehicleId(riderObj.transform.position, 10f);
                if (vehicleId == 0)
                    return;
                _playerVehicleBindings[playerId] = vehicleId;
            }

            var vehicle = COOPManager.AI?.TryGetCharacter(vehicleId);
            if (!vehicle)
                return;

            ApplyMountedPoseLikeControlOtherCharacter(riderObj, vehicle);
            EnsureMountedLock(riderObj, playerId, vehicle);
        }

        private static void EnsureMountedLock(GameObject riderObj, string playerId, CharacterMainControl vehicle)
        {
            if (riderObj == null || string.IsNullOrEmpty(playerId) || !vehicle)
                return;

            var rider = riderObj.GetComponentInChildren<CharacterMainControl>();
            if (!rider)
                return;

            var lockComp = riderObj.GetComponent<MountedRiderLock>();
            if (lockComp == null)
                lockComp = riderObj.AddComponent<MountedRiderLock>();

            lockComp.Bind(playerId, rider, vehicle);
        }

        private static void ApplyMountedPoseLikeControlOtherCharacter(GameObject riderObj, CharacterMainControl vehicle)
        {
            if (riderObj == null || !vehicle)
                return;

            var rider = riderObj.GetComponentInChildren<CharacterMainControl>();
            if (!rider)
                return;

            var socket = vehicle.VehicleSocket;
            if (socket)
            {
                rider.transform.position = socket.position;
                if (rider.modelRoot)
                    rider.modelRoot.transform.rotation = socket.rotation;
            }
            else
            {
                rider.transform.position = vehicle.transform.position;
                if (rider.modelRoot)
                    rider.modelRoot.transform.rotation = vehicle.transform.rotation;
            }

            rider.ridingVehicleType = vehicle.vehicleAnimationType;
            if (rider.movementControl != null)
                rider.movementControl.MovementEnabled = false;
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

        private static AnimSample ToSample(this PlayerAnimationSyncRpc message)
        {
            return new AnimSample
            {
                speed = message.MoveSpeed,
                dirX = message.MoveDirX,
                dirY = message.MoveDirY,
                dashing = message.IsDashing,
                attack = message.IsAttacking,
                hand = message.HandState,
                gunReady = message.GunReady,
                vehicleType = message.VehicleType,
                stateHash = message.StateHash,
                normTime = message.NormTime
            };
        }

        private static void UpsertEquipment(List<EquipmentSyncData> equipment, int slotHash, string itemId)
        {
            if (equipment == null) return;

            for (var i = 0; i < equipment.Count; i++)
            {
                if (equipment[i].SlotHash != slotHash) continue;
                equipment[i].ItemId = itemId;
                return;
            }

            equipment.Add(new EquipmentSyncData { SlotHash = slotHash, ItemId = itemId });
        }

        private static void UpsertWeapon(List<WeaponSyncData> weapons, int slotHash, string itemId, ItemSnapshot snapshot)
        {
            if (weapons == null) return;

            for (var i = 0; i < weapons.Count; i++)
            {
                if (weapons[i].SlotHash != slotHash) continue;
                weapons[i].ItemId = itemId;
                weapons[i].Snapshot = snapshot;
                return;
            }

            weapons.Add(new WeaponSyncData { SlotHash = slotHash, ItemId = itemId, Snapshot = snapshot });
        }



    }
}
