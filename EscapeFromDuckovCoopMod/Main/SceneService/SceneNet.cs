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
using EscapeFromDuckovCoopMod.Net;  // 引入智能发送扩展方法
using EscapeFromDuckovCoopMod.Utils;
using System;
using System.Collections;
using System.Collections.Generic;

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

    // 🆕 缓存完整的投票数据（供 UI 使用，客户端从主机接收，主机从本地构建）
    public SceneVoteMessage.VoteStateData cachedVoteData = null;
    
    // 🆕 过期投票ID（客户端维护，用于过滤已取消的投票）
    public int expiredVoteId = 0;
    
    private readonly Dictionary<string, string> _cliServerPidToLocal = new();
    private readonly Dictionary<string, string> _cliLocalPidToServer = new();
    private float _cliGateDeadline;

    public bool _srvSceneGateOpen; // 暴露给 Mod.cs 用于"迟到放行"判断
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
    private Dictionary<string, PlayerStatus> clientPlayerStatuses => Service?.clientPlayerStatuses;

    private void ResetClientParticipantMappings()
    {
        _cliServerPidToLocal.Clear();
        _cliLocalPidToServer.Clear();
    }

    private void RegisterClientParticipantId(string serverPid, string localPid)
    {
        serverPid ??= string.Empty;
        localPid ??= string.Empty;
        _cliServerPidToLocal[serverPid] = localPid;
        _cliLocalPidToServer[localPid] = serverPid;
    }

    private string MapServerPidToLocal(string pid)
    {
        if (string.IsNullOrEmpty(pid)) return pid ?? string.Empty;
        return _cliServerPidToLocal.TryGetValue(pid, out var local) ? local : pid;
    }

    internal string NormalizeParticipantId(string pid) => MapServerPidToLocal(pid);

    private string ResolveClientAliasForServerPid(string serverPid)
    {
        if (string.IsNullOrEmpty(serverPid)) return string.Empty;

        if (localPlayerStatus != null && string.Equals(localPlayerStatus.EndPoint, serverPid, StringComparison.Ordinal))
            return localPlayerStatus.EndPoint;

        if (playerStatuses != null)
            foreach (var kv in playerStatuses)
            {
                var st = kv.Value;
                if (st == null) continue;
                if (!string.Equals(st.EndPoint, serverPid, StringComparison.Ordinal)) continue;

                return !string.IsNullOrEmpty(st.ClientReportedId) ? st.ClientReportedId : serverPid;
            }

        return serverPid;
    }

    private string ResolveLocalAliasFromServerPid(string serverPid, string aliasFromServer)
    {
        if (string.IsNullOrEmpty(serverPid)) return aliasFromServer ?? string.Empty;

        if (!string.IsNullOrEmpty(aliasFromServer)) return aliasFromServer;

        if (localPlayerStatus == null) return aliasFromServer ?? string.Empty;

        var me = localPlayerStatus.EndPoint ?? string.Empty;
        if (string.IsNullOrEmpty(me)) return aliasFromServer ?? string.Empty;

        if (string.Equals(serverPid, me, StringComparison.Ordinal)) return me;

        if (clientPlayerStatuses != null && clientPlayerStatuses.TryGetValue(serverPid, out var st) && st != null)
        {
            var sameName = !string.IsNullOrEmpty(st.PlayerName) &&
                           !string.IsNullOrEmpty(localPlayerStatus.PlayerName) &&
                           string.Equals(st.PlayerName, localPlayerStatus.PlayerName, StringComparison.Ordinal);

            var sameScene = !string.IsNullOrEmpty(st.SceneId) &&
                            !string.IsNullOrEmpty(localPlayerStatus.SceneId) &&
                            string.Equals(st.SceneId, localPlayerStatus.SceneId, StringComparison.Ordinal);

            if (sameName && sameScene) return me;

            if (!string.IsNullOrEmpty(localPlayerStatus.CustomFaceJson) &&
                !string.IsNullOrEmpty(st.CustomFaceJson) &&
                string.Equals(st.CustomFaceJson, localPlayerStatus.CustomFaceJson, StringComparison.Ordinal))
                return me;
        }

        return aliasFromServer ?? string.Empty;
    }

    public void Init()
    {
        Instance = this;
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

        // ✅ 场景就绪包：不再包含 faceJson，保持小包快速传输
        writer.Reset();
        writer.Put((byte)Op.SCENE_READY);
        writer.Put(localPlayerStatus?.EndPoint ?? (IsServer ? $"Host:{port}" : "Client:Unknown"));
        writer.Put(sid);
        writer.PutVector3(pos);
        writer.PutQuaternion(rot);

        if (IsServer)
            netManager?.SendSmart(writer, Op.SCENE_READY);
        else
            connectedPeer?.SendSmart(writer, Op.SCENE_READY);

        _sceneReadySidSent = sid;

        // ✅ 异步发送外观数据，不阻塞场景同步和投票流程
        SendPlayerAppearance();
    }

    /// <summary>
    /// 发送玩家外观数据（faceJson）- 独立于场景同步，异步传输
    /// </summary>
    public void SendPlayerAppearance()
    {
        if (!networkStarted) return;

        var faceJson = CustomFace.LoadLocalCustomFaceJson() ?? string.Empty;
        if (string.IsNullOrEmpty(faceJson)) return; // 没有自定义外观就不发送

        var w = new NetDataWriter();
        w.Put((byte)Op.PLAYER_APPEARANCE);
        w.Put(localPlayerStatus?.EndPoint ?? (IsServer ? $"Host:{port}" : "Client:Unknown"));
        w.Put(faceJson);

        if (IsServer)
            netManager?.SendSmart(w, Op.PLAYER_APPEARANCE);
        else
            connectedPeer?.SendSmart(w, Op.PLAYER_APPEARANCE);
    }

    /// <summary>
    /// ✅ 修复：主机在大型地图撤离时崩溃
    /// 原因：立即广播导致旧场景数据未清理完成
    /// 解决：延迟广播，等待清理完成后再执行
    /// </summary>
    private void Server_BroadcastBeginSceneLoad()
    {
        if (Spectator.Instance._spectatorActive && Spectator.Instance._spectatorEndOnVotePending)
        {
            Spectator.Instance._spectatorEndOnVotePending = false;
            Spectator.Instance.EndSpectatorAndShowClosure();
        }

        // ✅ 先清理投票状态
        sceneVoteActive = false;
        sceneParticipantIds.Clear();
        ResetClientParticipantMappings();
        sceneReady.Clear();
        localReady = false;

        // ✅ 启动延迟广播协程，等待清理完成
        if (this != null && gameObject != null) // 确保对象未销毁
        {
            StartCoroutine(BroadcastAfterCleanupCoroutine());
        }
    }

    /// <summary>
    /// ✅ 等待清理完成后再广播场景切换
    /// </summary>
    private IEnumerator BroadcastAfterCleanupCoroutine()
    {
        Debug.Log("[SCENE] ========== 开始场景切换清理流程 ==========");

        // 等待一帧，让 Unity 销毁旧场景对象
        yield return null;

        // 强制清理缓存
        Debug.Log("[SCENE] 清理游戏对象缓存...");
        if (Utils.GameObjectCacheManager.Instance != null)
        {
            Utils.GameObjectCacheManager.Instance.ClearAllCaches();
        }

        // 清理战利品数据
        Debug.Log("[SCENE] 清理战利品数据...");
        if (LootManager.Instance != null)
        {
            LootManager.Instance.ClearCaches();
        }

        // 清空异步消息队列
        Debug.Log("[SCENE] 清空异步消息队列...");
        if (Utils.AsyncMessageQueue.Instance != null)
        {
            Utils.AsyncMessageQueue.Instance.ClearQueue();
        }

        // 再等待一帧，确保所有清理完成
        yield return null;

        Debug.Log("[SCENE] ========== 清理完成，开始广播场景切换 ==========");

        // 现在安全广播
        var w = new NetDataWriter();
        w.Put((byte)Op.SCENE_BEGIN_LOAD);
        w.Put((byte)1); // ver=1
        w.Put(sceneTargetId ?? "");

        var hasCurtain = !string.IsNullOrEmpty(sceneCurtainGuid);
        var flags = PackFlag.PackFlags(hasCurtain, sceneUseLocation, sceneNotifyEvac, sceneSaveToFile);
        w.Put(flags);

        if (hasCurtain) w.Put(sceneCurtainGuid);
        w.Put(sceneLocationName ?? "");

        // ★ 群发给所有客户端
        netManager.SendSmart(w, Op.SCENE_BEGIN_LOAD);
        Debug.Log($"[SCENE] 已广播场景切换: {sceneTargetId}");

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

        Debug.Log("[SCENE] ========== 场景切换广播流程完成 ==========");
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

        // 群发给所有客户端（不再二次按"同图"过滤）
        var w = new NetDataWriter();
        w.Put((byte)Op.SCENE_READY_SET);
        w.Put(pid);
        w.Put(ready);
        // 使用 SendSmart 自动选择传输方式（SCENE_READY_SET → Critical → ReliableOrdered）
        netManager.SendSmart(w, Op.SCENE_READY_SET);

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
        if (ver != 1 && ver != 2 && ver != 3)
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
        ResetClientParticipantMappings();
        for (var i = 0; i < cnt; i++)
        {
            if (!TryGetString(r, out var pid))
            {
                Debug.LogWarning($"[SCENE] vote: bad pid[{i}]");
                return;
            }

            pid ??= string.Empty;
            string aliasFromServer = string.Empty;

            if (ver >= 3)
            {
                if (!TryGetString(r, out aliasFromServer))
                {
                    Debug.LogWarning($"[SCENE] vote: bad alias[{i}]");
                    return;
                }
            }

            var localPid = ResolveLocalAliasFromServerPid(pid, aliasFromServer);
            if (string.IsNullOrEmpty(localPid)) localPid = pid;

            RegisterClientParticipantId(pid, localPid);
            sceneParticipantIds.Add(localPid);
        }

        // ===== 过滤：不同图 & 不在白名单，直接忽略 =====
        string mySceneId = null;
        LocalPlayerManager.Instance.ComputeIsInGame(out mySceneId);
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
            if (!string.IsNullOrEmpty(me) && !sceneParticipantIds.Contains(me))
            {
                Debug.Log($"[SCENE] vote: ignore (not in participants) me='{me}'");
                var peerId = connectedPeer != null && connectedPeer.EndPoint != null
                    ? connectedPeer.EndPoint.ToString()
                    : string.Empty;
                Debug.Log($"[SCENE] vote: ignore (not in participants) local='{localPlayerStatus.EndPoint}' peer='{peerId}'");
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
        var localPid = MapServerPidToLocal(pid);
        if (sceneReady.ContainsKey(localPid)) sceneReady[localPid] = rd;
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

        // ✅ 修复：立即更新实例变量，否则后面使用的是旧值！
        sceneTargetId = id;
        sceneCurtainGuid = curtainGuid;
        sceneLocationName = locName;
        sceneNotifyEvac = notifyEvac;
        sceneSaveToFile = saveToFile;
        sceneUseLocation = useLoc;

        Debug.Log($"[SCENE] 客户端收到场景加载通知: targetId={sceneTargetId}, curtain={sceneCurtainGuid}, loc={sceneLocationName}");

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
        ResetClientParticipantMappings();
        sceneReady.Clear();
        localReady = false;
    }

    public void Client_SendReadySet(bool ready)
    {
        if (IsServer || connectedPeer == null) return;

        var w = new NetDataWriter();
        w.Put((byte)Op.SCENE_READY_SET);
        w.Put(ready);
        // 使用 SendSmart 自动选择传输方式（SCENE_READY_SET → Critical → ReliableOrdered）
        connectedPeer.SendSmart(w, Op.SCENE_READY_SET);

        // ★ 本地乐观更新：立即把自己的 ready 写进就绪表，以免 UI 卡在"未准备"
        if (sceneVoteActive && localPlayerStatus != null)
        {
            var me = localPlayerStatus.EndPoint ?? string.Empty;
            if (!string.IsNullOrEmpty(me) && sceneReady.ContainsKey(me))
                sceneReady[me] = ready;
        }
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

        // 🆕 如果是服务器，使用 JSON 系统取消投票
        if (IsServer && networkStarted && netManager != null)
        {
            // 使用新的 JSON 投票系统取消投票
            SceneVoteMessage.Host_CancelVote();
            
            // ❌ 旧的二进制消息系统已废弃，保留以兼容旧客户端
            var w = new NetDataWriter();
            w.Put((byte)Op.SCENE_CANCEL);
            netManager.SendSmart(w, Op.SCENE_CANCEL);
            Debug.Log("[SCENE] 服务器已广播取消投票消息（JSON + 二进制）");
        }

        // 清除投票状态
        sceneVoteActive = false;
        sceneParticipantIds.Clear();
        ResetClientParticipantMappings();
        sceneReady.Clear();
        localReady = false;

        // 重置场景触发器，允许重新触发投票
        EscapeFromDuckovCoopMod.Utils.SceneTriggerResetter.ResetAllSceneTriggers();
    }

    /// <summary>
    /// 客户端接收到服务器的取消投票消息
    /// </summary>
    public void Client_OnVoteCancelled()
    {
        if (IsServer)
        {
            Debug.LogWarning("[SCENE] 服务器不应该接收客户端的取消投票消息");
            return;
        }

        Debug.Log("[SCENE] 收到服务器取消投票通知，重置本地状态");

        // 清除投票状态
        sceneVoteActive = false;
        sceneParticipantIds.Clear();
        ResetClientParticipantMappings();
        sceneReady.Clear();
        localReady = false;

        // 重置场景触发器，允许重新触发投票
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
            // ✅ 优化：使用缓存管理器获取 SceneLoaderProxy，避免 FindObjectsOfType
            IEnumerable<SceneLoaderProxy> sceneLoaders = GameObjectCacheManager.Instance != null
                ? GameObjectCacheManager.Instance.Environment.GetAllSceneLoaders()
                : FindObjectsOfType<SceneLoaderProxy>();

            foreach (var ii in sceneLoaders)
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

    public void Server_HandleSceneReady(NetPeer fromPeer, string playerId, string sceneId, Vector3 pos, Quaternion rot)
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
                if (playerStatuses.TryGetValue(other, out var s) && s != null)
                {
                    opos = s.Position;
                    orot = s.Rotation;
                }

                // ✅ REMOTE_CREATE 不再包含 faceJson，保持小包快速创建角色
                var w = new NetDataWriter();
                w.Put((byte)Op.REMOTE_CREATE);
                w.Put(playerStatuses[other].EndPoint); // other 的 id
                w.Put(sceneId);
                w.PutVector3(opos);
                w.PutQuaternion(orot);
                fromPeer?.SendSmart(w, Op.REMOTE_CREATE);

                // ✅ 如果有外观数据，异步发送
                if (!string.IsNullOrEmpty(s?.CustomFaceJson))
                {
                    var wa = new NetDataWriter();
                    wa.Put((byte)Op.PLAYER_APPEARANCE);
                    wa.Put(s.EndPoint);
                    wa.Put(s.CustomFaceJson);
                    fromPeer?.SendSmart(wa, Op.PLAYER_APPEARANCE);
                }
            }
        }

        // 2) 广播给同图的其他人：创建 fromPeer
        foreach (var kv in SceneM._srvPeerScene)
        {
            var other = kv.Key;
            if (other == fromPeer) continue;
            if (kv.Value == sceneId)
            {
                // ✅ REMOTE_CREATE 不再包含 faceJson
                var w = new NetDataWriter();
                w.Put((byte)Op.REMOTE_CREATE);
                w.Put(playerId);
                w.Put(sceneId);
                w.PutVector3(pos);
                w.PutQuaternion(rot);
                other.SendSmart(w, Op.REMOTE_CREATE);

                // ✅ 外观数据由 PLAYER_APPEARANCE 单独发送（已在 TrySendSceneReadyOnce 中处理）
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
                // 使用 SendSmart 自动选择传输方式（REMOTE_DESPAWN → Critical → ReliableOrdered）
                other.SendSmart(w1, Op.REMOTE_DESPAWN);

                var w2 = new NetDataWriter();
                w2.Put((byte)Op.REMOTE_DESPAWN);
                w2.Put(playerStatuses[other].EndPoint);
                fromPeer?.SendSmart(w2, Op.REMOTE_DESPAWN);
            }
        }

        // 4) （可选）主机本地也显示客户端：在主机场景创建"该客户端"的远端克隆
        if (!remoteCharacters.TryGetValue(fromPeer, out var exists) || exists == null)
        {
            // ✅ 外观数据从 playerStatuses 获取，或使用默认空字符串
            var face = fromPeer != null && playerStatuses.TryGetValue(fromPeer, out var s) && !string.IsNullOrEmpty(s.CustomFaceJson)
                ? s.CustomFaceJson
                : string.Empty;
            CreateRemoteCharacter.CreateRemoteCharacterAsync(fromPeer, pos, rot, face).Forget();
        }
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

        // ✅ 投票开始时立即重置场景门控状态，防止主机在加载新场景时误放行客户端
        _srvSceneGateOpen = false;
        _srvGateReadyPids.Clear();
        Debug.Log("[GATE] 投票开始，重置场景门控状态");

        // ✅ 使用新的 JSON 投票系统
        SceneVoteMessage.Host_StartVote(targetSceneId, curtainGuid, notifyEvac, saveToFile, useLocation, locationName);
        Debug.Log($"[SCENE] 投票开始 (JSON): target='{targetSceneId}', loc='{locationName}'");

        // 保留旧代码以兼容（但不再发送二进制消息）
        // 参与者（同图优先；拿不到 SceneId 的竞态由客户端再过滤）
        sceneParticipantIds.Clear();
        sceneParticipantIds.AddRange(CoopTool.BuildParticipantIds_Server());

        sceneVoteActive = true;
        localReady = false;
        sceneReady.Clear();
        foreach (var pid in sceneParticipantIds) sceneReady[pid] = false;

        // ❌ 旧的二进制消息系统已禁用，使用上面的 JSON 系统
        /*
        // 计算主机当前 SceneId
        string hostSceneId = null;
        LocalPlayerManager.Instance.ComputeIsInGame(out hostSceneId);
        hostSceneId = hostSceneId ?? string.Empty;

        var w = new NetDataWriter();
        w.Put((byte)Op.SCENE_VOTE_START);
        w.Put((byte)3);
        w.Put(sceneTargetId);

        var hasCurtain = !string.IsNullOrEmpty(sceneCurtainGuid);
        var flags = PackFlag.PackFlags(hasCurtain, sceneUseLocation, sceneNotifyEvac, sceneSaveToFile);
        w.Put(flags);

        if (hasCurtain) w.Put(sceneCurtainGuid);
        w.Put(sceneLocationName); // 空串也写
        w.Put(hostSceneId);

        w.Put(sceneParticipantIds.Count);
        foreach (var pid in sceneParticipantIds)
        {
            w.Put(pid);
            var alias = ResolveClientAliasForServerPid(pid);
            w.Put(alias ?? string.Empty);
        }


        // 使用 SendSmart 自动选择传输方式（SCENE_VOTE_START → Critical → ReliableOrdered）
        netManager.SendSmart(w, Op.SCENE_VOTE_START);
        Debug.Log($"[SCENE] 投票开始 v3: target='{sceneTargetId}', hostScene='{hostSceneId}', loc='{sceneLocationName}', count={sceneParticipantIds.Count}");
        */

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
                ww.Put((byte)3);
                ww.Put(sceneTargetId);
                ww.Put(flags);
                if (hasCurtain) ww.Put(sceneCurtainGuid);
                ww.Put(sceneLocationName);
                ww.Put(hostSceneId);
                ww.Put(sceneParticipantIds.Count);
                foreach (var pid in sceneParticipantIds)
                {
                    ww.Put(pid);
                    var alias = ResolveClientAliasForServerPid(pid);
                    ww.Put(alias ?? string.Empty);
                }

                // 使用 SendSmart 自动选择传输方式（SCENE_VOTE_START → Critical → ReliableOrdered）
                p.SendSmart(ww, Op.SCENE_VOTE_START);
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

        // 使用 SendSmart 自动选择传输方式（SCENE_VOTE_REQ → Critical → ReliableOrdered）
        connectedPeer.SendSmart(w, Op.SCENE_VOTE_REQ);
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
            var myPid = localPlayerStatus != null ? localPlayerStatus.EndPoint : "";
            writer.Reset();
            writer.Put((byte)Op.SCENE_GATE_READY);
            writer.Put(myPid);
            writer.Put(sid ?? "");
            // 使用 SendSmart 自动选择传输方式（SCENE_GATE_READY → Critical → ReliableOrdered）
            connectedPeer.SendSmart(writer, Op.SCENE_GATE_READY);
            Debug.Log($"[GATE] 客户端举手：pid={myPid}, sid={sid}");
        }
        else
        {
            Debug.LogWarning("[GATE] 客户端无法举手：connectedPeer 为空");
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
                // 使用 SendSmart 自动选择传输方式（SCENE_GATE_READY → Critical → ReliableOrdered）
                connectedPeer.SendSmart(writer, Op.SCENE_GATE_READY);
                break;
            }
        }

        // ✅ 主机会在加载完成后立即放行，正常情况下不需要等太久
        // ✅ 150 秒超时作为保底，防止主机崩溃或网络异常导致死锁（大型地图加载可能需要更长时间）
        _cliGateDeadline = Time.realtimeSinceStartup + 150f;

        Debug.Log($"[GATE] 客户端等待主机放行... (超时: 150秒)");

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

        if (!_cliSceneGateReleased)
        {
            Debug.LogWarning("[GATE] 客户端等待超时（150秒），强制开始加载。主机可能崩溃或网络异常。");
        }
        else
        {
            Debug.Log("[GATE] 客户端收到主机放行，开始加载场景");
        }


        //Client_ReportSelfHealth_IfReadyOnce();
        try
        {
            SceneLoader.LoadingComment = CoopLocalization.Get("scene.hostReady");
        }
        catch
        {
        }

        // ✅ 场景切换重连功能：在场景门控完成后尝试自动重连
        if (NetService.Instance != null && !NetService.Instance.IsServer)
        {
            Debug.Log("[AUTO_RECONNECT] 场景门控完成，触发自动重连检查");
            NetService.Instance.TryAutoReconnect();
        }
    }

    // 主机：自身初始化完成 → 立即开门并放行所有已举手的客户端
    public async UniTask Server_SceneGateAsync()
    {
        if (!IsServer || !networkStarted) return;

        _srvGateSid = TryGuessActiveSceneId();

        // ✅ 主机场景已加载完成（此方法在 OnAfterLevelInitialized 中调用）
        // ✅ 立即开门，不需要等待！
        _srvSceneGateOpen = true;

        Debug.Log($"[GATE] 主机场景加载完成，开始放行客户端。已举手: {_srvGateReadyPids.Count} 人");
        Debug.Log($"[GATE] _srvGateReadyPids: [{string.Join(", ", _srvGateReadyPids)}]");
        Debug.Log($"[GATE] playerStatuses 数量: {(playerStatuses != null ? playerStatuses.Count : 0)}");

        // 放行所有已经举手的客户端
        int releasedCount = 0;
        if (playerStatuses != null && playerStatuses.Count > 0)
        {
            foreach (var kv in playerStatuses)
            {
                var peer = kv.Key;
                var st = kv.Value;
                if (peer == null || st == null)
                {
                    Debug.LogWarning($"[GATE] 跳过空的 peer 或 status");
                    continue;
                }

                var peerAddr = peer.EndPoint != null ? peer.EndPoint.ToString() : "Unknown";
                Debug.Log($"[GATE] 检查客户端: EndPoint={st.EndPoint}, PeerAddr={peerAddr}, 是否举手: {_srvGateReadyPids.Contains(st.EndPoint)}");

                if (_srvGateReadyPids.Contains(st.EndPoint))
                {
                    Server_SendGateRelease(peer, _srvGateSid);
                    Debug.Log($"[GATE] ✅ 放行客户端: {st.EndPoint}");
                    releasedCount++;
                }
            }
        }
        else
        {
            Debug.LogWarning($"[GATE] playerStatuses 为空或数量为 0！");
        }

        Debug.Log($"[GATE] 放行完成，共放行 {releasedCount} 个客户端");

        // 之后若有客户端迟到，会在 SCENE_GATE_READY 接收处立即放行（已在 Mod.cs 中实现）
        await UniTask.Yield(); // 保持 async 方法格式
    }

    private void Server_SendGateRelease(NetPeer peer, string sid)
    {
        if (peer == null) return;
        var w = new NetDataWriter();
        w.Put((byte)Op.SCENE_GATE_RELEASE);
        w.Put(sid ?? "");
        // 使用 SendSmart 自动选择传输方式（SCENE_GATE_RELEASE → Critical → ReliableOrdered）
        peer.SendSmart(w, Op.SCENE_GATE_RELEASE);

        // ✅ 修复：异步发送战利品箱全量同步，避免主线程死锁
        ModBehaviourF.Instance.StartCoroutine(Server_SendLootFullSyncDelayed(peer));
    }

    /// <summary>
    /// ✅ 延迟发送战利品箱全量同步（避免主线程死锁）
    /// ⚠️ 注意：大型地图（>500个箱子）会导致网络IO阻塞，已禁用全量同步
    /// </summary>
    private System.Collections.IEnumerator Server_SendLootFullSyncDelayed(NetPeer peer)
    {
        // 等待一帧，让主线程先完成其他操作
        yield return null;

        // ⚠️ 禁用战利品全量同步：在大型地图上会导致网络IO阻塞，主线程卡死
        // 解决方案：完全依赖增量同步（LOOT_STATE 消息），由玩家打开箱子时触发同步
        Debug.Log($"[GATE] 战利品全量同步已禁用（避免大型地图网络IO阻塞） → {peer.EndPoint}");
        Debug.Log($"[GATE] 战利品将通过增量同步（玩家交互时）自动同步");

        yield break;

        /* 原始代码（已禁用）
        try
        {
            Debug.Log($"[GATE] 开始发送战利品箱全量同步 → {peer.EndPoint}");
            LootFullSyncMessage.Host_SendLootFullSync(peer);
            Debug.Log($"[GATE] 战利品箱全量同步发送完成 → {peer.EndPoint}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GATE] 发送战利品箱全量同步失败: {ex.Message}\n{ex.StackTrace}");
        }
        */
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