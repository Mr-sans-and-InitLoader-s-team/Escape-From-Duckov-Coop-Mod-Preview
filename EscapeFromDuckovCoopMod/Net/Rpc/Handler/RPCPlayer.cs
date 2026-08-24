using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;

namespace EscapeFromDuckovCoopMod
{
    public static class RPCPlayer
    {
        private const float PlayerPositionBackTime = 0.04f;
        private const float PlayerPositionBufferMultiplier = 0.5f;
        private const float PlayerPositionMaxExtrapolate = 0.24f;
        private const float PlayerPositionHardSnapDistance = 4f;
        private const float PlayerPositionCatchupSpeed = 0.3f;
        private const float PlayerPositionCatchupThreshold = 0.04f;
        private const float PlayerAdaptiveMaxBackTime = 0.18f;
        private const float PlayerAdaptiveIntervalMultiplier = 1.25f;
        private const float PlayerAdaptiveJitterMultiplier = 3f;
        private const float PlayerAnimationBackTime = 0.04f;
        private const float PlayerAnimationMaxExtrapolate = 0.14f;
        private const float PlayerAnimationSmoothTime = 0.025f;
        private const float PlayerAnimationMinHoldTime = 0.03f;
        private const float PlayerAnimationCrossfade = 0.03f;
        private const float PlayerAnimationSwitchConfirm = 0.02f;

        private static readonly Dictionary<string, int> _playerVehicleBindings = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, float> _nextMountedPoseLogTimes = new(StringComparer.Ordinal);
        private static float _nextPlayerClockSkewLogTime;

        public static NetInterpolator AttachPlayerPositionInterpolator(GameObject go)
        {
            var interp = NetInterpUtil.Attach(go);
            if (interp == null)
                return null;

            interp.interpolationBackTime = PlayerPositionBackTime;
            interp.bufferTimeMultiplier = PlayerPositionBufferMultiplier;
            interp.maxExtrapolate = PlayerPositionMaxExtrapolate;
            interp.hardSnapDistance = PlayerPositionHardSnapDistance;
            interp.sendInterval = Mathf.Clamp(NetService.Instance?.syncInterval ?? 0.015f, 0.01f, 0.05f);
            interp.useGlobalAiInterval = false;
            interp.adaptiveBackTime = true;
            interp.adaptiveMinBackTime = PlayerPositionBackTime;
            interp.adaptiveMaxBackTime = PlayerAdaptiveMaxBackTime;
            interp.adaptiveIntervalMultiplier = PlayerAdaptiveIntervalMultiplier;
            interp.adaptiveJitterMultiplier = PlayerAdaptiveJitterMultiplier;
            interp.catchupSpeed = PlayerPositionCatchupSpeed;
            interp.catchupNegativeThreshold = -PlayerPositionCatchupThreshold;
            interp.catchupPositiveThreshold = PlayerPositionCatchupThreshold;
            interp.driveModelPosition = false;
            return interp;
        }

        public static AnimParamInterpolator AttachPlayerAnimationInterpolator(GameObject go)
        {
            var interp = AnimInterpUtil.Attach(go);
            if (interp == null)
                return null;

            interp.interpolationBackTime = PlayerAnimationBackTime;
            interp.maxExtrapolate = PlayerAnimationMaxExtrapolate;
            interp.paramSmoothTime = PlayerAnimationSmoothTime;
            interp.minHoldTime = PlayerAnimationMinHoldTime;
            interp.crossfadeDuration = PlayerAnimationCrossfade;
            interp.stateSwitchConfirmTime = PlayerAnimationSwitchConfirm;
            return interp;
        }

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
            st.SteamName = service.ResolvePeerSteamName(peer, message.Player.SteamName);
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
                HostSpawnAndLoadoutAsync(peer, st).Forget();
            }
            else if (message.Player.IsInGame)
            {
                if (service.remoteCharacters.TryGetValue(peer, out var go) && go != null)
                {
                    HealthBarNameDisplay.TryRefreshRemoteCharacter(go, playerId, st.SteamName);

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

        private static async UniTask HostSpawnAndLoadoutAsync(NetPeer peer, PlayerStatus st)
        {
            var remote = await CreateRemoteCharacter.CreateRemoteCharacterAsync(peer, st.Position, st.Rotation, st.CustomFaceJson);
            if (remote == null)
                remote = await WaitForHostRemoteAsync(peer);
            if (remote == null)
                return;

            var equipmentList = st.EquipmentList ?? new List<EquipmentSyncData>();
            var weaponList = st.WeaponList ?? new List<WeaponSyncData>();

            foreach (var e in equipmentList)
                COOPManager.HostPlayer_Apply.ApplyEquipmentUpdate(peer, e.SlotHash, e.ItemId).Forget();
            foreach (var w in weaponList)
                COOPManager.HostPlayer_Apply.ApplyWeaponUpdate(peer, w.SlotHash, w.ItemId, w.Snapshot).Forget();
        }

        private static async UniTask<GameObject> WaitForHostRemoteAsync(NetPeer peer)
        {
            var service = NetService.Instance;
            if (service == null || peer == null)
                return null;

            for (var i = 0; i < 20; i++)
            {
                if (service.remoteCharacters.TryGetValue(peer, out var remote) && remote != null)
                    return remote;

                await UniTask.Delay(100);
            }

            CoopPerfLog.AppendEvent("loadout", $"host spawn wait timeout peer={peer.EndPoint}");
            return null;
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
                st.SteamName = payload.SteamName;
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
                {
                    CustomFace.Client_ApplyFaceIfAvailable(st.EndPoint, existing, st.CustomFaceJson);
                    HealthBarNameDisplay.TryRefreshRemoteCharacter(existing, st.EndPoint, st.SteamName);
                }

                if (payload.IsInGame && (!service.clientRemoteCharacters.TryGetValue(payload.PlayerId, out var remote) || remote == null))
                {
                    HandleClientSpawnAndLoadoutAsync(service, payload, st).Forget();
                    continue;
                }

                if (payload.IsInGame && service.clientRemoteCharacters.TryGetValue(payload.PlayerId, out var remoteObj) && remoteObj != null)
                {
                    HealthBarNameDisplay.TryRefreshRemoteCharacter(remoteObj, payload.PlayerId, st.SteamName);

                    if (!IsMountedPlayer(payload.PlayerId))
                    {
                        var ni = AttachPlayerPositionInterpolator(remoteObj);
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
                    var mounted = message.VehicleId != 0 || IsMountedPlayer(playerId);
                    if (!mounted)
                    {
                        var ni = AttachPlayerPositionInterpolator(go);
                        PushPlayerPosition(ni, position, rotation, message.Velocity, message.Timestamp, playerId);
                    }
                    else
                    {
                        ApplyMountedNetworkPose(playerId, go, message.VehicleId, position, rotation, message.Timestamp, message.Velocity);
                    }
                }

                var broadcast = message;
                broadcast.EndPoint = playerId;
                broadcast.Forward = forward;
                broadcast.Position = position;
                broadcast.VehicleId = message.VehicleId;
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
                var mounted = message.VehicleId != 0 || IsMountedPlayer(message.EndPoint);
                if (!mounted)
                {
                    var ni = AttachPlayerPositionInterpolator(remote);
                    PushPlayerPosition(ni, clientStatus.Position, clientStatus.Rotation, message.Velocity, message.Timestamp, message.EndPoint);

                    var cmc = remote.GetComponentInChildren<CharacterMainControl>(true);
                    if (cmc && cmc.modelRoot)
                    {
                        var euler = clientStatus.Rotation.eulerAngles;
                        cmc.modelRoot.transform.rotation = Quaternion.Euler(0f, euler.y, 0f);
                    }
                }
                else
                {
                    ApplyMountedNetworkPose(message.EndPoint, remote, message.VehicleId, clientStatus.Position, clientStatus.Rotation, message.Timestamp, message.Velocity);
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
                    var ai = AttachPlayerAnimationInterpolator(remote);
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

            var anim = AttachPlayerAnimationInterpolator(remoteObj);
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

        public static int CountRemoteRidersOnVehicle(int vehicleId)
        {
            if (vehicleId == 0)
                return 0;

            var count = 0;
            foreach (var kvp in _playerVehicleBindings)
            {
                if (kvp.Value == vehicleId && IsActivePlayerId(kvp.Key))
                    count++;
            }

            return count;
        }

        public static bool TryGetFirstRiderOnVehicle(int vehicleId, string exceptPlayerId, out string playerId)
        {
            playerId = null;
            if (vehicleId == 0)
                return false;

            foreach (var kvp in _playerVehicleBindings)
            {
                if (kvp.Value != vehicleId)
                    continue;

                if (!IsActivePlayerId(kvp.Key))
                    continue;

                if (!string.IsNullOrEmpty(exceptPlayerId) && string.Equals(kvp.Key, exceptPlayerId, StringComparison.Ordinal))
                    continue;

                playerId = kvp.Key;
                return !string.IsNullOrEmpty(playerId);
            }

            return false;
        }

        public static void ForgetVehicleRider(string playerId, bool notifyAuthority)
        {
            if (string.IsNullOrEmpty(playerId))
                return;

            if (!_playerVehicleBindings.TryGetValue(playerId, out var oldVehicleId))
                return;

            _playerVehicleBindings.Remove(playerId);
            _nextMountedPoseLogTimes.Remove(playerId);

            if (notifyAuthority && oldVehicleId != 0)
                RPCVehicle.ServerHandleVehicleAuthorityDismounted(oldVehicleId, playerId);
        }

        public static void ClearVehicleRiders()
        {
            _playerVehicleBindings.Clear();
            _nextMountedPoseLogTimes.Clear();
        }

        private static bool IsActivePlayerId(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
                return false;

            var service = NetService.Instance;
            if (service == null)
                return false;

            if (service.localPlayerStatus != null &&
                string.Equals(service.localPlayerStatus.EndPoint, playerId, StringComparison.Ordinal))
            {
                return true;
            }

            if (service.clientPlayerStatuses != null && service.clientPlayerStatuses.ContainsKey(playerId))
                return true;

            if (service.playerStatuses != null)
            {
                foreach (var kvp in service.playerStatuses)
                {
                    var status = kvp.Value;
                    if (status != null && string.Equals(status.EndPoint, playerId, StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
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

        private static void ApplyMountedNetworkPose(
            string playerId,
            GameObject riderObj,
            int preferredVehicleId,
            Vector3 position,
            Quaternion rotation,
            double timestamp,
            Vector3 velocity)
        {
            if (string.IsNullOrEmpty(playerId) || riderObj == null || !IsFinite(position))
                return;

            if (!IsUsableRotation(rotation))
                rotation = riderObj.transform.rotation;

            if (!TryResolveMountedVehicle(playerId, riderObj, preferredVehicleId, position, out var vehicleId, out var vehicle))
            {
                LogMountedNetworkPose(playerId, 0, position, "direct");
                ApplyDirectRiderPose(riderObj, position, rotation, timestamp, velocity);
                return;
            }

            var vehicleStatus = SendLocalVehicleStatus.Instance;
            if (vehicleStatus != null && !vehicleStatus.HasVehicleAuthority(vehicleId) && NetService.Instance != null && NetService.Instance.IsServer)
                RPCVehicle.ServerAssignVehicleAuthority(vehicleId, playerId);

            if (vehicleStatus != null && vehicleStatus.HasVehicleAuthority(vehicleId) && !vehicleStatus.IsAuthorityPlayer(vehicleId, playerId))
            {
                ApplyMountedPoseLikeControlOtherCharacter(riderObj, vehicle);
                EnsureMountedLock(riderObj, playerId, vehicle);
                LogMountedNetworkPose(playerId, vehicleId, position, "passenger");
                return;
            }

            if (RPCVehicle.HasRecentVehicleTransform(vehicleId))
            {
                ApplyMountedPoseLikeControlOtherCharacter(riderObj, vehicle);
                EnsureMountedLock(riderObj, playerId, vehicle);
                LogMountedNetworkPose(playerId, vehicleId, position, "vehicle-transform");
                return;
            }

            ApplyNetworkPoseToVehicle(vehicleId, vehicle, position, rotation, timestamp, velocity);
            ApplyMountedPoseLikeControlOtherCharacter(riderObj, vehicle);
            EnsureMountedLock(riderObj, playerId, vehicle);
            LogMountedNetworkPose(playerId, vehicleId, position, "vehicle");
        }

        private static bool TryResolveMountedVehicle(
            string playerId,
            GameObject riderObj,
            int preferredVehicleId,
            Vector3 networkPosition,
            out int vehicleId,
            out CharacterMainControl vehicle)
        {
            vehicleId = 0;
            vehicle = null;

            if (preferredVehicleId != 0)
            {
                vehicle = COOPManager.AI?.TryGetCharacter(preferredVehicleId);
                if (vehicle)
                {
                    vehicleId = preferredVehicleId;
                    _playerVehicleBindings[playerId] = vehicleId;
                    return true;
                }
            }

            if (_playerVehicleBindings.TryGetValue(playerId, out vehicleId) && vehicleId != 0)
            {
                vehicle = COOPManager.AI?.TryGetCharacter(vehicleId);
                if (vehicle)
                    return true;
            }

            vehicleId = FindNearestVehicleId(networkPosition, 24f);
            if (vehicleId == 0 && riderObj != null)
                vehicleId = FindNearestVehicleId(riderObj.transform.position, 24f);

            if (vehicleId == 0)
                return false;

            vehicle = COOPManager.AI?.TryGetCharacter(vehicleId);
            if (!vehicle)
                return false;

            _playerVehicleBindings[playerId] = vehicleId;
            return true;
        }

        private static void LogMountedNetworkPose(string playerId, int vehicleId, Vector3 position, string mode)
        {
            if (string.IsNullOrEmpty(playerId))
                return;

            var now = Time.unscaledTime;
            if (_nextMountedPoseLogTimes.TryGetValue(playerId, out var next) && now < next)
                return;

            _nextMountedPoseLogTimes[playerId] = now + 3f;
            CoopPerfLog.AppendEvent(
                "mounted-net",
                $"player={playerId} vehicle={vehicleId} mode={mode} pos={position.x:0.0},{position.y:0.0},{position.z:0.0}");
        }

        private static void ApplyNetworkPoseToVehicle(
            int vehicleId,
            CharacterMainControl vehicle,
            Vector3 position,
            Quaternion rotation,
            double timestamp,
            Vector3 velocity)
        {
            if (!vehicle || !IsFinite(position))
                return;

            if (!IsUsableRotation(rotation))
                rotation = vehicle.transform.rotation;

            if (CoopSyncDatabase.AI.TryGet(vehicleId, out var entry) && entry != null && entry.IsVehicle)
            {
                entry.LastKnownPosition = position;
                entry.LastKnownRotation = rotation;
                entry.LastKnownVelocity = velocity;
                entry.LastKnownRemoteTime = timestamp;
                entry.LastStateReceivedTime = Time.unscaledTime;
            }

            var interp = NetInterpUtil.Attach(vehicle.gameObject);
            if (interp != null)
            {
                interp.enabled = true;
                interp.driveModelPosition = true;
                interp.interpolationBackTime = 0.08f;
                interp.maxExtrapolate = 0.18f;
                interp.hardSnapDistance = 10f;
                interp.sendInterval = 0.05f;
                interp.Push(position, rotation, timestamp, velocity);
                return;
            }

            vehicle.transform.SetPositionAndRotation(position, rotation);
            if (vehicle.characterModel)
                vehicle.characterModel.transform.SetPositionAndRotation(position, rotation);
        }

        private static void ApplyDirectRiderPose(
            GameObject riderObj,
            Vector3 position,
            Quaternion rotation,
            double timestamp,
            Vector3 velocity)
        {
            var rider = riderObj.GetComponentInChildren<CharacterMainControl>();
            riderObj.transform.SetPositionAndRotation(position, rotation);
            if (rider)
            {
                rider.transform.SetPositionAndRotation(position, rotation);
                if (rider.modelRoot)
                    rider.modelRoot.transform.rotation = rotation;
            }

            var ni = AttachPlayerPositionInterpolator(riderObj);
            if (ni != null)
            {
                ni.enabled = true;
                ni.driveModelPosition = false;
                ni.PushArrival(position, rotation, velocity);
            }
        }

        private static void TryBindMountedRider(string playerId, GameObject riderObj, int vehicleType)
        {
            if (string.IsNullOrEmpty(playerId) || riderObj == null)
                return;

            if (vehicleType <= 0)
            {
                if (_playerVehicleBindings.TryGetValue(playerId, out var oldVehicleId) && oldVehicleId != 0)
                {
                    _playerVehicleBindings.Remove(playerId);
                    RPCVehicle.ServerHandleVehicleAuthorityDismounted(oldVehicleId, playerId);
                }

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

                ApplyKnownPlayerPoseAfterDismount(playerId, riderObj, rider);
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

        private static void ApplyKnownPlayerPoseAfterDismount(string playerId, GameObject riderObj, CharacterMainControl rider)
        {
            if (string.IsNullOrEmpty(playerId) || riderObj == null)
                return;

            if (!TryGetKnownPlayerPose(playerId, out var position, out var rotation, out var velocity))
                return;

            if (!IsFinite(position))
                return;

            if (!IsUsableRotation(rotation))
                rotation = rider != null && rider.modelRoot ? rider.modelRoot.transform.rotation : riderObj.transform.rotation;

            riderObj.transform.SetPositionAndRotation(position, rotation);

            if (rider)
            {
                rider.transform.SetPositionAndRotation(position, rotation);
                if (rider.modelRoot)
                    rider.modelRoot.transform.rotation = rotation;
            }

            var ni = AttachPlayerPositionInterpolator(riderObj);
            if (ni != null)
            {
                ni.enabled = true;
                ni.driveModelPosition = false;
                ni.PushArrival(position, rotation, velocity);
            }
        }

        private static bool TryGetKnownPlayerPose(string playerId, out Vector3 position, out Quaternion rotation, out Vector3 velocity)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            velocity = Vector3.zero;

            var service = NetService.Instance;
            if (service == null || string.IsNullOrEmpty(playerId))
                return false;

            if (service.clientPlayerStatuses != null &&
                service.clientPlayerStatuses.TryGetValue(playerId, out var clientStatus) &&
                TryReadPose(clientStatus, out position, out rotation, out velocity))
            {
                return true;
            }

            if (service.playerStatuses != null)
            {
                foreach (var kvp in service.playerStatuses)
                {
                    var status = kvp.Value;
                    if (status == null || !string.Equals(status.EndPoint, playerId, StringComparison.Ordinal))
                        continue;

                    if (TryReadPose(status, out position, out rotation, out velocity))
                        return true;
                }
            }

            return service.localPlayerStatus != null &&
                   string.Equals(service.localPlayerStatus.EndPoint, playerId, StringComparison.Ordinal) &&
                   TryReadPose(service.localPlayerStatus, out position, out rotation, out velocity);
        }

        private static void PushPlayerPosition(
            NetInterpolator interp,
            Vector3 position,
            Quaternion rotation,
            Vector3 velocity,
            double remoteTimestamp,
            string playerId)
        {
            if (interp == null)
                return;

            LogPlayerClockSkew(playerId, remoteTimestamp);
            interp.PushArrival(position, rotation, velocity);
        }

        private static void LogPlayerClockSkew(string playerId, double remoteTimestamp)
        {
            if (remoteTimestamp <= 0d)
                return;

            var skew = Time.unscaledTimeAsDouble - remoteTimestamp;
            if (Math.Abs(skew) < 1d || Time.unscaledTime < _nextPlayerClockSkewLogTime)
                return;

            _nextPlayerClockSkewLogTime = Time.unscaledTime + 5f;
            CoopPerfLog.AppendEvent(
                "player-net",
                $"clockSkew player={playerId} skew={skew:0.000}s usingArrivalTime=True");
        }

        private static bool TryReadPose(PlayerStatus status, out Vector3 position, out Quaternion rotation, out Vector3 velocity)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            velocity = Vector3.zero;

            if (status == null || !IsFinite(status.Position))
                return false;

            position = status.Position;
            rotation = IsUsableRotation(status.Rotation) ? status.Rotation : Quaternion.identity;
            velocity = IsFinite(status.Velocity) ? status.Velocity : Vector3.zero;
            return true;
        }

        private static bool IsUsableRotation(Quaternion rotation)
        {
            return !(float.IsNaN(rotation.x) || float.IsNaN(rotation.y) || float.IsNaN(rotation.z) || float.IsNaN(rotation.w) ||
                     float.IsInfinity(rotation.x) || float.IsInfinity(rotation.y) || float.IsInfinity(rotation.z) || float.IsInfinity(rotation.w)) &&
                   rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w > 1e-6f;
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
