// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspicuous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

using System.Linq;
using Duckov.UI;
using Steamworks;

namespace EscapeFromDuckovCoopMod;

public class SceneNet : MonoBehaviour
{
    public static SceneNet Instance;
    public string _sceneReadySidSent;
    public bool sceneVoteActive;
    public string sceneTargetId; // 统一的目标 SceneID
    public string sceneCurtainGuid; // 过场 GUID，可为空
    public bool sceneNotifyEvac;
    public bool sceneSaveToFile = true;

    public bool allowLocalSceneLoad;

    public bool sceneUseLocation;
    public string sceneLocationName;

    public bool localReady;

    //Scene Gate 等待进入地图系统 Wait join Map
    public volatile bool _cliSceneGateReleased;
    public string _cliGateSid;
    public string _srvGateSid;
    public bool IsMapSelectionEntry;

    public readonly Dictionary<string, string> _cliLastSceneIdByPlayer = new();

    // 记录已经“举手”的客户端（用 EndPoint 字符串，与现有 PlayerStatus 保持一致）
    public readonly HashSet<string> _srvGateReadyPids = new();

    private int _currentVoteId = -1;
    
    [Obsolete("Use RESTful API instead")]
    public readonly List<string> sceneParticipantIds = new();
    
    [Obsolete("Use RESTful API instead")]
    public readonly Dictionary<string, bool> sceneReady = new();
    
    private float _cliGateDeadline;
    private float _cliGateSeverDeadline;

    private bool _srvSceneGateOpen;
    private NetService Service => NetService.Instance;

    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;
    private int port => Service?.port ?? 0;
    private Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    private Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;
    private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;

    public void Init()
    {
        Instance = this;
        
        var restfulVote = Net.Core.RESTfulVoteSystem.Instance;
        if (restfulVote != null)
        {
            restfulVote.OnVoteCompleted += OnRESTfulVoteCompleted;
            restfulVote.OnPlayerReady += OnRESTfulPlayerReady;
        }
    }
    
    private void OnRESTfulVoteCompleted(int sceneId)
    {
        if (_currentVoteId < 0) return;
        
        Debug.Log($"[SceneNet-RESTful] Vote completed for scene {sceneId}");
        Server_BroadcastBeginSceneLoad();
    }
    
    private void OnRESTfulPlayerReady(string playerId, bool isReady, int sceneId)
    {
        Debug.Log($"[SceneNet-RESTful] Player {playerId} ready: {isReady}");
        
        if (sceneReady.ContainsKey(playerId))
        {
            sceneReady[playerId] = isReady;
        }
        
        if (IsServer && networkStarted && netManager != null)
        {
            var w = new NetDataWriter();
            w.Put((byte)Op.SCENE_READY_SET);
            w.Put(playerId);
            w.Put(isReady);
            netManager.SendToAll(w, LiteNetLib.DeliveryMethod.ReliableOrdered);
            Debug.Log($"[SceneNet-RESTful] Broadcasted ready status: {playerId} = {isReady}");
        }
        
        MModUI.Instance?.UpdateVotePanel();
    }

    public void TrySendSceneReadyOnce()
    {
        if (!networkStarted) return;

        // 只有真正进入地图（拿到 SceneId）才上报
        if (!LocalPlayerManager.Instance.ComputeIsInGame(out var sid) || string.IsNullOrEmpty(sid)) return;
        if (_sceneReadySidSent == sid) return; // 去抖：本场景只发一次

        var lm = LevelManager.Instance;
        var pos = lm && lm.MainCharacter ? lm.MainCharacter.transform.position : Vector3.zero;
        var rot = lm && lm.MainCharacter ? lm.MainCharacter.modelRoot.transform.rotation : Quaternion.identity;
        var faceJson = CustomFace.LoadLocalCustomFaceJson() ?? string.Empty;

        writer.Reset();
        writer.Put((byte)Op.SCENE_READY); // 你的枚举里已有 23 = SCENE_READY
        writer.Put(localPlayerStatus?.EndPoint ?? (IsServer ? $"Host:{port}" : "Client:Unknown"));
        writer.Put(sid);
        writer.PutVector3(pos);
        writer.PutQuaternion(rot);
        writer.Put(faceJson);


        if (IsServer)
            // 主机广播（本机也等同已就绪，方便让新进来的客户端看到主机）
            netManager?.SendToAll(writer, DeliveryMethod.ReliableOrdered);
        else
            connectedPeer?.Send(writer, DeliveryMethod.ReliableOrdered);

        _sceneReadySidSent = sid;
    }

    public void Server_BroadcastBeginSceneLoad()
    {
        if (Spectator.Instance._spectatorActive && Spectator.Instance._spectatorEndOnVotePending)
        {
            Spectator.Instance._spectatorEndOnVotePending = false;
            Spectator.Instance.EndSpectatorAndShowClosure();
        }

        Debug.Log($"[SERVER-LOAD] Broadcasting begin load");
        
        var voteRPC = VoteSystemRPC.Instance;
        if (voteRPC != null && voteRPC.UseRPCMode)
        {
            Debug.Log($"[SERVER-LOAD] Using RPC mode");
            voteRPC.Server_BroadcastBeginLoad();
            Debug.Log($"[SERVER-LOAD] RPC broadcast completed");
        }
        else
        {
            var w = new NetDataWriter();
            w.Put((byte)Op.SCENE_BEGIN_LOAD);
            w.Put((byte)1); // ver=1
            w.Put(sceneTargetId ?? "");

            var hasCurtain = !string.IsNullOrEmpty(sceneCurtainGuid);
            var flags = PackFlag.PackFlags(hasCurtain, sceneUseLocation, sceneNotifyEvac, sceneSaveToFile);
            w.Put(flags);

            if (hasCurtain) w.Put(sceneCurtainGuid);
            w.Put(sceneLocationName ?? "");

            // ★ 群发给所有客户端（客户端会根据是否正在投票/是否在名单自行处理）
            netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Debug.Log($"[SERVER-LOAD] 开始加载通过传统方式广播");
        }

        // 主机本地执行加载
        allowLocalSceneLoad = true;
        var map = CoopTool.GetMapSelectionEntrylist(sceneTargetId);
        if (map != null && IsMapSelectionEntry)
        {
            IsMapSelectionEntry = false;
            allowLocalSceneLoad = false;
            SceneM.Call_NotifyEntryClicked_ByInvoke(MapSelectionView.Instance, map, null);
        }
        else
        {
            TryPerformSceneLoad_Local(sceneTargetId, sceneCurtainGuid, sceneNotifyEvac, sceneSaveToFile, sceneUseLocation, sceneLocationName);
        }

        // 收尾与清理
        sceneVoteActive = false;
        sceneParticipantIds.Clear();
        sceneReady.Clear();
        localReady = false;
    }

    // ===== 主机：有人（或主机自己）切换准备 =====
    public void Server_OnSceneReadySet(NetPeer fromPeer, bool ready)
    {
        if (!IsServer) return;

        if (!sceneVoteActive) return;
        
        var restfulVote = Net.Core.RESTfulVoteSystem.Instance;
        if (restfulVote != null && _currentVoteId >= 0)
        {
            string playerId = null;
            
            var lobbyManager = SteamLobbyManager.Instance;
            bool isP2PMode = lobbyManager != null && lobbyManager.IsInLobby && SteamManager.Initialized;
            
            if (fromPeer != null)
            {
                if (isP2PMode && fromPeer.EndPoint is System.Net.IPEndPoint ipEndPoint)
                {
                    if (VirtualEndpointManager.Instance.TryGetSteamID(ipEndPoint, out var steamId))
                    {
                        playerId = $"steam_{steamId.m_SteamID}";
                        Debug.Log($"[SceneNet-RESTful-Server] Client {fromPeer.EndPoint} using SteamID: {playerId}");
                    }
                }
                
                if (playerId == null)
                {
                    var playerStatus = playerStatuses.TryGetValue(fromPeer, out var ps) ? ps : null;
                    playerId = playerStatus?.EndPoint ?? fromPeer.EndPoint?.ToString();
                }
            }
            else
            {
                if (isP2PMode)
                {
                    var mySteamId = Steamworks.SteamUser.GetSteamID();
                    playerId = $"steam_{mySteamId.m_SteamID}";
                    Debug.Log($"[SceneNet-RESTful-Server] Host using SteamID: {playerId}");
                }
                else
                {
                    playerId = localPlayerStatus?.EndPoint ?? $"Host:{port}";
                }
            }
            
            if (!string.IsNullOrEmpty(playerId) && restfulVote._votes.TryGetValue(_currentVoteId, out var vote))
            {
                if (vote.Participants.ContainsKey(playerId))
                {
                    vote.Participants[playerId] = ready;
                    Debug.Log($"[SceneNet-RESTful-Server] Player {playerId} ready={ready}");
                    
                    if (fromPeer == null)
                    {
                        localReady = ready;
                    }
                    
                    restfulVote.TriggerPlayerReady(playerId, ready, vote.SceneId);
                    
                    bool allReady = vote.Participants.Values.All(r => r);
                    int readyCount = vote.Participants.Values.Count(r => r);
                    int totalCount = vote.Participants.Count;
                    
                    Debug.Log($"[SceneNet-RESTful-Server] Vote status: {readyCount}/{totalCount} ready. Players: {string.Join(", ", vote.Participants.Select(p => $"{p.Key}={p.Value}"))}");
                    
                    if (allReady)
                    {
                        Debug.Log($"[SceneNet-RESTful-Server] All players ready, triggering completion");
                        restfulVote.TriggerVoteCompleted(vote.SceneId);
                    }
                }
                else
                {
                    Debug.LogWarning($"[SceneNet-RESTful-Server] Player {playerId} not in participants list. Available: {string.Join(", ", vote.Participants.Keys)}");
                }
            }
            
            return;
        }
        
        var unifiedVote = Net.HybridP2P.UnifiedVoteSystem.Instance;
        if (unifiedVote != null)
        {
            Net.HybridP2P.UnifiedPlayerID playerId = null;
            
            if (fromPeer != null)
            {
                playerId = Net.HybridP2P.UnifiedPlayerID.FromPeer(fromPeer);
                Debug.Log($"[SceneNet-Ready] Client ready: {playerId}, ready={ready}");
            }
            else
            {
                playerId = Net.HybridP2P.UnifiedPlayerID.GetLocalPlayerID();
                Debug.Log($"[SceneNet-Ready] Host ready: {playerId}, ready={ready}");
            }
            
            if (playerId != null)
            {
                localReady = ready;
                unifiedVote.Server_BroadcastReadyState(playerId, ready);
            }
            else
            {
                Debug.LogWarning($"[SceneNet-Ready] Cannot determine player ID");
            }
            
            return;
        }
        
        Debug.Log($"[SceneNet-Ready] Using legacy ready system");
        
        string pid = null;
        
        if (fromPeer != null)
        {
            var endPoint = fromPeer.EndPoint as System.Net.IPEndPoint;
            if (endPoint != null && VirtualEndpointManager.Instance != null)
            {
                if (VirtualEndpointManager.Instance.TryGetSteamID(endPoint, out var steamId))
                {
                    pid = $"steam_{steamId.m_SteamID}";
                    Debug.Log($"[SERVER-READY] 收到客户端准备(Steam): SteamID={steamId.m_SteamID}, ready={ready}");
                }
            }
            
            if (string.IsNullOrEmpty(pid))
            {
                pid = fromPeer.EndPoint?.ToString();
                Debug.Log($"[SERVER-READY] 收到客户端准备(直连): EndPoint={pid}, ready={ready}");
            }
        }
        else
        {
            bool hasNonLocalConnection = false;
            if (playerStatuses != null && playerStatuses.Count > 0)
            {
                var firstPeer = playerStatuses.Keys.FirstOrDefault();
                if (firstPeer != null)
                {
                    var ep = firstPeer.EndPoint as System.Net.IPEndPoint;
                    if (ep != null)
                    {
                        hasNonLocalConnection = !ep.Address.ToString().StartsWith("127.0.0.1");
                    }
                }
            }
            
            if (SteamManager.Initialized && hasNonLocalConnection)
            {
                var hostSteamId = SteamUser.GetSteamID().m_SteamID;
                pid = $"steam_{hostSteamId}";
                Debug.Log($"[SERVER-READY] 主机准备(Steam模式): SteamID={hostSteamId}, ready={ready}");
            }
            else if (localPlayerStatus != null)
            {
                pid = localPlayerStatus.EndPoint;
                Debug.Log($"[SERVER-READY] 主机准备(LAN模式): EndPoint={pid}, ready={ready}");
            }
            else
            {
                pid = $"Host:{NetService.Instance.port}";
                Debug.Log($"[SERVER-READY] 主机准备(LAN模式-默认): EndPoint={pid}, ready={ready}");
            }
        }
        
        if (string.IsNullOrEmpty(pid))
        {
            Debug.LogWarning($"[SERVER-READY] 无法解析玩家ID: fromPeer={fromPeer?.EndPoint}");
            return;
        }
        
        if (!sceneReady.ContainsKey(pid))
        {
            Debug.LogWarning($"[SERVER-READY] 玩家不在参与列表: pid={pid}, participants={string.Join(", ", sceneParticipantIds)}");
            
            if (sceneParticipantIds.Count == 0)
            {
                Debug.Log($"[SERVER-READY] 参与列表为空，允许玩家加入");
                sceneParticipantIds.Add(pid);
                sceneReady[pid] = false;
            }
            else
            {
                return;
            }
        }

        sceneReady[pid] = ready;

        // 群发给所有客户端
        Debug.Log($"[SERVER-READY] Broadcasting ready status: pid={pid}, ready={ready}");
        
        var voteRPC = VoteSystemRPC.Instance;
        if (voteRPC != null && voteRPC.UseRPCMode)
        {
            Debug.Log($"[SERVER-READY] Using RPC mode");
            voteRPC.Server_BroadcastReadySet(pid, ready);
            Debug.Log($"[SERVER-READY] RPC broadcast completed");
        }
        else
        {
            var w = new NetDataWriter();
            w.Put((byte)Op.SCENE_READY_SET);
            w.Put(pid);
            w.Put(ready);
            netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Debug.Log($"[SERVER-READY] 通过传统方式广播准备状态: pid={pid}, ready={ready}");
        }

        // 检查是否全员准备
        foreach (var id in sceneParticipantIds)
            if (!sceneReady.TryGetValue(id, out var r) || !r)
                return;

        // 全员就绪 → 开始加载
        Server_BroadcastBeginSceneLoad();
    }

    // ===== 客户端：收到“投票开始”（带参与者 pid 列表）=====
    public void Client_OnSceneVoteStart(NetDataReader r)
    {
        Debug.Log($"[CLIENT-VOTE-PARSE] 开始解析投票消息, availableBytes={r.AvailableBytes}");
        
        // ——读包：严格按顺序——
        if (!EnsureAvailable(r, 2))
        {
            Debug.LogWarning("[CLIENT-VOTE-PARSE] vote: header too short");
            return;
        }

        var ver = r.GetByte(); // switch 里已经吃掉了 op，这里是 ver
        Debug.Log($"[CLIENT-VOTE-PARSE] 投票版本: {ver}");
        if (ver < 1 || ver > 3)
        {
            Debug.LogWarning($"[CLIENT-VOTE-PARSE] vote: unsupported ver={ver}");
            return;
        }

        if (!TryGetString(r, out sceneTargetId))
        {
            Debug.LogWarning("[SCENE] vote: bad sceneId");
            return;
        }

        if (!EnsureAvailable(r, 1))
        {
            Debug.LogWarning("[SCENE] vote: no flags");
            return;
        }

        var flags = r.GetByte();
        bool hasCurtain, useLoc, notifyEvac, saveToFile;
        PackFlag.UnpackFlags(flags, out hasCurtain, out useLoc, out notifyEvac, out saveToFile);

        string curtainGuid = null;
        if (hasCurtain)
            if (!TryGetString(r, out curtainGuid))
            {
                Debug.LogWarning("[SCENE] vote: bad curtain");
                return;
            }

        string locName = null;
        if (!TryGetString(r, out locName))
        {
            Debug.LogWarning("[SCENE] vote: bad location");
            return;
        }


        var hostSceneId = string.Empty;
        if (ver >= 2)
        {
            if (!TryGetString(r, out hostSceneId))
            {
                Debug.LogWarning("[SCENE] vote: bad hostSceneId");
                return;
            }

            hostSceneId = hostSceneId ?? string.Empty;
        }

        if (!EnsureAvailable(r, 4))
        {
            Debug.LogWarning("[SCENE] vote: no count");
            return;
        }

        var cnt = r.GetInt();
        if (cnt < 0 || cnt > 256)
        {
            Debug.LogWarning("[SCENE] vote: weird count");
            return;
        }

        sceneParticipantIds.Clear();
        
        if (ver >= 3)
        {
            // 版本3：接收SteamID列表
            for (var i = 0; i < cnt; i++)
            {
                if (!EnsureAvailable(r, 8))
                {
                    Debug.LogWarning($"[SCENE] vote: bad steamId[{i}]");
                    return;
                }
                
                var steamId = r.GetULong();
                var pid = $"steam_{steamId}";
                sceneParticipantIds.Add(pid);
                Debug.Log($"[CLIENT-VOTE-PARSE] 接收参与者SteamID: {steamId}");
            }
        }
        else
        {
            // 版本1/2：接收EndPoint字符串列表（旧版本）
            for (var i = 0; i < cnt; i++)
            {
                if (!TryGetString(r, out var pid))
                {
                    Debug.LogWarning($"[SCENE] vote: bad pid[{i}]");
                    return;
                }

                sceneParticipantIds.Add(pid ?? "");
            }
        }

        // ===== 过滤：不同图 & 不在白名单，直接忽略 =====
        string mySceneId = null;
        LocalPlayerManager.Instance.ComputeIsInGame(out mySceneId);
        mySceneId = mySceneId ?? string.Empty;

        // A) 同图过滤（仅 v2 有 hostSceneId；v1 无法判断同图，用 B 兜底）
        if (!string.IsNullOrEmpty(hostSceneId) && !string.IsNullOrEmpty(mySceneId))
        {
            bool isSameScene = Spectator.AreSameMap(hostSceneId, mySceneId);
            if (!isSameScene)
            {
                Debug.Log($"[SCENE] vote: ignore (diff scene) host='{hostSceneId}' me='{mySceneId}'");
                return;
            }
        }

        // B) 白名单过滤：不在参与名单，就不显示
        if (sceneParticipantIds.Count > 0)
        {
            bool found = false;
            string myIdentifier = null;
            
            // 版本3：使用SteamID匹配
            if (ver >= 3)
            {
                if (SteamManager.Initialized)
                {
                    var mySteamId = SteamUser.GetSteamID().m_SteamID;
                    myIdentifier = $"steam_{mySteamId}";
                    found = sceneParticipantIds.Contains(myIdentifier);
                    Debug.Log($"[CLIENT-VOTE-PARSE] v3-SteamID匹配: mySteamId={mySteamId}, found={found}");
                }
            }
            // 版本1/2：使用EndPoint匹配（旧逻辑）
            else
            {
                if (!IsServer)
                {
                    // 1. 先尝试connectedPeer的EndPoint
                    if (connectedPeer != null)
                    {
                        myIdentifier = connectedPeer.EndPoint?.ToString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(myIdentifier) && sceneParticipantIds.Contains(myIdentifier))
                        {
                            found = true;
                            Debug.Log($"[CLIENT-VOTE-PARSE] 通过connectedPeer.EndPoint匹配: {myIdentifier}");
                        }
                    }
                    
                    // 2. 如果没找到，尝试通过SteamID查找对应的所有IP
                    if (!found && SteamManager.Initialized && VirtualEndpointManager.Instance != null)
                    {
                        var mySteamId = SteamUser.GetSteamID();
                        
                        foreach (var pid in sceneParticipantIds)
                        {
                            if (pid.StartsWith("Host:"))
                            {
                                continue;
                            }
                            
                            var parts = pid.Split(':');
                            if (parts.Length == 2 && System.Net.IPAddress.TryParse(parts[0], out var ipAddr) && int.TryParse(parts[1], out var port))
                            {
                                var ipEndPoint = new System.Net.IPEndPoint(ipAddr, port);
                                
                                if (VirtualEndpointManager.Instance.TryGetSteamID(ipEndPoint, out var steamId))
                                {
                                    if (steamId == mySteamId)
                                    {
                                        myIdentifier = pid;
                                        found = true;
                                        Debug.Log($"[CLIENT-VOTE-PARSE] 通过SteamID映射匹配: {pid} → {steamId.m_SteamID}");
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                // 服务器：使用localPlayerStatus.EndPoint（Host:xxxx格式）
                else if (IsServer && localPlayerStatus != null)
                {
                    myIdentifier = localPlayerStatus.EndPoint ?? string.Empty;
                    if (!string.IsNullOrEmpty(myIdentifier))
                    {
                        found = sceneParticipantIds.Contains(myIdentifier);
                    }
                }
            }
            
            Debug.Log($"[CLIENT-VOTE-PARSE] 检查白名单: me='{myIdentifier}', found={found}, participants={sceneParticipantIds.Count}, ver={ver}, IsServer={IsServer}");
            foreach (var p in sceneParticipantIds)
            {
                Debug.Log($"[CLIENT-VOTE-PARSE] 参与者: {p}");
            }
            
            if (!found)
            {
                if (!IsServer)
                {
                    Debug.Log($"[CLIENT-VOTE-PARSE] 客户端白名单未匹配，直连模式允许参与");
                }
                else
                {
                    Debug.Log($"[CLIENT-VOTE-PARSE] 主机白名单未匹配，直连模式允许主机参与投票");
                }
            }
        }

        // ——赋值到状态 & 初始化就绪表——
        sceneCurtainGuid = curtainGuid;
        sceneUseLocation = useLoc;
        sceneNotifyEvac = notifyEvac;
        sceneSaveToFile = saveToFile;
        sceneLocationName = locName ?? "";

        sceneVoteActive = true;
        localReady = false;
        sceneReady.Clear();
        foreach (var pid in sceneParticipantIds) sceneReady[pid] = false;
        
        _currentVoteId = 1;
        Debug.Log($"[CLIENT-VOTE-PARSE] Set _currentVoteId = {_currentVoteId} for RESTful voting");

        Debug.Log($"[CLIENT-VOTE-PARSE] ✅ 投票接收成功 v{ver}: target='{sceneTargetId}', hostScene='{hostSceneId}', myScene='{mySceneId}', players={cnt}, voteActive={sceneVoteActive}");

        // TODO：在这里弹出你的投票 UI（如果之前就是这里弹的，维持不变）
        // ShowSceneVoteUI(sceneTargetId, sceneLocationName, sceneParticipantIds) 等
    }


    // ===== 客户端：收到"某人准备状态变更"（pid + ready）=====
    private void Client_OnSomeoneReadyChanged(NetDataReader r)
    {
        var pid = r.GetString();
        var rd = r.GetBool();
        if (sceneReady.ContainsKey(pid))
        {
            sceneReady[pid] = rd;
            Debug.Log($"[CLIENT-READY] Received ready status: {pid} = {rd}");
            MModUI.Instance?.UpdateVotePanel();
        }
    }

    public void Client_OnBeginSceneLoad(NetDataReader r)
    {
        if (!EnsureAvailable(r, 2))
        {
            Debug.LogWarning("[SCENE] begin: header too short");
            return;
        }

        var ver = r.GetByte();
        if (ver != 1)
        {
            Debug.LogWarning($"[SCENE] begin: unsupported ver={ver}");
            return;
        }

        if (!TryGetString(r, out var id))
        {
            Debug.LogWarning("[SCENE] begin: bad sceneId");
            return;
        }

        if (!EnsureAvailable(r, 1))
        {
            Debug.LogWarning("[SCENE] begin: no flags");
            return;
        }

        var flags = r.GetByte();
        bool hasCurtain, useLoc, notifyEvac, saveToFile;
        PackFlag.UnpackFlags(flags, out hasCurtain, out useLoc, out notifyEvac, out saveToFile);

        string curtainGuid = null;
        if (hasCurtain)
            if (!TryGetString(r, out curtainGuid))
            {
                Debug.LogWarning("[SCENE] begin: bad curtain");
                return;
            }

        if (!TryGetString(r, out var locName))
        {
            Debug.LogWarning("[SCENE] begin: bad locName");
            return;
        }

        allowLocalSceneLoad = true;
        var map = CoopTool.GetMapSelectionEntrylist(sceneTargetId);
        if (map != null && sceneLocationName == "OnPointerClick")
        {
            IsMapSelectionEntry = false;
            allowLocalSceneLoad = false;
            SceneM.Call_NotifyEntryClicked_ByInvoke(MapSelectionView.Instance, map, null);
        }
        else
        {
            TryPerformSceneLoad_Local(sceneTargetId, sceneCurtainGuid, sceneNotifyEvac, sceneSaveToFile, sceneUseLocation, sceneLocationName);
        }

        sceneVoteActive = false;
        sceneParticipantIds.Clear();
        sceneReady.Clear();
        localReady = false;
    }

    public void Client_SendReadySet(bool ready)
    {
        if (IsServer || connectedPeer == null) return;

        var restfulVote = Net.Core.RESTfulVoteSystem.Instance;
        if (restfulVote != null && _currentVoteId >= 0)
        {
            string myPid = null;
            
            var lobbyManager = SteamLobbyManager.Instance;
            if (lobbyManager != null && lobbyManager.IsInLobby && SteamManager.Initialized)
            {
                var mySteamId = Steamworks.SteamUser.GetSteamID();
                myPid = $"steam_{mySteamId.m_SteamID}";
                Debug.Log($"[SceneNet-RESTful] Using Steam ID for ready: {myPid}");
            }
            else
            {
                myPid = localPlayerStatus?.EndPoint ?? connectedPeer.EndPoint?.ToString();
                Debug.Log($"[SceneNet-RESTful] Using EndPoint for ready: {myPid}");
            }
            
            if (string.IsNullOrEmpty(myPid))
            {
                Debug.LogWarning("[SceneNet-RESTful] Cannot send ready: player ID is null");
                return;
            }
            
            restfulVote.Client_UpdateParticipantStatus(
                voteId: _currentVoteId,
                playerId: myPid,
                isReady: ready,
                onSuccess: (participant) =>
                {
                    localReady = ready;
                    Debug.Log($"[SceneNet-RESTful] Ready status updated: {participant.PlayerId} = {participant.IsReady}");
                },
                onError: (error) =>
                {
                    Debug.LogError($"[SceneNet-RESTful] Failed to update ready status: {error}");
                }
            );
            
            return;
        }

        var voteRPC = VoteSystemRPC.Instance;
        if (voteRPC != null && voteRPC.UseRPCMode)
        {
            voteRPC.Client_SendReady(ready);
            return;
        }

        var w = new NetDataWriter();
        w.Put((byte)Op.SCENE_READY_SET);
        w.Put(ready);
        connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// 取消当前投票（房主或投票发起者可调用）
    /// </summary>
    public void CancelVote()
    {
        if (!sceneVoteActive)
        {
            Debug.LogWarning("[SCENE] 没有正在进行的投票");
            return;
        }

        Debug.Log("[SCENE] 取消投票，重置场景触发器");

        var restfulVote = Net.Core.RESTfulVoteSystem.Instance;
        if (restfulVote != null && _currentVoteId >= 0)
        {
            restfulVote.Client_DeleteVote(
                voteId: _currentVoteId,
                onSuccess: () =>
                {
                    Debug.Log($"[SceneNet-RESTful] Vote {_currentVoteId} deleted");
                },
                onError: (error) =>
                {
                    Debug.LogError($"[SceneNet-RESTful] Failed to delete vote: {error}");
                }
            );
        }

        if (IsServer && networkStarted && netManager != null)
        {
            var w = new NetDataWriter();
            w.Put((byte)Op.SCENE_CANCEL);
            netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Debug.Log("[SCENE] 服务器已广播取消投票消息");
        }

        sceneVoteActive = false;
        _currentVoteId = -1;
        localReady = false;

        EscapeFromDuckovCoopMod.Utils.SceneTriggerResetter.ResetAllSceneTriggers();
    }

    public void Client_OnVoteCancelled()
    {
        if (IsServer)
        {
            Debug.LogWarning("[SCENE] 服务器不应该接收客户端的取消投票消息");
            return;
        }

        Debug.Log("[SCENE] 收到服务器取消投票通知，重置本地状态");

        sceneVoteActive = false;
        _currentVoteId = -1;
        localReady = false;

        EscapeFromDuckovCoopMod.Utils.SceneTriggerResetter.ResetAllSceneTriggers();
    }

    private void TryPerformSceneLoad_Local(string targetSceneId, string curtainGuid,
        bool notifyEvac, bool save,
        bool useLocation, string locationName)
    {
        try
        {
            var loader = SceneLoader.Instance;
            var launched = false; // 是否已触发加载

            // （如果后面你把 loader.LoadScene 恢复了，这里可以先试 loader 路径并把 launched=true）

            // 无论 loader 是否存在，都尝试 SceneLoaderProxy 兜底
            foreach (var ii in FindObjectsOfType<SceneLoaderProxy>())
                try
                {
                    if (Traverse.Create(ii).Field<string>("sceneID").Value == targetSceneId)
                    {
                        ii.LoadScene();
                        launched = true;
                        Debug.Log($"[SCENE] Fallback via SceneLoaderProxy -> {targetSceneId}");
                        break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[SCENE] proxy check failed: " + e);
                }

            if (!launched) Debug.LogWarning($"[SCENE] Local load fallback failed: no proxy for '{targetSceneId}'");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[SCENE] Local load failed: " + e);
        }
        finally
        {
            allowLocalSceneLoad = false;
            if (networkStarted)
            {
                if (IsServer) SendLocalPlayerStatus.Instance.SendPlayerStatusUpdate();
                else Send_ClientStatus.Instance.SendClientStatusUpdate();
            }
        }
    }

    private Vector3 _srvUnifiedSpawnPos = Vector3.zero;
    private Quaternion _srvUnifiedSpawnRot = Quaternion.identity;
    private string _srvUnifiedSpawnScene = "";
    
    public void Server_HandleSceneReady(NetPeer fromPeer, string playerId, string sceneId, Vector3 pos, Quaternion rot, string faceJson)
    {
        Debug.Log($"[SERVER-SCENE-READY] Received from playerId={playerId}, sceneId={sceneId}, pos={pos}");
        
        if (fromPeer != null) SceneM._srvPeerScene[fromPeer] = sceneId;

        bool isValidPosition = pos.sqrMagnitude > 0.01f && !float.IsNaN(pos.x) && !float.IsNaN(pos.y) && !float.IsNaN(pos.z);
        
        if (_srvUnifiedSpawnScene != sceneId || _srvUnifiedSpawnPos == Vector3.zero || !isValidPosition)
        {
            if (isValidPosition)
            {
                _srvUnifiedSpawnScene = sceneId;
                _srvUnifiedSpawnPos = pos;
                _srvUnifiedSpawnRot = rot;
                Debug.Log($"[SERVER-SCENE-READY] Set unified spawn for scene {sceneId}: pos={pos}, rot={rot.eulerAngles}");
            }
            else
            {
                Debug.LogWarning($"[SERVER-SCENE-READY] Invalid position received: {pos}, using player's position instead");
            }
        }

        var actualPos = isValidPosition && (_srvUnifiedSpawnScene == sceneId) ? _srvUnifiedSpawnPos : pos;
        var actualRot = isValidPosition && (_srvUnifiedSpawnScene == sceneId) ? _srvUnifiedSpawnRot : rot;

        // 1) 回给 fromPeer：同图的所有已知玩家（使用统一位置）
        foreach (var kv in SceneM._srvPeerScene)
        {
            var other = kv.Key;
            if (other == fromPeer) continue;
            if (kv.Value == sceneId)
            {
                var oface = "";
                if (playerStatuses.TryGetValue(other, out var s) && s != null)
                {
                    oface = s.CustomFaceJson ?? "";
                }

                var w = new NetDataWriter();
                w.Put((byte)Op.REMOTE_CREATE);
                w.Put(playerStatuses[other].EndPoint); // other 的 id
                w.Put(sceneId);
                w.PutVector3(actualPos);
                w.PutQuaternion(actualRot);
                w.Put(oface);
                fromPeer?.Send(w, DeliveryMethod.ReliableOrdered);
                
                Debug.Log($"[SceneNet] Sent existing player {playerStatuses[other].EndPoint} to new player {playerId} at unified pos");
            }
        }

        // 2) 广播给同图的其他人：创建 fromPeer（使用统一位置）
        foreach (var kv in SceneM._srvPeerScene)
        {
            var other = kv.Key;
            if (other == fromPeer) continue;
            if (kv.Value == sceneId)
            {
                var w = new NetDataWriter();
                w.Put((byte)Op.REMOTE_CREATE);
                w.Put(playerId);
                w.Put(sceneId);
                w.PutVector3(actualPos);
                w.PutQuaternion(actualRot);
                var useFace = !string.IsNullOrEmpty(faceJson) ? faceJson :
                    playerStatuses.TryGetValue(fromPeer, out var ss) && !string.IsNullOrEmpty(ss.CustomFaceJson) ? ss.CustomFaceJson : "";
                w.Put(useFace);
                other.Send(w, DeliveryMethod.ReliableOrdered);
                
                Debug.Log($"[SceneNet] Broadcast new player {playerId} to {playerStatuses[other].EndPoint} at unified pos");
            }
        }

        // 3) 对不同图的人，互相 DESPAWN
        foreach (var kv in SceneM._srvPeerScene)
        {
            var other = kv.Key;
            if (other == fromPeer) continue;
            if (kv.Value != sceneId)
            {
                var w1 = new NetDataWriter();
                w1.Put((byte)Op.REMOTE_DESPAWN);
                w1.Put(playerId);
                other.Send(w1, DeliveryMethod.ReliableOrdered);

                var w2 = new NetDataWriter();
                w2.Put((byte)Op.REMOTE_DESPAWN);
                w2.Put(playerStatuses[other].EndPoint);
                fromPeer?.Send(w2, DeliveryMethod.ReliableOrdered);
            }
        }

        // 4) （可选）主机本地也显示客户端：在主机场景创建“该客户端”的远端克隆
        if (!remoteCharacters.TryGetValue(fromPeer, out var exists) || exists == null)
            CreateRemoteCharacter.CreateRemoteCharacterAsync(fromPeer, pos, rot, faceJson).Forget();
    }

    public void Host_BeginSceneVote_Simple(string targetSceneId, string curtainGuid,
        bool notifyEvac, bool saveToFile,
        bool useLocation, string locationName)
    {
        sceneTargetId = targetSceneId ?? "";
        sceneCurtainGuid = string.IsNullOrEmpty(curtainGuid) ? null : curtainGuid;
        sceneNotifyEvac = notifyEvac;
        sceneSaveToFile = saveToFile;
        sceneUseLocation = useLocation;
        sceneLocationName = locationName ?? "";

        var restfulVote = Net.Core.RESTfulVoteSystem.Instance;
        var hybridTransport = Net.Core.HybridTransport.Instance;
        var restTransport = hybridTransport?.RESTfulTransport;
        
        Debug.Log($"[SceneNet-Vote] RESTfulVoteSystem: {(restfulVote != null ? "OK" : "NULL")}, HybridTransport: {(hybridTransport != null ? "OK" : "NULL")}, RESTTransport: {(restTransport != null ? "OK" : "NULL")}, IsInitialized: {restTransport?.IsInitialized}");
        
        if (restfulVote != null && restTransport != null && restTransport.IsInitialized)
        {
            Debug.Log("[SceneNet-Vote] Using RESTful API for voting");
            
            var participants = new List<string>();
            
            var lobbyManager = SteamLobbyManager.Instance;
            if (lobbyManager != null && lobbyManager.IsInLobby && SteamManager.Initialized)
            {
                var currentLobby = lobbyManager.CurrentLobbyId;
                int memberCount = Steamworks.SteamMatchmaking.GetNumLobbyMembers(currentLobby);
                
                for (int i = 0; i < memberCount; i++)
                {
                    var memberId = Steamworks.SteamMatchmaking.GetLobbyMemberByIndex(currentLobby, i);
                    var pid = $"steam_{memberId.m_SteamID}";
                    participants.Add(pid);
                    Debug.Log($"[SceneNet-RESTful] Adding participant: {pid}");
                }
            }
            else
            {
                var hostPid = localPlayerStatus?.EndPoint ?? $"Host:{port}";
                participants.Add(hostPid);
                
                if (playerStatuses != null)
                {
                    foreach (var kv in playerStatuses)
                    {
                        var pid = kv.Value?.EndPoint;
                        if (!string.IsNullOrEmpty(pid) && !participants.Contains(pid))
                        {
                            participants.Add(pid);
                        }
                    }
                }
            }
            
            Debug.Log($"[SceneNet-RESTful] Creating vote with {participants.Count} participants");
            
            int sceneIdInt = 0;
            int.TryParse(targetSceneId, out sceneIdInt);
            
            restfulVote.Client_CreateVote(
                sceneId: sceneIdInt,
                participants: participants,
                onSuccess: (voteResource) =>
                {
                    _currentVoteId = voteResource.VoteId;
                    sceneVoteActive = true;
                    localReady = false;
                    
                    sceneParticipantIds.Clear();
                    sceneReady.Clear();
                    foreach (var p in participants)
                    {
                        sceneParticipantIds.Add(p);
                        sceneReady[p] = false;
                    }
                    
                    Debug.Log($"[SceneNet-RESTful] Vote created: ID={_currentVoteId}, Status={voteResource.Status}, Participants={participants.Count}");
                    
                    if (IsServer)
                    {
                        string hostSceneId = "";
                        if (LocalPlayerManager.Instance != null)
                        {
                            LocalPlayerManager.Instance.ComputeIsInGame(out hostSceneId);
                        }
                        hostSceneId = hostSceneId ?? "";
                        
                        var w = new NetDataWriter();
                        w.Put((byte)Op.SCENE_VOTE_START);
                        w.Put((byte)3);
                        w.Put(targetSceneId ?? "");
                        var flags = PackFlag.PackFlags(!string.IsNullOrEmpty(curtainGuid), useLocation, notifyEvac, saveToFile);
                        w.Put(flags);
                        if (!string.IsNullOrEmpty(curtainGuid)) w.Put(curtainGuid);
                        w.Put(locationName ?? "");
                        w.Put(hostSceneId);
                        
                        var steamIdList = new List<ulong>();
                        
                        var lobbyManager = SteamLobbyManager.Instance;
                        if (lobbyManager != null && lobbyManager.IsInLobby && SteamManager.Initialized)
                        {
                            var currentLobby = lobbyManager.CurrentLobbyId;
                            int memberCount = Steamworks.SteamMatchmaking.GetNumLobbyMembers(currentLobby);
                            
                            for (int i = 0; i < memberCount; i++)
                            {
                                var memberId = Steamworks.SteamMatchmaking.GetLobbyMemberByIndex(currentLobby, i);
                                steamIdList.Add(memberId.m_SteamID);
                                Debug.Log($"[SceneNet-RESTful] Adding lobby member SteamID: {memberId.m_SteamID}");
                            }
                        }
                        else
                        {
                            Debug.LogWarning("[SceneNet-RESTful] Not in Steam lobby, sending 0 participants");
                            steamIdList.Add(0UL);
                        }
                        
                        w.Put(steamIdList.Count);
                        foreach (var steamId in steamIdList)
                        {
                            w.Put(steamId);
                        }
                        
                        netManager.SendToAll(w, LiteNetLib.DeliveryMethod.ReliableOrdered);
                        Debug.Log($"[SceneNet-RESTful] Broadcasted vote start to all clients: target={targetSceneId}, hostScene={hostSceneId}, participants={steamIdList.Count}");
                    }
                    
                    MModUI.Instance?.UpdateVotePanel();
                },
                onError: (error) =>
                {
                    Debug.LogError($"[SceneNet-RESTful] Failed to create vote: {error}");
                }
            );
            
            return;
        }
        
        Debug.Log($"[SceneNet] RESTfulVoteSystem not available, falling back to legacy");
        
        var participantSteamIds = new List<ulong>();
        
        var voteRPC = VoteSystemRPC.Instance;
        if (voteRPC != null && voteRPC.UseRPCMode)
        {
            Debug.Log($"[SceneNet] Using RPC mode for vote system, voteRPC ready: {voteRPC != null}");
        }
        else
        {
            Debug.Log($"[SceneNet] Using legacy mode for vote system, voteRPC: {voteRPC != null}, UseRPCMode: {voteRPC?.UseRPCMode}");
        }
        
        bool useSteamIds = false;
        bool hasNonLocalConnection = false;
        
        if (SteamManager.Initialized && VirtualEndpointManager.Instance != null)
        {
            if (playerStatuses != null && playerStatuses.Count > 0)
            {
                var firstPeer = playerStatuses.Keys.FirstOrDefault();
                if (firstPeer != null)
                {
                    var ep = firstPeer.EndPoint as System.Net.IPEndPoint;
                    if (ep != null)
                    {
                        hasNonLocalConnection = !ep.Address.ToString().StartsWith("127.0.0.1");
                        useSteamIds = hasNonLocalConnection && VirtualEndpointManager.Instance.TryGetSteamID(ep, out _);
                    }
                }
            }
            else
            {
                useSteamIds = true;
            }
        }
        
        Debug.Log($"[SERVER-VOTE-BUILD] hasNonLocalConnection={hasNonLocalConnection}, useSteamIds={useSteamIds}, SteamInit={SteamManager.Initialized}");
        
        if (useSteamIds)
        {
            if (SteamManager.Initialized)
            {
                var hostSteamId = SteamUser.GetSteamID().m_SteamID;
                var hostPid = $"steam_{hostSteamId}";
                sceneParticipantIds.Add(hostPid);
                participantSteamIds.Add(hostSteamId);
                Debug.Log($"[SERVER-VOTE-BUILD] Steam模式 - 添加主机: SteamID={hostSteamId}");
            }
            
            if (playerStatuses != null && VirtualEndpointManager.Instance != null)
            {
                foreach (var kv in playerStatuses)
                {
                    var peer = kv.Key;
                    if (peer == null) continue;
                    
                    var endPoint = peer.EndPoint as System.Net.IPEndPoint;
                    if (endPoint != null && VirtualEndpointManager.Instance.TryGetSteamID(endPoint, out var steamId))
                    {
                        var pid = $"steam_{steamId.m_SteamID}";
                        if (!sceneParticipantIds.Contains(pid))
                        {
                            sceneParticipantIds.Add(pid);
                            participantSteamIds.Add(steamId.m_SteamID);
                            Debug.Log($"[SERVER-VOTE-BUILD] Steam模式 - 添加客户端: SteamID={steamId.m_SteamID}, EndPoint={endPoint}");
                        }
                    }
                }
            }
        }
        else
        {
            string hostPid = null;
            if (localPlayerStatus != null)
            {
                hostPid = localPlayerStatus.EndPoint;
            }
            else
            {
                hostPid = $"Host:{NetService.Instance.port}";
                Debug.Log($"[SERVER-VOTE-BUILD] LAN模式 - localPlayerStatus为空，使用默认主机ID");
            }
            
            if (!string.IsNullOrEmpty(hostPid))
            {
                sceneParticipantIds.Add(hostPid);
                Debug.Log($"[SERVER-VOTE-BUILD] LAN模式 - 添加主机: EndPoint={hostPid}");
            }
            
            if (playerStatuses != null)
            {
                foreach (var kv in playerStatuses)
                {
                    var peer = kv.Key;
                    if (peer == null || peer.EndPoint == null) continue;
                    
                    var pid = peer.EndPoint.ToString();
                    if (!sceneParticipantIds.Contains(pid))
                    {
                        sceneParticipantIds.Add(pid);
                        Debug.Log($"[SERVER-VOTE-BUILD] LAN模式 - 添加客户端: EndPoint={pid}");
                    }
                }
            }
            
            if (sceneParticipantIds.Count == 0)
            {
                Debug.LogWarning($"[SERVER-VOTE-BUILD] LAN模式 - 参与者列表为空！playerStatuses count={playerStatuses?.Count ?? 0}");
            }
        }

        sceneVoteActive = true;
        localReady = false;
        sceneReady.Clear();
        foreach (var pid in sceneParticipantIds) sceneReady[pid] = false;

        // 计算主机当前 SceneId
        string hostSceneId = null;
        LocalPlayerManager.Instance.ComputeIsInGame(out hostSceneId);
        hostSceneId = hostSceneId ?? string.Empty;

        var w = new NetDataWriter();
        w.Put((byte)Op.SCENE_VOTE_START);
        
        if (useSteamIds)
        {
            w.Put((byte)3);
            w.Put(sceneTargetId);

            var hasCurtain = !string.IsNullOrEmpty(sceneCurtainGuid);
            var flags = PackFlag.PackFlags(hasCurtain, sceneUseLocation, sceneNotifyEvac, sceneSaveToFile);
            w.Put(flags);

            if (hasCurtain) w.Put(sceneCurtainGuid);
            w.Put(sceneLocationName);
            w.Put(hostSceneId);

            w.Put(participantSteamIds.Count);
            foreach (var steamId in participantSteamIds)
            {
                w.Put(steamId);
            }

            Debug.Log($"[SERVER-VOTE-SEND] Steam模式发送投票(v3): target='{sceneTargetId}', participants={participantSteamIds.Count}");
            foreach (var steamId in participantSteamIds)
            {
                Debug.Log($"[SERVER-VOTE-SEND] 参与者SteamID: {steamId}");
            }
        }
        else
        {
            w.Put((byte)2);
            w.Put(sceneTargetId);

            var hasCurtain = !string.IsNullOrEmpty(sceneCurtainGuid);
            var flags = PackFlag.PackFlags(hasCurtain, sceneUseLocation, sceneNotifyEvac, sceneSaveToFile);
            w.Put(flags);

            if (hasCurtain) w.Put(sceneCurtainGuid);
            w.Put(sceneLocationName);
            w.Put(hostSceneId);

            w.Put(sceneParticipantIds.Count);
            foreach (var pid in sceneParticipantIds)
            {
                w.Put(pid);
            }

            Debug.Log($"[SERVER-VOTE-SEND] LAN模式发送投票(v2): target='{sceneTargetId}', participants={sceneParticipantIds.Count}");
            foreach (var pid in sceneParticipantIds)
            {
                Debug.Log($"[SERVER-VOTE-SEND] 参与者EndPoint: {pid}");
            }
        }
        
        Debug.Log($"[SERVER-VOTE-SEND] ConnectedPeerList count: {netManager.ConnectedPeerList?.Count ?? 0}");
        if (netManager.ConnectedPeerList != null)
        {
            foreach (var peer in netManager.ConnectedPeerList)
            {
                Debug.Log($"[SERVER-VOTE-SEND] 已连接peer: {peer?.EndPoint}");
            }
        }
        
        Debug.Log($"[SERVER-VOTE-SEND] voteRPC null? {voteRPC == null}, UseRPCMode: {voteRPC?.UseRPCMode}");
        
        if (voteRPC != null && voteRPC.UseRPCMode)
        {
            if (useSteamIds)
            {
                Debug.Log($"[SERVER-VOTE-SEND] Calling voteRPC.Server_StartVoteP2P with {participantSteamIds.Count} Steam IDs");
                voteRPC.Server_StartVoteP2P(sceneTargetId, sceneCurtainGuid, sceneNotifyEvac, sceneSaveToFile, sceneUseLocation, sceneLocationName, participantSteamIds);
            }
            else
            {
                Debug.Log($"[SERVER-VOTE-SEND] Calling voteRPC.Server_StartVoteLAN with {sceneParticipantIds.Count} EndPoints");
                voteRPC.Server_StartVoteLAN(sceneTargetId, sceneCurtainGuid, sceneNotifyEvac, sceneSaveToFile, sceneUseLocation, sceneLocationName, sceneParticipantIds.ToList());
            }
            Debug.Log($"[SERVER-VOTE-SEND] RPC call completed");
        }
        else
        {
            Debug.Log($"[SERVER-VOTE-SEND] Using legacy mode, sending via SendToAll");
            netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Debug.Log($"[SERVER-VOTE-SEND] Legacy SendToAll completed");
        }

        // 如需"只发同图"，可以替换为下面这段（二选一）：
        /*
        foreach (var p in netManager.ConnectedPeerList)
        {
            if (p == null) continue;

            string peerScene = null;
            if (!_srvPeerScene.TryGetValue(p, out peerScene) && playerStatuses.TryGetValue(p, out var st))
                peerScene = st?.SceneId;

            if (!string.IsNullOrEmpty(peerScene) && peerScene == hostSceneId)
            {
                var ww = new NetDataWriter();
                ww.Put((byte)Op.SCENE_VOTE_START);
                ww.Put((byte)2);
                ww.Put(sceneTargetId);
                ww.Put(flags);
                if (hasCurtain) ww.Put(sceneCurtainGuid);
                ww.Put(sceneLocationName);
                ww.Put(hostSceneId);
                ww.Put(sceneParticipantIds.Count);
                foreach (var pid in sceneParticipantIds) ww.Put(pid);

                p.Send(ww, DeliveryMethod.ReliableOrdered);
            }
        }
        */
    }

    public void Client_RequestBeginSceneVote(
        string targetId, string curtainGuid,
        bool notifyEvac, bool saveToFile,
        bool useLocation, string locationName)
    {
        if (!networkStarted || IsServer || connectedPeer == null) return;

        Debug.Log($"[CLIENT-VOTE-REQ] 客户端发起投票请求: target='{targetId}', useLoc={useLocation}, locName='{locationName}'");

        var voteRPC = VoteSystemRPC.Instance;
        if (voteRPC != null && voteRPC.UseRPCMode)
        {
            voteRPC.Client_RequestVote(targetId, curtainGuid, notifyEvac, saveToFile, useLocation, locationName);
            Debug.Log($"[CLIENT-VOTE-REQ] 投票请求已通过RPC发送");
            return;
        }

        var w = new NetDataWriter();
        w.Put((byte)Op.SCENE_VOTE_REQ);
        w.Put(targetId);
        w.Put(PackFlag.PackFlags(!string.IsNullOrEmpty(curtainGuid), useLocation, notifyEvac, saveToFile));
        if (!string.IsNullOrEmpty(curtainGuid)) w.Put(curtainGuid);
        w.Put(locationName ?? string.Empty);

        connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
        Debug.Log($"[CLIENT-VOTE-REQ] 投票请求已发送到服务器 (Legacy mode)");
    }

    public UniTask AppendSceneGate(UniTask original)
    {
        return Internal();

        async UniTask Internal()
        {
            // 先等待原本的其他初始化
            await original;

            try
            {
                if (!networkStarted) return;

                // 只在“关卡场景”里做门控；LevelManager 在关卡中才存在
                // （这里不去使用 waitForInitializationList / LoadScene）

                await Client_SceneGateAsync();
            }
            catch (Exception e)
            {
                Debug.LogError("[SCENE-GATE] " + e);
            }
        }
    }

    public async UniTask Client_SceneGateAsync()
    {
        if (!networkStarted || IsServer) return;

        // 1) 等到握手建立（高性能机器上 StartInit 可能早于握手）
        var connectDeadline = Time.realtimeSinceStartup + 8f;
        while (connectedPeer == null && Time.realtimeSinceStartup < connectDeadline)
            await UniTask.Delay(100);

        // 2) 重置释放标记
        _cliSceneGateReleased = false;

        var sid = _cliGateSid;
        if (string.IsNullOrEmpty(sid))
            sid = TryGuessActiveSceneId();
        _cliGateSid = sid;

        // 4) 尝试上报 READY（握手稍晚的情况，后面会重试一次）
        if (connectedPeer != null)
        {
            writer.Reset();
            writer.Put((byte)Op.SCENE_GATE_READY);
            writer.Put(localPlayerStatus != null ? localPlayerStatus.EndPoint : "");
            writer.Put(sid ?? "");
            connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        // 5) 若此时仍未连上，后台短暂轮询直到拿到 peer 后补发 READY（最多再等 5s）
        var retryDeadline = Time.realtimeSinceStartup + 5f;
        while (connectedPeer == null && Time.realtimeSinceStartup < retryDeadline)
        {
            await UniTask.Delay(200);
            if (connectedPeer != null)
            {
                writer.Reset();
                writer.Put((byte)Op.SCENE_GATE_READY);
                writer.Put(localPlayerStatus != null ? localPlayerStatus.EndPoint : "");
                writer.Put(sid ?? "");
                connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
                break;
            }
        }

        _cliGateDeadline = Time.realtimeSinceStartup + 100f; // 可调超时（防死锁）吃保底

        while (!_cliSceneGateReleased && Time.realtimeSinceStartup < _cliGateDeadline)
        {
            try
            {
                SceneLoader.LoadingComment = CoopLocalization.Get("scene.waitingForHost");
            }
            catch
            {
            }

            await UniTask.Delay(100);
        }


        //Client_ReportSelfHealth_IfReadyOnce();
        try
        {
            SceneLoader.LoadingComment = CoopLocalization.Get("scene.hostReady");
        }
        catch
        {
        }
    }

    // 主机：自身初始化完成 → 开门；已举手的立即放行；之后若有迟到的 READY，也会单放行
    public async UniTask Server_SceneGateAsync()
    {
        if (!IsServer || !networkStarted) return;

        _srvGateSid = TryGuessActiveSceneId();
        _srvSceneGateOpen = false;
        _cliGateSeverDeadline = Time.realtimeSinceStartup + 15f;

        while (Time.realtimeSinceStartup < _cliGateSeverDeadline) await UniTask.Delay(100);

        _srvSceneGateOpen = true;

        // 放行已经举手的所有客户端
        if (playerStatuses != null && playerStatuses.Count > 0)
            foreach (var kv in playerStatuses)
            {
                var peer = kv.Key;
                var st = kv.Value;
                if (peer == null || st == null) continue;
                if (_srvGateReadyPids.Contains(st.EndPoint))
                    Server_SendGateRelease(peer, _srvGateSid);
            }

        // 主机不阻塞：之后若有 SCENE_GATE_READY 迟到，就在接收处即刻单独放行 目前不想去写也没啥毛病
    }

    private void Server_SendGateRelease(NetPeer peer, string sid)
    {
        if (peer == null) return;
        var w = new NetDataWriter();
        w.Put((byte)Op.SCENE_GATE_RELEASE);
        w.Put(sid ?? "");
        peer.Send(w, DeliveryMethod.ReliableOrdered);
    }


    private string TryGuessActiveSceneId()
    {
        return sceneTargetId;
    }

    // ——安全读取（调试期防止崩溃）——
    public static bool TryGetString(NetDataReader r, out string s)
    {
        try
        {
            s = r.GetString();
            return true;
        }
        catch
        {
            s = null;
            return false;
        }
    }

    public static bool EnsureAvailable(NetDataReader r, int need)
    {
        return r.AvailableBytes >= need;
    }
}