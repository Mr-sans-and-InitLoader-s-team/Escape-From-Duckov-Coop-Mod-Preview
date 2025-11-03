using LiteNetLib;
using LiteNetLib.Utils;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;

namespace EscapeFromDuckovCoopMod
{
    public class VoteSystemRPC : MonoBehaviour
    {
        public static VoteSystemRPC Instance { get; private set; }

        private SceneNet _sceneNet;
        private bool _rpcRegistered = false;

        public bool UseRPCMode { get; set; } = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            _sceneNet = SceneNet.Instance;
            if (_sceneNet == null)
            {
                Debug.LogError("[VoteSystemRPC] SceneNet.Instance is null");
                return;
            }

            RegisterRPCs();
        }

        private void RegisterRPCs()
        {
            var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
            if (rpcManager == null)
            {
                Debug.LogWarning("[VoteSystemRPC] HybridRPCManager not found, RPC mode disabled");
                return;
            }

            rpcManager.RegisterRPC("VoteStart", OnRPC_VoteStart);
            rpcManager.RegisterRPC("VoteRequest", OnRPC_VoteRequest);
            rpcManager.RegisterRPC("VoteCast", OnRPC_VoteCast);
            rpcManager.RegisterRPC("VoteReadySet", OnRPC_VoteReadySet);
            rpcManager.RegisterRPC("VoteBeginLoad", OnRPC_VoteBeginLoad);
            rpcManager.RegisterRPC("VoteCancel", OnRPC_VoteCancel);

            _rpcRegistered = true;
            Debug.Log("[VoteSystemRPC] All RPCs registered");
        }

        #region Server -> Client RPCs

        public void Server_StartVote(string targetSceneId, string curtainGuid, bool notifyEvac, bool saveToFile, bool useLocation, string locationName, List<ulong> participantSteamIds)
        {
            if (!UseRPCMode || !_rpcRegistered)
            {
                Debug.Log("[VoteSystemRPC] RPC mode disabled, using legacy method");
                return;
            }

            var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
            if (rpcManager == null || !rpcManager.IsServer)
                return;

            string hostSceneId = "";
            LocalPlayerManager.Instance.ComputeIsInGame(out hostSceneId);
            hostSceneId = hostSceneId ?? string.Empty;

            rpcManager.CallRPC("VoteStart", Net.HybridP2P.RPCTarget.AllClients, 0, (writer) =>
            {
                writer.Put(targetSceneId ?? "");
                writer.Put(curtainGuid ?? "");
                writer.Put(notifyEvac);
                writer.Put(saveToFile);
                writer.Put(useLocation);
                writer.Put(locationName ?? "");
                writer.Put(hostSceneId);

                writer.Put(participantSteamIds.Count);
                foreach (var steamId in participantSteamIds)
                {
                    writer.Put(steamId);
                }
            }, DeliveryMethod.ReliableOrdered);

            Debug.Log($"[VoteSystemRPC] Server sent vote start: target={targetSceneId}, participants={participantSteamIds.Count}");
        }

        public void Server_BroadcastReadySet(string pid, bool ready)
        {
            if (!UseRPCMode || !_rpcRegistered)
                return;

            var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
            if (rpcManager == null || !rpcManager.IsServer)
                return;

            rpcManager.CallRPC("VoteReadySet", Net.HybridP2P.RPCTarget.AllClients, 0, (writer) =>
            {
                writer.Put(pid);
                writer.Put(ready);
            }, DeliveryMethod.ReliableOrdered);

            Debug.Log($"[VoteSystemRPC] Server broadcast ready: pid={pid}, ready={ready}");
        }

        public void Server_BroadcastBeginLoad()
        {
            if (!UseRPCMode || !_rpcRegistered)
                return;

            var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
            if (rpcManager == null || !rpcManager.IsServer)
                return;

            rpcManager.CallRPC("VoteBeginLoad", Net.HybridP2P.RPCTarget.AllClients, 0, (writer) =>
            {
                writer.Put(_sceneNet.sceneTargetId ?? "");
                writer.Put(_sceneNet.sceneCurtainGuid ?? "");
                writer.Put(_sceneNet.sceneNotifyEvac);
                writer.Put(_sceneNet.sceneSaveToFile);
                writer.Put(_sceneNet.sceneUseLocation);
                writer.Put(_sceneNet.sceneLocationName ?? "");
            }, DeliveryMethod.ReliableOrdered);

            Debug.Log($"[VoteSystemRPC] Server broadcast begin load: target={_sceneNet.sceneTargetId}");
        }

        public void Server_BroadcastCancelVote()
        {
            if (!UseRPCMode || !_rpcRegistered)
                return;

            var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
            if (rpcManager == null || !rpcManager.IsServer)
                return;

            rpcManager.CallRPC("VoteCancel", Net.HybridP2P.RPCTarget.AllClients, 0, (writer) =>
            {
                // No additional data needed
            }, DeliveryMethod.ReliableOrdered);

            Debug.Log($"[VoteSystemRPC] Server broadcast cancel vote");
        }

        #endregion

        #region Client -> Server RPCs

        public void Client_RequestVote(string targetId, string curtainGuid, bool notifyEvac, bool saveToFile, bool useLocation, string locationName)
        {
            if (!UseRPCMode || !_rpcRegistered)
                return;

            var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
            if (rpcManager == null || rpcManager.IsServer)
                return;

            rpcManager.CallRPC("VoteRequest", Net.HybridP2P.RPCTarget.Server, 0, (writer) =>
            {
                writer.Put(targetId ?? "");
                writer.Put(curtainGuid ?? "");
                writer.Put(notifyEvac);
                writer.Put(saveToFile);
                writer.Put(useLocation);
                writer.Put(locationName ?? "");
            }, DeliveryMethod.ReliableOrdered);

            Debug.Log($"[VoteSystemRPC] Client sent vote request: target={targetId}");
        }

        public void Client_SendReady(bool ready)
        {
            if (!UseRPCMode || !_rpcRegistered)
                return;

            var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
            if (rpcManager == null || rpcManager.IsServer)
                return;

            rpcManager.CallRPC("VoteCast", Net.HybridP2P.RPCTarget.Server, 0, (writer) =>
            {
                writer.Put(ready);
            }, DeliveryMethod.ReliableOrdered);

            Debug.Log($"[VoteSystemRPC] Client cast vote: ready={ready}");
        }

        #endregion

        #region RPC Handlers

        private void OnRPC_VoteStart(long senderConnectionId, NetDataReader reader)
        {
            if (_sceneNet == null) return;

            string targetSceneId = reader.GetString();
            string curtainGuid = reader.GetString();
            bool notifyEvac = reader.GetBool();
            bool saveToFile = reader.GetBool();
            bool useLocation = reader.GetBool();
            string locationName = reader.GetString();
            string hostSceneId = reader.GetString();

            int participantCount = reader.GetInt();
            var participantSteamIds = new List<ulong>();
            for (int i = 0; i < participantCount; i++)
            {
                participantSteamIds.Add(reader.GetULong());
            }

            Debug.Log($"[VoteSystemRPC] Received vote start: target={targetSceneId}, participants={participantCount}");

            _sceneNet.sceneTargetId = targetSceneId;
            _sceneNet.sceneCurtainGuid = string.IsNullOrEmpty(curtainGuid) ? null : curtainGuid;
            _sceneNet.sceneNotifyEvac = notifyEvac;
            _sceneNet.sceneSaveToFile = saveToFile;
            _sceneNet.sceneUseLocation = useLocation;
            _sceneNet.sceneLocationName = locationName;

            _sceneNet.sceneParticipantIds.Clear();
            foreach (var steamId in participantSteamIds)
            {
                var pid = $"steam_{steamId}";
                _sceneNet.sceneParticipantIds.Add(pid);
            }

            var mySteamId = SteamUser.GetSteamID().m_SteamID;
            var myPid = $"steam_{mySteamId}";

            if (!string.IsNullOrEmpty(hostSceneId))
            {
                string mySceneId = null;
                LocalPlayerManager.Instance.ComputeIsInGame(out mySceneId);
                mySceneId = mySceneId ?? string.Empty;

                if (!string.Equals(hostSceneId, mySceneId, System.StringComparison.Ordinal))
                {
                    Debug.Log($"[VoteSystemRPC] Different scene: host={hostSceneId}, me={mySceneId}");
                    return;
                }
            }

            if (_sceneNet.sceneParticipantIds.Count > 0 && !_sceneNet.sceneParticipantIds.Contains(myPid))
            {
                Debug.LogWarning($"[VoteSystemRPC] Not in participants: me={myPid}");
                return;
            }

            _sceneNet.sceneVoteActive = true;
            _sceneNet.localReady = false;
            _sceneNet.sceneReady.Clear();
            foreach (var pid in _sceneNet.sceneParticipantIds)
            {
                _sceneNet.sceneReady[pid] = false;
            }

            Debug.Log($"[VoteSystemRPC] Vote started successfully: participants={_sceneNet.sceneParticipantIds.Count}");
        }

        private void OnRPC_VoteRequest(long senderConnectionId, NetDataReader reader)
        {
            if (_sceneNet == null || !NetService.Instance.IsServer) return;

            string targetId = reader.GetString();
            string curtainGuid = reader.GetString();
            bool notifyEvac = reader.GetBool();
            bool saveToFile = reader.GetBool();
            bool useLocation = reader.GetBool();
            string locationName = reader.GetString();

            Debug.Log($"[VoteSystemRPC] Received vote request from {senderConnectionId}: target={targetId}");

            _sceneNet.Host_BeginSceneVote_Simple(targetId, curtainGuid, notifyEvac, saveToFile, useLocation, locationName);
        }

        private void OnRPC_VoteCast(long senderConnectionId, NetDataReader reader)
        {
            if (_sceneNet == null || !NetService.Instance.IsServer) return;

            bool ready = reader.GetBool();

            Debug.Log($"[VoteSystemRPC] Received vote cast from {senderConnectionId}: ready={ready}");

            if (!SteamManager.Initialized || SteamEndPointMapper.Instance == null)
                return;

            string pid = null;
            foreach (var kv in NetService.Instance.playerStatuses)
            {
                var peer = kv.Key;
                if (peer == null) continue;

                var endPoint = peer.EndPoint as System.Net.IPEndPoint;
                if (endPoint != null && SteamEndPointMapper.Instance.TryGetSteamID(endPoint, out var steamId))
                {
                    if ((long)steamId.m_SteamID == senderConnectionId)
                    {
                        pid = $"steam_{steamId.m_SteamID}";
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(pid))
            {
                Debug.LogWarning($"[VoteSystemRPC] Could not find SteamID for connection {senderConnectionId}");
                return;
            }

            if (!_sceneNet.sceneVoteActive || !_sceneNet.sceneReady.ContainsKey(pid))
            {
                Debug.LogWarning($"[VoteSystemRPC] Invalid vote state for {pid}");
                return;
            }

            _sceneNet.sceneReady[pid] = ready;
            Server_BroadcastReadySet(pid, ready);

            foreach (var id in _sceneNet.sceneParticipantIds)
            {
                if (!_sceneNet.sceneReady.TryGetValue(id, out var r) || !r)
                    return;
            }

            Server_BroadcastBeginLoad();
        }

        private void OnRPC_VoteReadySet(long senderConnectionId, NetDataReader reader)
        {
            if (_sceneNet == null) return;

            string pid = reader.GetString();
            bool ready = reader.GetBool();

            Debug.Log($"[VoteSystemRPC] Received ready set: pid={pid}, ready={ready}");

            if (_sceneNet.sceneReady.ContainsKey(pid))
            {
                _sceneNet.sceneReady[pid] = ready;
            }
        }

        private void OnRPC_VoteBeginLoad(long senderConnectionId, NetDataReader reader)
        {
            if (_sceneNet == null) return;

            string targetSceneId = reader.GetString();
            string curtainGuid = reader.GetString();
            bool notifyEvac = reader.GetBool();
            bool saveToFile = reader.GetBool();
            bool useLocation = reader.GetBool();
            string locationName = reader.GetString();

            Debug.Log($"[VoteSystemRPC] Received begin load: target={targetSceneId}");

            _sceneNet.sceneVoteActive = false;
            _sceneNet.sceneTargetId = targetSceneId;
            _sceneNet.sceneCurtainGuid = string.IsNullOrEmpty(curtainGuid) ? null : curtainGuid;
            _sceneNet.sceneNotifyEvac = notifyEvac;
            _sceneNet.sceneSaveToFile = saveToFile;
            _sceneNet.sceneUseLocation = useLocation;
            _sceneNet.sceneLocationName = locationName;

            var loadMethod = typeof(SceneNet).GetMethod("TryPerformSceneLoad_Local", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (loadMethod != null)
            {
                _sceneNet.allowLocalSceneLoad = true;
                loadMethod.Invoke(_sceneNet, new object[] { targetSceneId, curtainGuid, notifyEvac, saveToFile, useLocation, locationName });
            }

            _sceneNet.sceneReady.Clear();
            _sceneNet.localReady = false;
        }

        private void OnRPC_VoteCancel(long senderConnectionId, NetDataReader reader)
        {
            if (_sceneNet == null) return;

            Debug.Log($"[VoteSystemRPC] Received cancel vote");

            _sceneNet.sceneVoteActive = false;
            _sceneNet.localReady = false;
            _sceneNet.sceneReady.Clear();
        }

        #endregion

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}

