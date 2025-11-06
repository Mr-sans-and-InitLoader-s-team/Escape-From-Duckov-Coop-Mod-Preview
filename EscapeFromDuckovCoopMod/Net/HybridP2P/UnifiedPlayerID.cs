using System.Collections.Generic;
using System.Net;
using UnityEngine;
using Steamworks;
using LiteNetLib;

namespace EscapeFromDuckovCoopMod.Net.HybridP2P
{
    public class UnifiedPlayerID
    {
        private readonly ulong _steamId;
        private readonly string _endPoint;
        private readonly bool _isSteam;
        
        private UnifiedPlayerID(ulong steamId)
        {
            _steamId = steamId;
            _isSteam = true;
            _endPoint = null;
        }
        
        private UnifiedPlayerID(string endPoint)
        {
            _steamId = 0;
            _isSteam = false;
            _endPoint = endPoint;
        }
        
        public static UnifiedPlayerID FromSteamID(ulong steamId)
        {
            return new UnifiedPlayerID(steamId);
        }
        
        public static UnifiedPlayerID FromEndPoint(string endPoint)
        {
            return new UnifiedPlayerID(endPoint);
        }
        
        public static UnifiedPlayerID FromPeer(NetPeer peer)
        {
            if (peer == null) return null;
            
            var endPoint = peer.EndPoint?.ToString();
            if (string.IsNullOrEmpty(endPoint)) return null;
            
            if (SteamManager.Initialized && SteamEndPointMapper.Instance != null)
            {
                if (SteamEndPointMapper.Instance.TryGetSteamID((IPEndPoint)peer.EndPoint, out var steamId))
                {
                    return FromSteamID(steamId.m_SteamID);
                }
            }
            
            return FromEndPoint(endPoint);
        }
        
        public static UnifiedPlayerID GetLocalPlayerID()
        {
            if (SteamManager.Initialized)
            {
                return FromSteamID(SteamUser.GetSteamID().m_SteamID);
            }
            
            if (NetService.Instance?.localPlayerStatus != null)
            {
                var ep = NetService.Instance.localPlayerStatus.EndPoint;
                if (!string.IsNullOrEmpty(ep))
                    return FromEndPoint(ep);
            }
            
            return null;
        }
        
        public string ToNetworkString()
        {
            return _isSteam ? $"s:{_steamId}" : $"e:{_endPoint}";
        }
        
        public static UnifiedPlayerID FromNetworkString(string str)
        {
            if (string.IsNullOrEmpty(str)) return null;
            
            if (str.StartsWith("s:"))
            {
                var idStr = str.Substring(2);
                if (ulong.TryParse(idStr, out var steamId))
                    return FromSteamID(steamId);
            }
            else if (str.StartsWith("e:"))
            {
                return FromEndPoint(str.Substring(2));
            }
            
            return null;
        }
        
        public bool IsSamePeer(NetPeer peer)
        {
            if (peer == null) return false;
            
            if (_isSteam && SteamManager.Initialized && SteamEndPointMapper.Instance != null)
            {
                if (SteamEndPointMapper.Instance.TryGetSteamID((IPEndPoint)peer.EndPoint, out var steamId))
                {
                    return steamId.m_SteamID == _steamId;
                }
            }
            
            if (!_isSteam)
            {
                return peer.EndPoint?.ToString() == _endPoint;
            }
            
            return false;
        }
        
        public override string ToString()
        {
            return ToNetworkString();
        }
        
        public override bool Equals(object obj)
        {
            if (obj is UnifiedPlayerID other)
            {
                if (_isSteam != other._isSteam) return false;
                return _isSteam ? _steamId == other._steamId : _endPoint == other._endPoint;
            }
            return false;
        }
        
        public override int GetHashCode()
        {
            return _isSteam ? _steamId.GetHashCode() : _endPoint?.GetHashCode() ?? 0;
        }
        
        public string GetDisplayName()
        {
            if (_isSteam && SteamManager.Initialized)
            {
                var name = SteamFriends.GetFriendPersonaName(new CSteamID(_steamId));
                if (!string.IsNullOrEmpty(name) && name != "[unknown]")
                    return name;
                return $"Player_{_steamId.ToString().Substring(System.Math.Max(0, _steamId.ToString().Length - 4))}";
            }
            
            return _endPoint ?? "Unknown";
        }
    }
    
    public class UnifiedVoteSystem : MonoBehaviour
    {
        public static UnifiedVoteSystem Instance { get; private set; }
        
        private SceneNet _sceneNet;
        private bool _initialized = false;
        
        public bool VoteActive { get; private set; }
        public string TargetSceneId { get; private set; }
        public Dictionary<UnifiedPlayerID, bool> ReadyStates { get; private set; } = new Dictionary<UnifiedPlayerID, bool>();
        
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
                Debug.LogError("[UnifiedVoteSystem] SceneNet.Instance is null");
                return;
            }
            
            RegisterRPCs();
        }
        
        private void RegisterRPCs()
        {
            var rpcManager = HybridRPCManager.Instance;
            if (rpcManager == null)
            {
                Debug.LogWarning("[UnifiedVoteSystem] HybridRPCManager not available, will retry");
                Invoke(nameof(RegisterRPCs), 1f);
                return;
            }
            
            rpcManager.RegisterRPC("UnifiedVoteStart", OnRPC_VoteStart);
            rpcManager.RegisterRPC("UnifiedVoteReady", OnRPC_VoteReady);
            rpcManager.RegisterRPC("UnifiedVoteBeginLoad", OnRPC_BeginLoad);
            rpcManager.RegisterRPC("UnifiedVoteCancel", OnRPC_Cancel);
            
            _initialized = true;
            Debug.Log("[UnifiedVoteSystem] Initialized with unified RPC system");
        }
        
        public void Server_StartVote(string targetSceneId, string curtainGuid, bool notifyEvac, bool saveToFile, bool useLocation, string locationName, List<UnifiedPlayerID> participants)
        {
            if (!_initialized)
            {
                Debug.LogError("[UnifiedVoteSystem] Not initialized");
                return;
            }
            
            var rpcManager = HybridRPCManager.Instance;
            if (rpcManager == null || !rpcManager.IsServer) return;
            
            string hostSceneId = "";
            LocalPlayerManager.Instance?.ComputeIsInGame(out hostSceneId);
            
            Debug.Log($"[UnifiedVoteSystem-Server] Starting vote: target={targetSceneId}, participants={participants.Count}");
            
            rpcManager.CallReliableRPC("UnifiedVoteStart", RPCTarget.AllClients, 0, (writer) =>
            {
                writer.Put(targetSceneId ?? "");
                writer.Put(curtainGuid ?? "");
                writer.Put(notifyEvac);
                writer.Put(saveToFile);
                writer.Put(useLocation);
                writer.Put(locationName ?? "");
                writer.Put(hostSceneId);
                
                writer.Put(participants.Count);
                foreach (var pid in participants)
                {
                    writer.Put(pid.ToNetworkString());
                }
            }, DeliveryMethod.ReliableOrdered);
            
            VoteActive = true;
            TargetSceneId = targetSceneId;
            ReadyStates.Clear();
            foreach (var pid in participants)
            {
                ReadyStates[pid] = false;
            }
        }
        
        public void Client_SetReady(bool ready)
        {
            if (!_initialized) return;
            
            var rpcManager = HybridRPCManager.Instance;
            if (rpcManager == null || !rpcManager.IsClient) return;
            
            var myId = UnifiedPlayerID.GetLocalPlayerID();
            if (myId == null)
            {
                Debug.LogWarning("[UnifiedVoteSystem-Client] Cannot determine local player ID");
                return;
            }
            
            Debug.Log($"[UnifiedVoteSystem-Client] Setting ready={ready}, myId={myId}");
            
            rpcManager.CallReliableRPC("UnifiedVoteReady", RPCTarget.Server, 0, (writer) =>
            {
                writer.Put(myId.ToNetworkString());
                writer.Put(ready);
            }, DeliveryMethod.ReliableOrdered);
        }
        
        public void Server_BroadcastReadyState(UnifiedPlayerID playerId, bool ready)
        {
            if (!_initialized) return;
            
            var rpcManager = HybridRPCManager.Instance;
            if (rpcManager == null || !rpcManager.IsServer) return;
            
            if (ReadyStates.ContainsKey(playerId))
            {
                ReadyStates[playerId] = ready;
            }
            
            Debug.Log($"[UnifiedVoteSystem-Server] Broadcasting ready: player={playerId}, ready={ready}");
            
            rpcManager.CallReliableRPC("UnifiedVoteReady", RPCTarget.AllClients, 0, (writer) =>
            {
                writer.Put(playerId.ToNetworkString());
                writer.Put(ready);
            }, DeliveryMethod.ReliableOrdered);
            
            bool allReady = true;
            foreach (var kvp in ReadyStates)
            {
                if (!kvp.Value)
                {
                    allReady = false;
                    break;
                }
            }
            
            if (allReady && ReadyStates.Count > 0)
            {
                Server_BeginLoad();
            }
        }
        
        private void Server_BeginLoad()
        {
            if (!_initialized) return;
            
            var rpcManager = HybridRPCManager.Instance;
            if (rpcManager == null || !rpcManager.IsServer) return;
            
            Debug.Log($"[UnifiedVoteSystem-Server] All ready, beginning load to {TargetSceneId}");
            
            rpcManager.CallReliableRPC("UnifiedVoteBeginLoad", RPCTarget.AllClients, 0, (writer) =>
            {
                writer.Put(TargetSceneId ?? "");
            }, DeliveryMethod.ReliableOrdered);
            
            VoteActive = false;
            
            if (_sceneNet != null)
            {
                _sceneNet.Server_BroadcastBeginSceneLoad();
            }
        }
        
        public void Server_CancelVote()
        {
            if (!_initialized) return;
            
            var rpcManager = HybridRPCManager.Instance;
            if (rpcManager == null || !rpcManager.IsServer) return;
            
            Debug.Log("[UnifiedVoteSystem-Server] Cancelling vote");
            
            rpcManager.CallReliableRPC("UnifiedVoteCancel", RPCTarget.AllClients, 0, (writer) => {}, DeliveryMethod.ReliableOrdered);
            
            VoteActive = false;
            ReadyStates.Clear();
        }
        
        private void OnRPC_VoteStart(long senderConnectionId, LiteNetLib.Utils.NetDataReader reader)
        {
            string targetSceneId = reader.GetString();
            string curtainGuid = reader.GetString();
            bool notifyEvac = reader.GetBool();
            bool saveToFile = reader.GetBool();
            bool useLocation = reader.GetBool();
            string locationName = reader.GetString();
            string hostSceneId = reader.GetString();
            
            int participantCount = reader.GetInt();
            var participants = new List<UnifiedPlayerID>();
            for (int i = 0; i < participantCount; i++)
            {
                var pidStr = reader.GetString();
                var pid = UnifiedPlayerID.FromNetworkString(pidStr);
                if (pid != null)
                    participants.Add(pid);
            }
            
            Debug.Log($"[UnifiedVoteSystem-Client] Received vote start: target={targetSceneId}, participants={participants.Count}");
            
            if (!string.IsNullOrEmpty(hostSceneId))
            {
                string mySceneId = "";
                LocalPlayerManager.Instance?.ComputeIsInGame(out mySceneId);
                
                if (!Spectator.AreSameMap(hostSceneId, mySceneId))
                {
                    Debug.Log($"[UnifiedVoteSystem-Client] Different scene, ignoring vote");
                    return;
                }
            }
            
            var myId = UnifiedPlayerID.GetLocalPlayerID();
            if (myId != null && participants.Count > 0)
            {
                bool found = false;
                foreach (var pid in participants)
                {
                    if (pid.Equals(myId))
                    {
                        found = true;
                        break;
                    }
                }
                
                if (!found)
                {
                    Debug.Log($"[UnifiedVoteSystem-Client] Not in participant list, ignoring");
                    return;
                }
            }
            
            VoteActive = true;
            TargetSceneId = targetSceneId;
            ReadyStates.Clear();
            foreach (var pid in participants)
            {
                ReadyStates[pid] = false;
            }
            
            if (_sceneNet != null)
            {
                _sceneNet.sceneTargetId = targetSceneId;
                _sceneNet.sceneCurtainGuid = curtainGuid;
                _sceneNet.sceneNotifyEvac = notifyEvac;
                _sceneNet.sceneSaveToFile = saveToFile;
                _sceneNet.sceneUseLocation = useLocation;
                _sceneNet.sceneLocationName = locationName;
                _sceneNet.sceneVoteActive = true;
                _sceneNet.localReady = false;
            }
            
            Debug.Log($"[UnifiedVoteSystem-Client] Vote activated successfully");
        }
        
        private void OnRPC_VoteReady(long senderConnectionId, LiteNetLib.Utils.NetDataReader reader)
        {
            var pidStr = reader.GetString();
            bool ready = reader.GetBool();
            
            var pid = UnifiedPlayerID.FromNetworkString(pidStr);
            if (pid == null) return;
            
            Debug.Log($"[UnifiedVoteSystem] Received ready update: player={pid}, ready={ready}");
            
            if (ReadyStates.ContainsKey(pid))
            {
                ReadyStates[pid] = ready;
            }
            
            if (_sceneNet != null)
            {
                _sceneNet.sceneReady[pidStr] = ready;
            }
        }
        
        private void OnRPC_BeginLoad(long senderConnectionId, LiteNetLib.Utils.NetDataReader reader)
        {
            string targetSceneId = reader.GetString();
            
            Debug.Log($"[UnifiedVoteSystem-Client] Begin load to {targetSceneId}");
            
            VoteActive = false;
            
            if (_sceneNet != null)
            {
                _sceneNet.sceneVoteActive = false;
                _sceneNet.localReady = false;
                _sceneNet.sceneReady.Clear();
            }
        }
        
        private void OnRPC_Cancel(long senderConnectionId, LiteNetLib.Utils.NetDataReader reader)
        {
            Debug.Log("[UnifiedVoteSystem-Client] Vote cancelled");
            
            VoteActive = false;
            ReadyStates.Clear();
            
            if (_sceneNet != null)
            {
                _sceneNet.sceneVoteActive = false;
                _sceneNet.localReady = false;
                _sceneNet.sceneReady.Clear();
            }
        }
    }
}

