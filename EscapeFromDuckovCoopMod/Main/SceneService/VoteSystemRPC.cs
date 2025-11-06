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

            rpcManager.RegisterRPC("VoteStart", OnRPC_VoteStart); // Legacy
            rpcManager.RegisterRPC("VoteStartP2P", OnRPC_VoteStartP2P);
            rpcManager.RegisterRPC("VoteStartLAN", OnRPC_VoteStartLAN);
            rpcManager.RegisterRPC("VoteRequest", OnRPC_VoteRequest);
            rpcManager.RegisterRPC("VoteCast", OnRPC_VoteCast);
            rpcManager.RegisterRPC("VoteReadySet", OnRPC_VoteReadySet);
            rpcManager.RegisterRPC("VoteBeginLoad", OnRPC_VoteBeginLoad);
            rpcManager.RegisterRPC("VoteCancel", OnRPC_VoteCancel);

            _rpcRegistered = true;
            UseRPCMode = true;
            Debug.Log("[VoteSystemRPC] All RPCs registered (P2P + LAN modes), UseRPCMode enabled");
        }

        #region Server -> Client RPCs

        public void Server_StartVote(string targetSceneId, string curtainGuid, bool notifyEvac, bool saveToFile, bool useLocation, string locationName, List<ulong> participantSteamIds)
        {
            Debug.LogWarning("[VoteSystemRPC] Server_StartVote(legacy) called - this should not be used, use Server_StartVoteP2P or Server_StartVoteLAN directly");
            Server_StartVoteP2P(targetSceneId, curtainGuid, notifyEvac, saveToFile, useLocation, locationName, participantSteamIds);
        }

        public void Server_StartVoteP2P(string targetSceneId, string curtainGuid, bool notifyEvac, bool saveToFile, bool useLocation, string locationName, List<ulong> participantSteamIds)
        {
            if (!UseRPCMode || !_rpcRegistered)
            {
                Debug.Log("[VoteSystemRPC] P2P RPC mode disabled, using legacy method");
                return;
            }

            var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
            if (rpcManager == null || !rpcManager.IsServer)
            {
                Debug.LogWarning("[VoteSystemRPC] Server_StartVoteP2P: rpcManager null or not server");
                return;
            }

            string hostSceneId = "";
            LocalPlayerManager.Instance?.ComputeIsInGame(out hostSceneId);
            hostSceneId = hostSceneId ?? "";

            Debug.Log($"[VoteSystemRPC-P2P] Server sending vote start: target={targetSceneId}, hostScene={hostSceneId}, participants={participantSteamIds.Count}");

            rpcManager.CallReliableRPC("VoteStartP2P", Net.HybridP2P.RPCTarget.AllClients, 0, (writer) =>
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

            Debug.Log($"[VoteSystemRPC-P2P] Server vote start RPC sent");
        }

        public void Server_StartVoteLAN(string targetSceneId, string curtainGuid, bool notifyEvac, bool saveToFile, bool useLocation, string locationName, List<string> participantEndPoints)
        {
            if (!UseRPCMode || !_rpcRegistered)
            {
                Debug.Log("[VoteSystemRPC] LAN RPC mode disabled, using legacy method");
                return;
            }

            var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
            if (rpcManager == null || !rpcManager.IsServer)
            {
                Debug.LogWarning("[VoteSystemRPC] Server_StartVoteLAN: rpcManager null or not server");
                return;
            }

            string hostSceneId = "";
            LocalPlayerManager.Instance?.ComputeIsInGame(out hostSceneId);
            hostSceneId = hostSceneId ?? "";

            Debug.Log($"[VoteSystemRPC-LAN] Server sending vote start: target={targetSceneId}, hostScene={hostSceneId}, participants={participantEndPoints.Count}");

            rpcManager.CallReliableRPC("VoteStartLAN", Net.HybridP2P.RPCTarget.AllClients, 0, (writer) =>
            {
                writer.Put(targetSceneId ?? "");
                writer.Put(curtainGuid ?? "");
                writer.Put(notifyEvac);
                writer.Put(saveToFile);
                writer.Put(useLocation);
                writer.Put(locationName ?? "");
                writer.Put(hostSceneId);

                writer.Put(participantEndPoints.Count);
                foreach (var ep in participantEndPoints)
                {
                    writer.Put(ep ?? "");
                }
            }, DeliveryMethod.ReliableOrdered);

            Debug.Log($"[VoteSystemRPC-LAN] Server vote start RPC sent");
        }

        public void Server_BroadcastReadySet(string pid, bool ready)
        {
            if (!UseRPCMode || !_rpcRegistered)
                return;

            var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
            if (rpcManager == null || !rpcManager.IsServer)
                return;

            rpcManager.CallReliableRPC("VoteReadySet", Net.HybridP2P.RPCTarget.AllClients, 0, (writer) =>
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

            rpcManager.CallReliableRPC("VoteBeginLoad", Net.HybridP2P.RPCTarget.AllClients, 0, (writer) =>
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

            rpcManager.CallReliableRPC("VoteCancel", Net.HybridP2P.RPCTarget.AllClients, 0, (writer) =>
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
            OnRPC_VoteStartP2P(senderConnectionId, reader);
        }

        private void OnRPC_VoteStartP2P(long senderConnectionId, NetDataReader reader)
        {
            Debug.Log($"[VoteSystemRPC-P2P] OnRPC_VoteStartP2P called from sender {senderConnectionId}");
            
            if (_sceneNet == null)
            {
                Debug.LogWarning("[VoteSystemRPC-P2P] _sceneNet is null");
                return;
            }

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

            Debug.Log($"[VoteSystemRPC-P2P] Received vote start: target={targetSceneId}, hostScene={hostSceneId}, participants={participantCount}");

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
                Debug.Log($"[VoteSystemRPC-P2P] Added participant: steam_{steamId}");
            }

            var mySteamId = SteamUser.GetSteamID().m_SteamID;
            var myPid = $"steam_{mySteamId}";
            Debug.Log($"[VoteSystemRPC-P2P] My SteamID: {mySteamId}, myPid: {myPid}");

            if (!string.IsNullOrEmpty(hostSceneId))
            {
                string mySceneId = "";
                LocalPlayerManager.Instance?.ComputeIsInGame(out mySceneId);
                mySceneId = mySceneId ?? "";

                bool isSameScene = Spectator.AreSameMap(hostSceneId, mySceneId);
                Debug.Log($"[VoteSystemRPC-P2P] Scene check: host={hostSceneId}, me={mySceneId}, isSame={isSameScene}");
                
                if (!isSameScene)
                {
                    Debug.Log($"[VoteSystemRPC-P2P] Different scene, ignoring vote");
                    return;
                }
            }

            if (_sceneNet.sceneParticipantIds.Count > 0 && !_sceneNet.sceneParticipantIds.Contains(myPid))
            {
                Debug.LogWarning($"[VoteSystemRPC-P2P] Not in participants: me={myPid}");
                return;
            }

            _sceneNet.sceneVoteActive = true;
            _sceneNet.localReady = false;
            _sceneNet.sceneReady.Clear();
            foreach (var pid in _sceneNet.sceneParticipantIds)
            {
                _sceneNet.sceneReady[pid] = false;
            }

            Debug.Log($"[VoteSystemRPC-P2P] Vote started successfully: participants={_sceneNet.sceneParticipantIds.Count}");
        }

        private void OnRPC_VoteStartLAN(long senderConnectionId, NetDataReader reader)
        {
            Debug.Log($"[VoteSystemRPC-LAN] OnRPC_VoteStartLAN called from sender {senderConnectionId}");
            
            if (_sceneNet == null)
            {
                Debug.LogWarning("[VoteSystemRPC-LAN] _sceneNet is null");
                return;
            }

            string targetSceneId = reader.GetString();
            string curtainGuid = reader.GetString();
            bool notifyEvac = reader.GetBool();
            bool saveToFile = reader.GetBool();
            bool useLocation = reader.GetBool();
            string locationName = reader.GetString();
            string hostSceneId = reader.GetString();

            int participantCount = reader.GetInt();
            var participantEndPoints = new List<string>();
            for (int i = 0; i < participantCount; i++)
            {
                participantEndPoints.Add(reader.GetString());
            }

            Debug.Log($"[VoteSystemRPC-LAN] Received vote start: target={targetSceneId}, hostScene={hostSceneId}, participants={participantCount}");

            _sceneNet.sceneTargetId = targetSceneId;
            _sceneNet.sceneCurtainGuid = string.IsNullOrEmpty(curtainGuid) ? null : curtainGuid;
            _sceneNet.sceneNotifyEvac = notifyEvac;
            _sceneNet.sceneSaveToFile = saveToFile;
            _sceneNet.sceneUseLocation = useLocation;
            _sceneNet.sceneLocationName = locationName;

            _sceneNet.sceneParticipantIds.Clear();
            foreach (var ep in participantEndPoints)
            {
                _sceneNet.sceneParticipantIds.Add(ep);
                Debug.Log($"[VoteSystemRPC-LAN] Added participant: {ep}");
            }

            var myPid = NetService.Instance?.localPlayerStatus?.EndPoint ?? "unknown";
            Debug.Log($"[VoteSystemRPC-LAN] My EndPoint: {myPid}");

            if (!string.IsNullOrEmpty(hostSceneId))
            {
                string mySceneId = "";
                LocalPlayerManager.Instance?.ComputeIsInGame(out mySceneId);
                mySceneId = mySceneId ?? "";

                bool isSameScene = Spectator.AreSameMap(hostSceneId, mySceneId);
                Debug.Log($"[VoteSystemRPC-LAN] Scene check: host={hostSceneId}, me={mySceneId}, isSame={isSameScene}");
                
                if (!isSameScene)
                {
                    Debug.Log($"[VoteSystemRPC-LAN] Different scene, ignoring vote");
                    return;
                }
            }

            if (_sceneNet.sceneParticipantIds.Count > 0 && !_sceneNet.sceneParticipantIds.Contains(myPid))
            {
                Debug.LogWarning($"[VoteSystemRPC-LAN] Not in participants: me={myPid}, list={string.Join(",", _sceneNet.sceneParticipantIds)}");
                return;
            }

            _sceneNet.sceneVoteActive = true;
            _sceneNet.localReady = false;
            _sceneNet.sceneReady.Clear();
            foreach (var pid in _sceneNet.sceneParticipantIds)
            {
                _sceneNet.sceneReady[pid] = false;
            }

            Debug.Log($"[VoteSystemRPC-LAN] Vote started successfully: participants={_sceneNet.sceneParticipantIds.Count}");
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

            NetPeer senderPeer = null;
            foreach (var kv in NetService.Instance.playerStatuses)
            {
                var peer = kv.Key;
                if (peer != null && peer.Id == senderConnectionId)
                {
                    senderPeer = peer;
                    break;
                }
            }

            if (senderPeer == null)
            {
                Debug.LogWarning($"[VoteSystemRPC] Could not find peer for connection {senderConnectionId}");
                return;
            }

            string pid = null;
            if (SteamManager.Initialized && VirtualEndpointManager.Instance != null)
            {
                var endPoint = senderPeer.EndPoint as System.Net.IPEndPoint;
                if (endPoint != null && VirtualEndpointManager.Instance.TryGetSteamID(endPoint, out var steamId))
                {
                    pid = $"steam_{steamId.m_SteamID}";
                }
            }

            if (string.IsNullOrEmpty(pid))
            {
                pid = senderPeer.EndPoint?.ToString() ?? $"peer_{senderConnectionId}";
            }

            Debug.Log($"[VoteSystemRPC] Resolved pid={pid} for connection {senderConnectionId}");

            if (!_sceneNet.sceneVoteActive || !_sceneNet.sceneReady.ContainsKey(pid))
            {
                Debug.LogWarning($"[VoteSystemRPC] Invalid vote state for {pid}, voteActive={_sceneNet.sceneVoteActive}, hasKey={_sceneNet.sceneReady.ContainsKey(pid)}");
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

