using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;

namespace EscapeFromDuckovCoopMod
{
    public static class RPCPlayer
    {
        public static void HandleClientStatusUpdate(RpcContext context, ClientStatusUpdateRpc message)
        {
            var service = context.Service;
            if (service == null || !context.IsServer)
                return;

            var peer = context.Sender;
            var playerId = service.GetPlayerId(peer);

            if (!service.playerStatuses.TryGetValue(peer, out var st))
                st = service.playerStatuses[peer] = new PlayerStatus();

            st.EndPoint = playerId;

            st.PlayerName = message.Player.PlayerName;
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
                foreach (var w in st.WeaponList) COOPManager.HostPlayer_Apply.ApplyWeaponUpdate(peer, w.SlotHash, w.ItemId).Forget();
            }
            else if (message.Player.IsInGame)
            {
                if (service.remoteCharacters.TryGetValue(peer, out var go) && go != null)
                {
                    go.transform.position = st.Position;
                    go.GetComponentInChildren<CharacterMainControl>().modelRoot.transform.rotation = st.Rotation;
                }

                foreach (var e in st.EquipmentList) COOPManager.HostPlayer_Apply.ApplyEquipmentUpdate(peer, e.SlotHash, e.ItemId).Forget();
                foreach (var w in st.WeaponList) COOPManager.HostPlayer_Apply.ApplyWeaponUpdate(peer, w.SlotHash, w.ItemId).Forget();
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
                    var ni = NetInterpUtil.Attach(remoteObj);
                    ni?.Push(st.Position, st.Rotation);

                    CoopTool.Client_ApplyPendingRemoteIfAny(payload.PlayerId, remoteObj);

                    foreach (var e in st.EquipmentList) COOPManager.ClientPlayer_Apply.ApplyEquipmentUpdate_Client(payload.PlayerId, e.SlotHash, e.ItemId).Forget();
                    foreach (var w in st.WeaponList) COOPManager.ClientPlayer_Apply.ApplyWeaponUpdate_Client(payload.PlayerId, w.SlotHash, w.ItemId).Forget();
                }
            }
        }

        private static async UniTask HandleClientSpawnAndLoadoutAsync(NetService service, PlayerStatusPayload payload, PlayerStatus st)
        {
            await CreateRemoteCharacter.CreateRemoteCharacterForClient(payload.PlayerId, payload.Position, payload.Rotation, payload.CustomFaceJson);

            if (service.clientRemoteCharacters.TryGetValue(payload.PlayerId, out var remote) && remote != null)
            {
                CoopTool.Client_ApplyPendingRemoteIfAny(payload.PlayerId, remote);

                foreach (var e in st.EquipmentList) COOPManager.ClientPlayer_Apply.ApplyEquipmentUpdate_Client(payload.PlayerId, e.SlotHash, e.ItemId).Forget();
                foreach (var w in st.WeaponList) COOPManager.ClientPlayer_Apply.ApplyWeaponUpdate_Client(payload.PlayerId, w.SlotHash, w.ItemId).Forget();
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
                    var ni = NetInterpUtil.Attach(go);
                    ni?.Push(position, rotation, message.Timestamp, message.Velocity);
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

                COOPManager.HostPlayer_Apply.ApplyWeaponUpdate(context.Sender, message.SlotHash, message.ItemId).Forget();

                if (service.playerStatuses.TryGetValue(context.Sender, out var st))
                    UpsertWeapon(st.WeaponList, message.SlotHash, message.ItemId);

                var broadcast = message;
                broadcast.PlayerId = playerId;
                CoopTool.SendRpc(in broadcast, context.Sender);
                return;
            }

            if (service.IsSelfId(message.PlayerId)) return;

            if (service.clientPlayerStatuses.TryGetValue(message.PlayerId, out var clientStatus) && clientStatus.WeaponList != null)
                UpsertWeapon(clientStatus.WeaponList, message.SlotHash, message.ItemId);

            COOPManager.ClientPlayer_Apply.ApplyWeaponUpdate_Client(message.PlayerId, message.SlotHash, message.ItemId).Forget();
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

        private static void UpsertWeapon(List<WeaponSyncData> weapons, int slotHash, string itemId)
        {
            if (weapons == null) return;

            for (var i = 0; i < weapons.Count; i++)
            {
                if (weapons[i].SlotHash != slotHash) continue;
                weapons[i].ItemId = itemId;
                return;
            }

            weapons.Add(new WeaponSyncData { SlotHash = slotHash, ItemId = itemId });
        }



    }
}
