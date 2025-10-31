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

using Duckov.UI;

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

    // 所有端都使用主机广播的这份参与者 pid 列表（关键：统一 pid）
    public readonly List<string> sceneParticipantIds = new();

    // 就绪表（key = 上面那个 pid）
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
    }

    public void TrySendSceneReadyOnce()
    {
        if (!networkStarted) return;

        // 只有真正进入地图（拿到 SceneId）才上报
        if (!LoaclPlayerManager.Instance.ComputeIsInGame(out var sid) || string.IsNullOrEmpty(sid)) return;
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

    private void Server_BroadcastBeginSceneLoad()
    {
        if (Spectator.Instance._spectatorActive && Spectator.Instance._spectatorEndOnVotePending)
        {
            Spectator.Instance._spectatorEndOnVotePending = false;
            Spectator.Instance.EndSpectatorAndShowClosure();
        }

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

        // 统一 pid（fromPeer==null 代表主机自己）
        var pid = fromPeer != null ? NetService.Instance.GetPlayerId(fromPeer) : NetService.Instance.GetPlayerId(null);

        if (!sceneVoteActive) return;
        if (!sceneReady.ContainsKey(pid)) return; // 不在这轮投票里，丢弃

        sceneReady[pid] = ready;

        // 群发给所有客户端（不再二次按“同图”过滤）
        var w = new NetDataWriter();
        w.Put((byte)Op.SCENE_READY_SET);
        w.Put(pid);
        w.Put(ready);
        netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);

        // 检查是否全员准备
        foreach (var id in sceneParticipantIds)
            if (!sceneReady.TryGetValue(id, out var r) || !r)
                return;

        // 全员就绪 → 开始加载
        Server_BroadcastBeginSceneLoad();
    }

    // ===== 客户端：收到“投票开始”（带参与者 pid 列表）=====
    public void Client_OnSceneVoteStart(NetPacketReader r)
    {
        // ——读包：严格按顺序——
        if (!EnsureAvailable(r, 2))
        {
            Debug.LogWarning("[SCENE] vote: header too short");
            return;
        }

        var ver = r.GetByte(); // switch 里已经吃掉了 op，这里是 ver
        if (ver != 1 && ver != 2)
        {
            Debug.LogWarning($"[SCENE] vote: unsupported ver={ver}");
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
        for (var i = 0; i < cnt; i++)
        {
            if (!TryGetString(r, out var pid))
            {
                Debug.LogWarning($"[SCENE] vote: bad pid[{i}]");
                return;
            }

            sceneParticipantIds.Add(pid ?? "");
        }

        // ===== 过滤：不同图 & 不在白名单，直接忽略 =====
        string mySceneId = null;
        LoaclPlayerManager.Instance.ComputeIsInGame(out mySceneId);
        mySceneId = mySceneId ?? string.Empty;

        // A) 同图过滤（仅 v2 有 hostSceneId；v1 无法判断同图，用 B 兜底）
        if (!string.IsNullOrEmpty(hostSceneId) && !string.IsNullOrEmpty(mySceneId))
            if (!string.Equals(hostSceneId, mySceneId, StringComparison.Ordinal))
            {
                Debug.Log($"[SCENE] vote: ignore (diff scene) host='{hostSceneId}' me='{mySceneId}'");
                return;
            }

        // B) 白名单过滤：不在参与名单，就不显示
        if (sceneParticipantIds.Count > 0 && localPlayerStatus != null)
        {
            var me = localPlayerStatus.EndPoint ?? string.Empty;
            if (!sceneParticipantIds.Contains(me))
            {
                Debug.Log($"[SCENE] vote: ignore (not in participants) me='{me}'");
                return;
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

        Debug.Log($"[SCENE] 收到投票 v{ver}: target='{sceneTargetId}', hostScene='{hostSceneId}', myScene='{mySceneId}', players={cnt}");

        // TODO：在这里弹出你的投票 UI（如果之前就是这里弹的，维持不变）
        // ShowSceneVoteUI(sceneTargetId, sceneLocationName, sceneParticipantIds) 等
    }


    // ===== 客户端：收到“某人准备状态变更”（pid + ready）=====
    private void Client_OnSomeoneReadyChanged(NetPacketReader r)
    {
        var pid = r.GetString();
        var rd = r.GetBool();
        if (sceneReady.ContainsKey(pid)) sceneReady[pid] = rd;
    }

    public void Client_OnBeginSceneLoad(NetPacketReader r)
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

        var w = new NetDataWriter();
        w.Put((byte)Op.SCENE_READY_SET);
        w.Put(ready);
        connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);

        // ★ 本地乐观更新：立即把自己的 ready 写进就绪表，以免 UI 卡在“未准备”
        if (sceneVoteActive && localPlayerStatus != null)
        {
            var me = localPlayerStatus.EndPoint ?? string.Empty;
            if (!string.IsNullOrEmpty(me) && sceneReady.ContainsKey(me))
                sceneReady[me] = ready;
        }
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

    public void Server_HandleSceneReady(NetPeer fromPeer, string playerId, string sceneId, Vector3 pos, Quaternion rot, string faceJson)
    {
        if (fromPeer != null) SceneM._srvPeerScene[fromPeer] = sceneId;

        // 1) 回给 fromPeer：同图的所有已知玩家
        foreach (var kv in SceneM._srvPeerScene)
        {
            var other = kv.Key;
            if (other == fromPeer) continue;
            if (kv.Value == sceneId)
            {
                // 取 other 的快照（尽量从 playerStatuses 或远端对象抓取）
                var opos = Vector3.zero;
                var orot = Quaternion.identity;
                var oface = "";
                if (playerStatuses.TryGetValue(other, out var s) && s != null)
                {
                    opos = s.Position;
                    orot = s.Rotation;
                    oface = s.CustomFaceJson ?? "";
                }

                var w = new NetDataWriter();
                w.Put((byte)Op.REMOTE_CREATE);
                w.Put(playerStatuses[other].EndPoint); // other 的 id
                w.Put(sceneId);
                w.PutVector3(opos);
                w.PutQuaternion(orot);
                w.Put(oface);
                fromPeer?.Send(w, DeliveryMethod.ReliableOrdered);
            }
        }

        // 2) 广播给同图的其他人：创建 fromPeer
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
                w.PutVector3(pos);
                w.PutQuaternion(rot);
                var useFace = !string.IsNullOrEmpty(faceJson) ? faceJson :
                    playerStatuses.TryGetValue(fromPeer, out var ss) && !string.IsNullOrEmpty(ss.CustomFaceJson) ? ss.CustomFaceJson : "";
                w.Put(useFace);
                other.Send(w, DeliveryMethod.ReliableOrdered);
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

        // 参与者（同图优先；拿不到 SceneId 的竞态由客户端再过滤）
        sceneParticipantIds.Clear();
        sceneParticipantIds.AddRange(CoopTool.BuildParticipantIds_Server());

        sceneVoteActive = true;
        localReady = false;
        sceneReady.Clear();
        foreach (var pid in sceneParticipantIds) sceneReady[pid] = false;

        // 计算主机当前 SceneId
        string hostSceneId = null;
        LoaclPlayerManager.Instance.ComputeIsInGame(out hostSceneId);
        hostSceneId = hostSceneId ?? string.Empty;

        var w = new NetDataWriter();
        w.Put((byte)Op.SCENE_VOTE_START);
        w.Put((byte)2);
        w.Put(sceneTargetId);

        var hasCurtain = !string.IsNullOrEmpty(sceneCurtainGuid);
        var flags = PackFlag.PackFlags(hasCurtain, sceneUseLocation, sceneNotifyEvac, sceneSaveToFile);
        w.Put(flags);

        if (hasCurtain) w.Put(sceneCurtainGuid);
        w.Put(sceneLocationName); // 空串也写
        w.Put(hostSceneId);

        w.Put(sceneParticipantIds.Count);
        foreach (var pid in sceneParticipantIds) w.Put(pid);


        netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        Debug.Log($"[SCENE] 投票开始 v2: target='{sceneTargetId}', hostScene='{hostSceneId}', loc='{sceneLocationName}', count={sceneParticipantIds.Count}");

        // 如需“只发同图”，可以替换为下面这段（二选一）：
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

        var w = new NetDataWriter();
        w.Put((byte)Op.SCENE_VOTE_REQ);
        w.Put(targetId);
        w.Put(PackFlag.PackFlags(!string.IsNullOrEmpty(curtainGuid), useLocation, notifyEvac, saveToFile));
        if (!string.IsNullOrEmpty(curtainGuid)) w.Put(curtainGuid);
        w.Put(locationName ?? string.Empty);

        connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
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
    public static bool TryGetString(NetPacketReader r, out string s)
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

    public static bool EnsureAvailable(NetPacketReader r, int need)
    {
        return r.AvailableBytes >= need;
    }
}