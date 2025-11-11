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

using System.Collections.Generic;
using System.Linq;
using LiteNetLib;
using UnityEngine;
using EscapeFromDuckovCoopMod.Utils.Logger.Tools;

namespace EscapeFromDuckovCoopMod.Net;

/// <summary>
/// 场景投票 JSON 消息系统
/// 支持每秒广播和中途加入
/// </summary>
public static class SceneVoteMessage
{
    /// <summary>
    /// 玩家信息（包含ID、名称、SteamID）
    /// </summary>
    [System.Serializable]
    public class PlayerInfo
    {
        public string playerId; // 玩家网络ID（如 "Host:9050" 或 "192.168.1.1:9050"）
        public string playerName; // 玩家名称
        public string steamId; // Steam ID（如果有）
        public string steamName; // 🆕 Steam 用户名
        public bool ready; // 是否准备
    }

    /// <summary>
    /// 玩家列表包装类（Unity JsonUtility 需要）
    /// </summary>
    [System.Serializable]
    public class PlayerList
    {
        public PlayerInfo[] items;
    }

    /// <summary>
    /// 投票状态数据结构
    /// </summary>
    [System.Serializable]
    public class VoteStateData
    {
        public string type = "sceneVote";
        public int voteId; // 🆕 投票ID，每次投票自增，用于识别过期投票
        public bool active; // 投票是否激活
        public string targetSceneId; // 目标场景ID
        public string targetSceneDisplayName; // 🆕 目标场景显示名称（中文）
        public string curtainGuid; // 过场GUID
        public string locationName; // 位置名称
        public bool notifyEvac; // 是否通知撤离
        public bool saveToFile; // 是否保存到文件
        public bool useLocation; // 是否使用位置
        public string hostSceneId; // 主机当前场景ID
        public PlayerList playerList; // 🔧 使用包装类，Unity JsonUtility 才能正确序列化
        public int totalPlayers; // 🆕 总玩家数
        public int readyPlayers; // 🆕 已准备玩家数
        public string timestamp; // 时间戳
    }

    /// <summary>
    /// 玩家准备状态（向后兼容，已废弃）
    /// </summary>
    [System.Serializable]
    [System.Obsolete("使用 PlayerInfo 代替")]
    public class PlayerReadyState
    {
        public string playerId; // 玩家ID
        public string playerName; // 玩家名称
        public bool ready; // 是否准备
    }

    /// <summary>
    /// 客户端投票请求数据结构
    /// </summary>
    [System.Serializable]
    public class VoteRequestData
    {
        public string type = "sceneVoteRequest";
        public string targetSceneId;
        public string curtainGuid;
        public string locationName;
        public bool notifyEvac;
        public bool saveToFile;
        public bool useLocation;
        public string timestamp;
    }

    /// <summary>
    /// 客户端准备状态切换数据结构
    /// </summary>
    [System.Serializable]
    public class ReadyToggleData
    {
        public string type = "sceneVoteReady";
        public string playerId;
        public bool ready;
        public string timestamp;
    }

    /// <summary>
    /// 强制场景切换数据结构（投票成功后广播）
    /// </summary>
    [System.Serializable]
    public class ForceSceneLoadData
    {
        public string type = "forceSceneLoad";
        public string targetSceneId;
        public string curtainGuid;
        public string locationName;
        public bool notifyEvac;
        public bool saveToFile;
        public bool useLocation;
        public string timestamp;
    }

    // 主机端：当前投票状态缓存
    private static VoteStateData _hostVoteState = null;
    private static float _lastBroadcastTime = 0f;
    private const float BROADCAST_INTERVAL = 1.0f; // 每秒广播一次

    // 🆕 投票ID计数器（主机端）
    private static int _nextVoteId = 1;

    /// <summary>
    /// 主机：开始投票
    /// </summary>
    public static void Host_StartVote(
        string targetSceneId,
        string curtainGuid,
        bool notifyEvac,
        bool saveToFile,
        bool useLocation,
        string locationName
    )
    {
        var service = NetService.Instance;
        if (service == null || !service.IsServer)
        {
            LoggerHelper.LogWarning("[SceneVote] 只有主机可以发起投票");
            return;
        }

        // 计算主机当前场景ID
        string hostSceneId = null;
        LocalPlayerManager.Instance.ComputeIsInGame(out hostSceneId);
        hostSceneId = hostSceneId ?? string.Empty;

        // 🔧 构建玩家信息列表（包含ID、名称、SteamID）
        var players = new List<PlayerInfo>();

        // 添加主机自己
        var hostId = service.GetPlayerId(null);
        var hostName = service.localPlayerStatus?.PlayerName ?? "Host";
        var hostSteamId = GetSteamId(null); // 主机的SteamID
        var hostSteamName = GetSteamName(null); // 🆕 主机的Steam用户名
        players.Add(
            new PlayerInfo
            {
                playerId = hostId,
                playerName = hostName,
                steamId = hostSteamId,
                steamName = hostSteamName,
                ready = false,
            }
        );

        // 添加所有客户端
        if (service.playerStatuses != null)
        {
            foreach (var kv in service.playerStatuses)
            {
                var peer = kv.Key;
                var status = kv.Value;
                if (peer == null || status == null)
                    continue;

                var clientSteamId = GetSteamId(peer); // 客户端的SteamID
                var clientSteamName = GetSteamName(peer); // 🆕 客户端的Steam用户名
                players.Add(
                    new PlayerInfo
                    {
                        playerId = status.EndPoint,
                        playerName = status.PlayerName ?? "Player",
                        steamId = clientSteamId,
                        steamName = clientSteamName,
                        ready = false,
                    }
                );
            }
        }

        // 🔍 详细日志：显示所有玩家信息
        LoggerHelper.Log(
            $"[SceneVote] 主机构建玩家列表: {string.Join(", ", players.Select(p => $"{p.playerName}({p.playerId})"))}"
        );

        // 🆕 获取场景显示名称（中文）
        var targetSceneDisplayName = Utils.SceneNameMapper.GetDisplayName(targetSceneId);

        // 🆕 分配新的投票ID
        var currentVoteId = _nextVoteId++;

        // 创建投票状态
        _hostVoteState = new VoteStateData
        {
            voteId = currentVoteId, // 🆕 设置投票ID
            active = true,
            targetSceneId = targetSceneId,
            targetSceneDisplayName = targetSceneDisplayName, // 🆕 添加显示名称
            curtainGuid = curtainGuid,
            locationName = locationName,
            notifyEvac = notifyEvac,
            saveToFile = saveToFile,
            useLocation = useLocation,
            hostSceneId = hostSceneId,
            playerList = new PlayerList { items = players.ToArray() }, // 🔧 使用包装类
            totalPlayers = players.Count, // 🆕 总玩家数
            readyPlayers = 0, // 🆕 初始化为0
            timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        };

        LoggerHelper.Log($"[SceneVote] 主机发起投票，voteId={currentVoteId}");

        // 🔧 同步更新 SceneNet 的状态，让主机UI能正确显示
        var sceneNet = SceneNet.Instance;
        if (sceneNet != null)
        {
            sceneNet.sceneVoteActive = true;
            sceneNet.sceneTargetId = targetSceneId;
            sceneNet.sceneCurtainGuid = curtainGuid;
            sceneNet.sceneLocationName = locationName;
            sceneNet.sceneNotifyEvac = notifyEvac;
            sceneNet.sceneSaveToFile = saveToFile;
            sceneNet.sceneUseLocation = useLocation;

            // 🔧 更新参与者列表和准备状态
            sceneNet.sceneParticipantIds.Clear();
            sceneNet.sceneReady.Clear();
            foreach (var player in players)
            {
                sceneNet.sceneParticipantIds.Add(player.playerId);
                sceneNet.sceneReady[player.playerId] = false;
            }

            sceneNet.localReady = false;

            // 🆕 主机端也缓存投票数据（在 _hostVoteState 创建后同步）
            sceneNet.cachedVoteData = _hostVoteState;

            LoggerHelper.Log(
                $"[SceneVote] ✓ 已同步更新 SceneNet 状态，参与者: {sceneNet.sceneParticipantIds.Count}"
            );
        }

        // 立即广播一次
        Host_BroadcastVoteState();
        _lastBroadcastTime = Time.time;

        LoggerHelper.Log($"[SceneVote] 主机发起投票: {targetSceneId}, 参与者: {players.Count}");
    }

    /// <summary>
    /// 主机：广播投票状态（每秒调用）
    /// </summary>
    public static void Host_BroadcastVoteState()
    {
        if (_hostVoteState == null || !_hostVoteState.active)
            return;

        var service = NetService.Instance;
        if (service == null || !service.IsServer)
            return;

        // 更新时间戳
        _hostVoteState.timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

        // 🔧 使用 Newtonsoft.Json 序列化（单行输出）
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(_hostVoteState, Newtonsoft.Json.Formatting.None);
        LoggerHelper.Log($"[SceneVote] 主机广播 JSON: {json}");

        // 发送给所有客户端
        JsonMessage.BroadcastToAllClients(json, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// 主机：Update 中调用，定期广播
    /// </summary>
    public static void Host_Update()
    {
        if (_hostVoteState == null || !_hostVoteState.active)
            return;

        if (Time.time - _lastBroadcastTime >= BROADCAST_INTERVAL)
        {
            Host_BroadcastVoteState();
            _lastBroadcastTime = Time.time;
        }
    }

    /// <summary>
    /// 主机：处理客户端的准备状态切换
    /// </summary>
    public static void Host_HandleReadyToggle(string playerId, bool ready)
    {
        if (_hostVoteState == null || !_hostVoteState.active)
            return;

        // 🔧 在 playerList 中查找并更新玩家的准备状态
        bool found = false;
        if (_hostVoteState.playerList != null && _hostVoteState.playerList.items != null)
        {
            foreach (var player in _hostVoteState.playerList.items)
            {
                if (player.playerId == playerId)
                {
                    player.ready = ready;
                    found = true;
                    LoggerHelper.Log(
                        $"[SceneVote] 玩家 {player.playerName}({playerId}) 准备状态: {ready}"
                    );
                    break;
                }
            }
        }

        if (!found)
        {
            LoggerHelper.LogWarning($"[SceneVote] 未找到玩家: {playerId}");
            return;
        }

        // 🔧 同步更新主机的 SceneNet.sceneReady，让 UI 能读取到
        var sceneNet = SceneNet.Instance;
        if (
            sceneNet != null
            && _hostVoteState.playerList != null
            && _hostVoteState.playerList.items != null
        )
        {
            foreach (var player in _hostVoteState.playerList.items)
            {
                sceneNet.sceneReady[player.playerId] = player.ready;
            }
            LoggerHelper.Log($"[SceneVote] 已同步更新 SceneNet.sceneReady");
        }

        // 🆕 更新已准备玩家数
        _hostVoteState.readyPlayers = _hostVoteState.playerList?.items?.Count(p => p.ready) ?? 0;
        _hostVoteState.totalPlayers = _hostVoteState.playerList?.items?.Length ?? 0;

        // 立即广播更新
        Host_BroadcastVoteState();
        LoggerHelper.Log($"[SceneVote] 已广播更新的投票状态 ({_hostVoteState.readyPlayers}/{_hostVoteState.totalPlayers})");

        // 检查是否全员准备
        bool allReady =
            _hostVoteState.playerList != null
            && _hostVoteState.playerList.items != null
            && _hostVoteState.playerList.items.Length > 0
            && _hostVoteState.playerList.items.All(p => p.ready);

        if (allReady)
        {
            LoggerHelper.Log("[SceneVote] 全员准备，开始加载场景");
            Host_StartSceneLoad();
        }
    }

    /// <summary>
    /// 主机：开始加载场景
    /// </summary>
    private static void Host_StartSceneLoad()
    {
        if (_hostVoteState == null)
            return;

        // 🔧 检查并踢出没有SteamID的玩家（仅在Steam P2P模式下）
        var service = NetService.Instance;
        if (
            service != null
            && service.IsServer
            && service.TransportMode == NetworkTransportMode.SteamP2P  // ✅ 只在 Steam P2P 传输模式下才检查
            && SteamManager.Initialized
            && _hostVoteState.playerList != null
            && _hostVoteState.playerList.items != null
        )
        {
            var playersToKick = new System.Collections.Generic.List<string>();

            foreach (var player in _hostVoteState.playerList.items)
            {
                // 跳过主机自己
                if (service.GetPlayerId(null) == player.playerId)
                    continue;

                // 检查是否缺少SteamID
                if (string.IsNullOrEmpty(player.steamId))
                {
                    LoggerHelper.LogWarning(
                        $"[SceneVote] 玩家 {player.playerName}({player.playerId}) 缺少SteamID，准备踢出"
                    );
                    playersToKick.Add(player.playerId);
                }
            }

            // 踢出没有SteamID的玩家
            if (playersToKick.Count > 0)
            {
                LoggerHelper.LogWarning(
                    $"[SceneVote] 发现 {playersToKick.Count} 个玩家缺少SteamID，开始踢出"
                );

                foreach (var playerId in playersToKick)
                {
                    // 查找对应的NetPeer
                    if (service.playerStatuses != null)
                    {
                        foreach (var kv in service.playerStatuses)
                        {
                            var peer = kv.Key;
                            var status = kv.Value;

                            if (status != null && status.EndPoint == playerId)
                            {
                                LoggerHelper.LogWarning(
                                    $"[SceneVote] 踢出玩家: {status.PlayerName}({playerId})"
                                );
                                try
                                {
                                    peer.Disconnect();
                                }
                                catch (System.Exception ex)
                                {
                                    LoggerHelper.LogError($"[SceneVote] 踢出玩家时出错: {ex.Message}");
                                }
                                break;
                            }
                        }
                    }
                }
            }
        }

        // 🆕 广播强制场景切换 JSON 消息（确保所有客户端都能收到）
        Host_BroadcastForceSceneLoad(
            _hostVoteState.targetSceneId,
            _hostVoteState.curtainGuid,
            _hostVoteState.locationName,
            _hostVoteState.notifyEvac,
            _hostVoteState.saveToFile,
            _hostVoteState.useLocation
        );

        // 调用原有的场景加载逻辑
        var sceneNet = SceneNet.Instance;
        if (sceneNet != null)
        {
            sceneNet.sceneTargetId = _hostVoteState.targetSceneId;
            sceneNet.sceneCurtainGuid = _hostVoteState.curtainGuid;
            sceneNet.sceneNotifyEvac = _hostVoteState.notifyEvac;
            sceneNet.sceneSaveToFile = _hostVoteState.saveToFile;
            sceneNet.sceneUseLocation = _hostVoteState.useLocation;
            sceneNet.sceneLocationName = _hostVoteState.locationName;

            // 使用原有的 Server_BroadcastBeginSceneLoad 方法
            // 通过反射调用私有方法
            var method = typeof(SceneNet).GetMethod(
                "Server_BroadcastBeginSceneLoad",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            );
            if (method != null)
            {
                method.Invoke(sceneNet, null);
            }
        }

        // 清除投票状态
        _hostVoteState.active = false;
        _hostVoteState = null;
    }

    /// <summary>
    /// 主机：广播强制场景切换消息（投票成功后）
    /// </summary>
    private static void Host_BroadcastForceSceneLoad(
        string targetSceneId,
        string curtainGuid,
        string locationName,
        bool notifyEvac,
        bool saveToFile,
        bool useLocation
    )
    {
        var service = NetService.Instance;
        if (service == null || !service.IsServer)
            return;

        var data = new ForceSceneLoadData
        {
            targetSceneId = targetSceneId,
            curtainGuid = curtainGuid,
            locationName = locationName,
            notifyEvac = notifyEvac,
            saveToFile = saveToFile,
            useLocation = useLocation,
            timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        };

        string json = Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.None);
        LoggerHelper.Log($"[SceneVote] 主机广播强制场景切换 JSON: {json}");

        // 使用 Op.JSON 发送给所有客户端
        var writer = new NetDataWriter();
        writer.Put((byte)Op.JSON);
        writer.Put(json);

        service.netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// 主机：取消投票
    /// </summary>
    public static void Host_CancelVote()
    {
        if (_hostVoteState == null)
            return;

        var cancelledVoteId = _hostVoteState.voteId;
        _hostVoteState.active = false;

        // 🆕 广播取消状态（只需要发送一次，客户端会更新 expiredVoteId）
        Host_BroadcastVoteState();

        _hostVoteState = null;

        LoggerHelper.Log($"[SceneVote] 主机取消投票，voteId={cancelledVoteId}");
    }

    /// <summary>
    /// 客户端：处理接收到的投票状态
    /// </summary>
    public static void Client_HandleVoteState(string json)
    {
        var service = NetService.Instance;
        if (service == null || service.IsServer)
            return;

        // 🔍 输出接收到的完整 JSON（单行）
        LoggerHelper.Log($"[SceneVote] 客户端收到 JSON: {json}");

        try
        {
            // 🔧 使用 Newtonsoft.Json 反序列化，支持嵌套对象
            var data = Newtonsoft.Json.JsonConvert.DeserializeObject<VoteStateData>(json);
            if (data == null || data.type != "sceneVote")
            {
                LoggerHelper.LogWarning("[SceneVote] 无效的投票状态数据");
                return;
            }

            var sceneNet = SceneNet.Instance;
            if (sceneNet == null)
                return;

            // 🆕 收到投票消息时，立即上报客户端状态（确保 Steam 名字信息最新）
            // 放在过期检查之前，确保即使消息被忽略也能更新数据库
            ClientStatusMessage.Client_SendStatusUpdate();

            // 🆕 检查投票ID是否过期（voteId=0 是特殊的玩家信息更新消息，不检查过期）
            if (data.voteId > 0 && data.voteId <= sceneNet.expiredVoteId)
            {
                LoggerHelper.Log($"[SceneVote] 忽略过期投票: voteId={data.voteId}, expiredVoteId={sceneNet.expiredVoteId}");
                return;
            }

            // 如果投票已取消
            if (!data.active)
            {
                // 🆕 特殊处理：voteId=0 表示这是玩家信息更新消息（不是真正的投票）
                if (data.voteId == 0 && data.playerList != null && data.playerList.items != null)
                {
                    LoggerHelper.Log($"[SceneVote] 收到玩家信息更新消息 (voteId=0)，更新缓存但不激活投票UI");
                    
                    // 🔧 更新缓存的投票数据（供 UI 使用），但不激活投票
                    sceneNet.cachedVoteData = data;
                    
                    // 🔧 FIX: 即使不激活投票UI，也要更新参与者列表，让 UI 能显示所有玩家
                    sceneNet.sceneParticipantIds.Clear();
                    sceneNet.sceneReady.Clear();
                    
                    // 🔧 FIX: 同时更新 PlayerInfoDatabase，确保 UI 能从数据库获取到所有玩家
                    var playerDb = Utils.Database.PlayerInfoDatabase.Instance;
                    
                    foreach (var player in data.playerList.items)
                    {
                        if (string.IsNullOrEmpty(player.playerId))
                            continue;
                        
                        if (!sceneNet.sceneParticipantIds.Contains(player.playerId))
                        {
                            sceneNet.sceneParticipantIds.Add(player.playerId);
                        }
                        sceneNet.sceneReady[player.playerId] = player.ready;
                        
                        // 检查是否是自己
                        if (service.IsSelfId(player.playerId))
                        {
                            sceneNet.localReady = player.ready;
                        }
                        
                        // 🔧 FIX: 更新玩家数据库
                        if (!string.IsNullOrEmpty(player.steamId) && !string.IsNullOrEmpty(player.steamName))
                        {
                            playerDb.AddOrUpdatePlayer(
                                steamId: player.steamId,
                                playerName: player.steamName,
                                avatarUrl: "", // 投票数据中没有头像 URL
                                isLocal: service.IsSelfId(player.playerId),
                                endPoint: player.playerId,
                                lastUpdate: System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                            );
                        }
                    }
                    
                    LoggerHelper.Log($"[SceneVote] ✓ 已更新玩家信息缓存和参与者列表，共 {data.playerList.items.Length} 名玩家");
                    
                    // 🔧 FIX: 触发 MModUI 重建玩家列表
                    if (MModUI.Instance != null)
                    {
                        MModUI.Instance.UpdatePlayerList();
                    }
                    
                    return;
                }
                
                // 🆕 更新过期ID，避免后续收到旧的投票包
                sceneNet.expiredVoteId = data.voteId;
                LoggerHelper.Log($"[SceneVote] 收到投票取消通知，voteId={data.voteId}，更新 expiredVoteId={sceneNet.expiredVoteId}");

                if (sceneNet.sceneVoteActive)
                {
                    sceneNet.sceneVoteActive = false;
                    sceneNet.sceneReady.Clear();
                    sceneNet.localReady = false;
                    sceneNet.sceneParticipantIds.Clear();
                }
                return;
            }

            // 检查场景是否匹配
            string mySceneId = null;
            LocalPlayerManager.Instance.ComputeIsInGame(out mySceneId);
            mySceneId = mySceneId ?? string.Empty;

            if (!string.IsNullOrEmpty(data.hostSceneId) && !string.IsNullOrEmpty(mySceneId))
            {
                if (!string.Equals(data.hostSceneId, mySceneId, System.StringComparison.Ordinal))
                {
                    // 不同场景，忽略
                    LoggerHelper.Log(
                        $"[SceneVote] 不同场景，忽略投票: host={data.hostSceneId}, me={mySceneId}"
                    );
                    return;
                }
            }

            // 更新投票状态
            sceneNet.sceneVoteActive = true;
            sceneNet.sceneTargetId = data.targetSceneId;
            sceneNet.sceneCurtainGuid = data.curtainGuid;
            sceneNet.sceneLocationName = data.locationName;
            sceneNet.sceneNotifyEvac = data.notifyEvac;
            sceneNet.sceneSaveToFile = data.saveToFile;
            sceneNet.sceneUseLocation = data.useLocation;

            // 🔧 完全依赖主机发送的 players 数组构建参与者列表
            // 客户端不再自己构建列表，以主机为准
            sceneNet.sceneParticipantIds.Clear();
            sceneNet.sceneReady.Clear();

            // 🔍 详细日志：显示收到的玩家信息
            if (data.playerList != null && data.playerList.items != null)
            {
                LoggerHelper.Log(
                    $"[SceneVote] 收到 {data.playerList.items.Length} 个玩家信息: {string.Join(", ", data.playerList.items.Select(p => $"{p.playerName}({p.playerId})"))}"
                );

                // 从主机广播的 playerList 解析玩家列表和准备状态
                foreach (var player in data.playerList.items)
                {
                    if (string.IsNullOrEmpty(player.playerId))
                        continue;

                    LoggerHelper.Log(
                        $"[SceneVote] 解析玩家: name='{player.playerName}', id='{player.playerId}', steamId='{player.steamId}', ready={player.ready}"
                    );

                    // 添加到参与者列表
                    if (!sceneNet.sceneParticipantIds.Contains(player.playerId))
                    {
                        sceneNet.sceneParticipantIds.Add(player.playerId);
                        LoggerHelper.Log(
                            $"[SceneVote] 添加参与者: {player.playerName}({player.playerId}), IsSelfId={service.IsSelfId(player.playerId)}"
                        );
                    }
                    sceneNet.sceneReady[player.playerId] = player.ready;

                    // 检查是否是自己，更新本地准备状态
                    if (service.IsSelfId(player.playerId))
                    {
                        sceneNet.localReady = player.ready;
                        LoggerHelper.Log(
                            $"[SceneVote] 识别到自己: {player.playerName}({player.playerId})"
                        );
                    }
                }
            }
            else
            {
                LoggerHelper.LogWarning("[SceneVote] 收到的投票状态没有玩家信息");
            }

            // 🆕 缓存完整的投票数据到 SceneNet，供 UI 使用
            sceneNet.cachedVoteData = data;

            LoggerHelper.Log(
                $"[SceneVote] 更新投票状态: {data.targetSceneId}, 参与者: {sceneNet.sceneParticipantIds.Count}, 已准备: {data.readyPlayers}/{data.totalPlayers}"
            );
            LoggerHelper.Log($"[SceneVote] 参与者列表: {string.Join(", ", sceneNet.sceneParticipantIds)}");
        }
        catch (System.Exception ex)
        {
            LoggerHelper.LogError($"[SceneVote] 处理投票状态失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 客户端：切换准备状态
    /// </summary>
    public static void Client_ToggleReady(bool ready)
    {
        var service = NetService.Instance;
        if (service == null || service.IsServer)
            return;

        var myId = service.localPlayerStatus?.EndPoint ?? "";
        if (string.IsNullOrEmpty(myId))
        {
            LoggerHelper.LogWarning("[SceneVote] 无法获取本地玩家ID");
            return;
        }

        var data = new ReadyToggleData
        {
            playerId = myId,
            ready = ready,
            timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        };

        LoggerHelper.Log($"[SceneVote] 客户端发送准备状态切换: playerId={myId}, ready={ready}");
        JsonMessage.SendToHost(data, DeliveryMethod.ReliableOrdered);

        // 本地乐观更新
        var sceneNet = SceneNet.Instance;
        if (sceneNet != null && sceneNet.sceneVoteActive)
        {
            sceneNet.localReady = ready;
            if (sceneNet.sceneReady.ContainsKey(myId))
            {
                sceneNet.sceneReady[myId] = ready;
            }
            LoggerHelper.Log($"[SceneVote] 本地乐观更新完成");
        }

        LoggerHelper.Log($"[SceneVote] 客户端切换准备状态: {ready}");
    }

    /// <summary>
    /// 客户端：请求发起投票
    /// </summary>
    public static void Client_RequestVote(
        string targetSceneId,
        string curtainGuid,
        bool notifyEvac,
        bool saveToFile,
        bool useLocation,
        string locationName
    )
    {
        var service = NetService.Instance;
        if (service == null || service.IsServer)
            return;

        var data = new VoteRequestData
        {
            targetSceneId = targetSceneId,
            curtainGuid = curtainGuid,
            locationName = locationName,
            notifyEvac = notifyEvac,
            saveToFile = saveToFile,
            useLocation = useLocation,
            timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        };

        JsonMessage.SendToHost(data, DeliveryMethod.ReliableOrdered);

        LoggerHelper.Log($"[SceneVote] 客户端请求发起投票: {targetSceneId}");
    }

    /// <summary>
    /// 主机：处理客户端的投票请求
    /// </summary>
    public static void Host_HandleVoteRequest(string json)
    {
        var service = NetService.Instance;
        if (service == null || !service.IsServer)
            return;

        try
        {
            var data = JsonUtility.FromJson<VoteRequestData>(json);
            if (data == null || data.type != "sceneVoteRequest")
            {
                LoggerHelper.LogWarning("[SceneVote] 无效的投票请求数据");
                return;
            }

            LoggerHelper.Log($"[SceneVote] 收到客户端投票请求: {data.targetSceneId}");

            // 发起投票
            Host_StartVote(
                data.targetSceneId,
                data.curtainGuid,
                data.notifyEvac,
                data.saveToFile,
                data.useLocation,
                data.locationName
            );
        }
        catch (System.Exception ex)
        {
            LoggerHelper.LogError($"[SceneVote] 处理投票请求失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 主机：处理客户端的准备状态切换
    /// </summary>
    public static void Host_HandleReadyToggle(string json)
    {
        var service = NetService.Instance;
        if (service == null || !service.IsServer)
            return;

        try
        {
            LoggerHelper.Log($"[SceneVote] 主机收到准备状态切换消息: {json}");

            var data = JsonUtility.FromJson<ReadyToggleData>(json);
            if (data == null || data.type != "sceneVoteReady")
            {
                LoggerHelper.LogWarning("[SceneVote] 无效的准备状态数据");
                return;
            }

            LoggerHelper.Log($"[SceneVote] 解析成功: playerId={data.playerId}, ready={data.ready}");
            Host_HandleReadyToggle(data.playerId, data.ready);
        }
        catch (System.Exception ex)
        {
            LoggerHelper.LogError($"[SceneVote] 处理准备状态失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 客户端：处理强制场景切换消息（投票成功后）
    /// </summary>
    public static void Client_HandleForceSceneLoad(string json)
    {
        var service = NetService.Instance;
        if (service == null || service.IsServer)
            return;

        LoggerHelper.Log($"[SceneVote] 客户端收到强制场景切换 JSON: {json}");

        try
        {
            var data = Newtonsoft.Json.JsonConvert.DeserializeObject<ForceSceneLoadData>(json);
            if (data == null || data.type != "forceSceneLoad")
            {
                LoggerHelper.LogWarning("[SceneVote] 无效的强制场景切换数据");
                return;
            }

            var sceneNet = SceneNet.Instance;
            if (sceneNet == null)
            {
                LoggerHelper.LogWarning("[SceneVote] SceneNet 实例不存在");
                return;
            }

            LoggerHelper.Log($"[SceneVote] 🚀 强制场景切换: {data.targetSceneId}");

            // 🔧 立即停止投票 UI 并清除投票状态
            if (sceneNet.sceneVoteActive)
            {
                LoggerHelper.Log("[SceneVote] 停止投票 UI，准备传送");
                sceneNet.sceneVoteActive = false;
                sceneNet.sceneReady.Clear();
                sceneNet.localReady = false;
                sceneNet.sceneParticipantIds.Clear();
            }

            // 🔧 更新场景目标信息
            sceneNet.sceneTargetId = data.targetSceneId;
            sceneNet.sceneCurtainGuid = data.curtainGuid;
            sceneNet.sceneLocationName = data.locationName;
            sceneNet.sceneNotifyEvac = data.notifyEvac;
            sceneNet.sceneSaveToFile = data.saveToFile;
            sceneNet.sceneUseLocation = data.useLocation;

            // 🔧 允许本地场景加载
            sceneNet.allowLocalSceneLoad = true;

            // 🔧 执行场景切换（调用 SceneNet 的私有方法）
            var method = typeof(SceneNet).GetMethod(
                "TryPerformSceneLoad_Local",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            );

            if (method != null)
            {
                method.Invoke(
                    sceneNet,
                    new object[]
                    {
                        data.targetSceneId,
                        data.curtainGuid,
                        data.notifyEvac,
                        data.saveToFile,
                        data.useLocation,
                        data.locationName
                    }
                );
                LoggerHelper.Log($"[SceneVote] ✅ 已触发场景加载: {data.targetSceneId}");
            }
            else
            {
                LoggerHelper.LogError("[SceneVote] 无法找到 TryPerformSceneLoad_Local 方法");
            }
        }
        catch (System.Exception ex)
        {
            LoggerHelper.LogError($"[SceneVote] 处理强制场景切换失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// 🆕 获取玩家的 Steam 用户名
    /// </summary>
    private static string GetSteamName(NetPeer peer)
    {
        try
        {
            if (!SteamManager.Initialized)
            {
                return "";
            }

            if (peer == null)
            {
                // 主机自己的 Steam 用户名
                return Steamworks.SteamFriends.GetPersonaName();
            }

            // 🆕 优先从 ClientStatusMessage 的缓存中获取
            var steamIdStr = GetSteamId(peer);
            if (!string.IsNullOrEmpty(steamIdStr))
            {
                var cachedName = ClientStatusMessage.GetSteamNameFromSteamId(steamIdStr);
                if (!string.IsNullOrEmpty(cachedName))
                {
                    LoggerHelper.Log(
                        $"[SceneVote] 从缓存获取 Steam 名字: {steamIdStr} -> {cachedName}"
                    );
                    return cachedName;
                }
            }

            // 🔧 备用方案：从 Steam API 获取用户名（可能失败，仅适用于好友）
            if (!string.IsNullOrEmpty(steamIdStr) && ulong.TryParse(steamIdStr, out var steamIdValue))
            {
                var steamId = new Steamworks.CSteamID(steamIdValue);
                var steamName = Steamworks.SteamFriends.GetFriendPersonaName(steamId);
                if (!string.IsNullOrEmpty(steamName) && steamName != "[unknown]")
                {
                    LoggerHelper.Log(
                        $"[SceneVote] 从 Steam API 获取名字: {steamIdStr} -> {steamName}"
                    );
                    return steamName;
                }
            }
        }
        catch (System.Exception ex)
        {
            LoggerHelper.LogWarning($"[SceneVote] 获取Steam用户名失败: {ex.Message}");
        }

        return "";
    }

    /// <summary>
    /// 获取玩家的 Steam ID（使用与 MModUI 相同的逻辑）
    /// </summary>
    private static string GetSteamId(NetPeer peer)
    {
        try
        {
            if (!SteamManager.Initialized)
            {
                return "";
            }

            if (peer == null)
            {
                // 主机自己的 SteamID
                return Steamworks.SteamUser.GetSteamID().ToString();
            }

            // 🔧 从 PlayerStatus 获取 EndPoint，然后使用与 MModUI 相同的逻辑
            var service = NetService.Instance;
            if (service == null || service.playerStatuses == null)
            {
                return "";
            }

            if (!service.playerStatuses.TryGetValue(peer, out var status))
            {
                LoggerHelper.LogWarning($"[SceneVote] 找不到 PlayerStatus: {peer.EndPoint}");
                return "";
            }

            // 🔧 使用与 MModUI.GetSteamIdFromStatus 相同的逻辑
            var endPoint = status.EndPoint;

            // 如果是 "Steam:xxx" 格式（从Lobby直接获取的），直接解析SteamID
            if (endPoint.StartsWith("Steam:"))
            {
                var steamIdStr = endPoint.Substring(6); // 去掉 "Steam:" 前缀
                if (ulong.TryParse(steamIdStr, out ulong steamId))
                {
                    LoggerHelper.Log($"[SceneVote] 从 Steam: 格式获取 SteamID: {endPoint} -> {steamId}");
                    return steamId.ToString();
                }
            }

            // 如果是 "Host:xxx" 格式，返回房间所有者的SteamID
            if (endPoint.StartsWith("Host:"))
            {
                if (SteamLobbyManager.Instance != null && SteamLobbyManager.Instance.IsInLobby)
                {
                    var lobbyOwner = Steamworks.SteamMatchmaking.GetLobbyOwner(
                        SteamLobbyManager.Instance.CurrentLobbyId
                    );
                    LoggerHelper.Log($"[SceneVote] 从 Host: 格式获取 SteamID: {endPoint} -> {lobbyOwner.m_SteamID}");
                    return lobbyOwner.m_SteamID.ToString();
                }
            }

            // 🔧 尝试从虚拟IP EndPoint获取（直连模式）
            var parts = endPoint.Split(':');
            if (
                parts.Length == 2
                && System.Net.IPAddress.TryParse(parts[0], out var ipAddr)
                && int.TryParse(parts[1], out var port)
            )
            {
                var ipEndPoint = new System.Net.IPEndPoint(ipAddr, port);
                if (
                    SteamEndPointMapper.Instance != null
                    && SteamEndPointMapper.Instance.TryGetSteamID(ipEndPoint, out var cSteamId)
                )
                {
                    LoggerHelper.Log($"[SceneVote] 从虚拟 IP 获取 SteamID: {endPoint} -> {cSteamId.m_SteamID}");
                    return cSteamId.m_SteamID.ToString();
                }
                else
                {
                    LoggerHelper.LogWarning($"[SceneVote] 无法从虚拟 IP 获取 SteamID: {endPoint}");
                }
            }
        }
        catch (System.Exception ex)
        {
            LoggerHelper.LogWarning($"[SceneVote] 获取SteamID失败: {ex.Message}\n{ex.StackTrace}");
        }

        return ""; // 如果没有 Steam 或获取失败，返回空字符串
    }

    /// <summary>
    /// 检查是否有活跃的投票
    /// </summary>
    public static bool HasActiveVote()
    {
        return _hostVoteState != null && _hostVoteState.active;
    }

    /// <summary>
    /// 更新玩家的 EndPoint（端口变化时）
    /// </summary>
    public static void UpdatePlayerEndPoint(string oldEndPoint, string newEndPoint, string steamName)
    {
        if (_hostVoteState == null || _hostVoteState.playerList == null || _hostVoteState.playerList.items == null)
            return;

        // 查找并更新玩家信息
        foreach (var player in _hostVoteState.playerList.items)
        {
            if (player.playerId == oldEndPoint)
            {
                player.playerId = newEndPoint;
                
                // 🔧 同时更新 Steam 名字（如果提供）
                if (!string.IsNullOrEmpty(steamName))
                {
                    player.steamName = steamName;
                }

                LoggerHelper.Log(
                    $"[SceneVote] ✓ 已更新投票玩家列表: {oldEndPoint} -> {newEndPoint}, steamName={steamName}"
                );
                
                // 立即广播更新后的状态
                Host_BroadcastVoteState();
                break;
            }
        }
    }
}
