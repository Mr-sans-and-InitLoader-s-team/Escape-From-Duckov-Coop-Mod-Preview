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

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Steamworks;
using LiteNetLib;
using EscapeFromDuckovCoopMod.Chat.Models;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 统一聊天传输层
    /// 自动选择 Steam P2P 或直连 UDP 传输聊天消息
    /// </summary>
    public class UnifiedChatTransport : MonoBehaviour
    {
        #region 单例模式

        private static UnifiedChatTransport _instance;

        /// <summary>
        /// 统一聊天传输层单例实例
        /// </summary>
        public static UnifiedChatTransport Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("UnifiedChatTransport");
                    _instance = go.AddComponent<UnifiedChatTransport>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        #endregion

        #region 常量定义

        /// <summary>
        /// Steam P2P 聊天通道
        /// </summary>
        private const int STEAM_CHAT_CHANNEL = 1;

        /// <summary>
        /// 最大消息大小（字节）
        /// </summary>
        private const int MAX_MESSAGE_SIZE = 64 * 1024; // 64KB

        #endregion

        #region 字段和属性

        /// <summary>
        /// 是否已初始化
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// 当前是否为主机
        /// </summary>
        public bool IsHost { get; private set; }

        /// <summary>
        /// 当前 Steam 大厅 ID
        /// </summary>
        public CSteamID CurrentLobbyId { get; private set; }

        /// <summary>
        /// Steam 是否可用
        /// </summary>
        public bool IsSteamAvailable => SteamManager.Initialized;

        /// <summary>
        /// 直连网络是否可用
        /// </summary>
        public bool IsDirectNetworkAvailable => NetService.Instance?.netManager != null;

        /// <summary>
        /// Steam 客户端映射表 (SteamID -> 端点字符串)
        /// </summary>
        private readonly Dictionary<CSteamID, string> _steamClientMap = new Dictionary<CSteamID, string>();

        /// <summary>
        /// 端点到 SteamID 的反向映射表
        /// </summary>
        private readonly Dictionary<string, CSteamID> _endpointToSteamId = new Dictionary<string, CSteamID>();

        /// <summary>
        /// 已处理的消息 ID 缓存（用于去重）
        /// </summary>
        private readonly HashSet<string> _processedMessageIds = new HashSet<string>();

        /// <summary>
        /// 消息 ID 缓存的最大大小
        /// </summary>
        private const int MAX_MESSAGE_CACHE_SIZE = 1000;

        /// <summary>
        /// Steam P2P 会话请求回调
        /// </summary>
        private Callback<P2PSessionRequest_t> _p2pSessionRequestCallback;

        /// <summary>
        /// 消息接收缓冲区
        /// </summary>
        private readonly byte[] _receiveBuffer = new byte[MAX_MESSAGE_SIZE];

        #endregion

        #region 事件

        /// <summary>
        /// 聊天消息接收事件
        /// </summary>
        public event Action<string, string> OnChatMessageReceived; // (messageJson, senderEndpoint)

        /// <summary>
        /// 传输错误事件
        /// </summary>
        public event Action<string> OnTransportError;

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            Initialize();
        }

        private void Update()
        {
            if (!IsInitialized)
                return;

            // 处理 Steam P2P 消息
            ProcessSteamP2PMessages();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                Shutdown();
                _instance = null;
            }
        }

        #endregion

        #region 初始化和清理

        /// <summary>
        /// 初始化传输层
        /// </summary>
        private void Initialize()
        {
            try
            {
                LogInfo("正在初始化统一聊天传输层...");

                // 初始化 Steam 回调
                if (IsSteamAvailable)
                {
                    _p2pSessionRequestCallback = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
                    LogInfo("Steam P2P 回调已注册");
                }

                IsInitialized = true;
                LogInfo("统一聊天传输层初始化完成");
            }
            catch (Exception ex)
            {
                LogError($"初始化统一聊天传输层时发生异常: {ex.Message}");
                OnTransportError?.Invoke($"初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 关闭传输层
        /// </summary>
        private void Shutdown()
        {
            try
            {
                LogInfo("正在关闭统一聊天传输层...");

                // 关闭所有 Steam P2P 会话
                if (IsSteamAvailable)
                {
                    foreach (var steamId in _steamClientMap.Keys)
                    {
                        SteamNetworking.CloseP2PSessionWithUser(steamId);
                    }
                }

                _steamClientMap.Clear();
                _endpointToSteamId.Clear();

                IsInitialized = false;
                LogInfo("统一聊天传输层已关闭");
            }
            catch (Exception ex)
            {
                LogError($"关闭统一聊天传输层时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 公共接口

        /// <summary>
        /// 设置为主机模式
        /// </summary>
        /// <param name="lobbyId">Steam 大厅 ID（可选）</param>
        public void SetAsHost(CSteamID lobbyId = default)
        {
            IsHost = true;
            CurrentLobbyId = lobbyId;
            LogInfo($"设置为主机模式，Steam 大厅 ID: {(lobbyId.IsValid() ? lobbyId.ToString() : "无")}");
            
            // 发送系统消息
            if (lobbyId.IsValid())
            {
                SendSystemChatMessage($"聊天服务已启动（主机模式 - Steam 大厅）");
            }
            else
            {
                SendSystemChatMessage($"聊天服务已启动（主机模式 - 直连 UDP）");
            }
        }

        /// <summary>
        /// 设置为客户端模式
        /// </summary>
        /// <param name="lobbyId">Steam 大厅 ID（可选）</param>
        public void SetAsClient(CSteamID lobbyId = default)
        {
            IsHost = false;
            CurrentLobbyId = lobbyId;
            LogInfo($"设置为客户端模式，Steam 大厅 ID: {(lobbyId.IsValid() ? lobbyId.ToString() : "无")}");
            
            // 发送系统消息
            if (lobbyId.IsValid())
            {
                SendSystemChatMessage($"正在连接聊天服务（Steam P2P 模式）...");
            }
            else
            {
                SendSystemChatMessage($"正在连接聊天服务（直连 UDP 模式）...");
            }
        }

        /// <summary>
        /// 注册客户端的 SteamID 映射
        /// </summary>
        /// <param name="endpoint">客户端端点</param>
        /// <param name="steamId">Steam ID</param>
        public void RegisterClientSteamId(string endpoint, CSteamID steamId)
        {
            if (string.IsNullOrEmpty(endpoint) || !steamId.IsValid())
            {
                LogWarning($"无效的客户端注册参数: endpoint={endpoint}, steamId={steamId}");
                return;
            }

            _steamClientMap[steamId] = endpoint;
            _endpointToSteamId[endpoint] = steamId;
            LogInfo($"注册客户端 SteamID 映射: {endpoint} <-> {steamId}");
        }

        /// <summary>
        /// 发送聊天消息
        /// </summary>
        /// <param name="messageJson">消息 JSON</param>
        /// <param name="targetEndpoint">目标端点（null 表示广播）</param>
        /// <returns>发送是否成功</returns>
        public bool SendChatMessage(string messageJson, string targetEndpoint = null)
        {
            if (!IsInitialized)
            {
                LogError("传输层未初始化");
                return false;
            }

            try
            {
                // 如果指定了目标端点，尝试单播
                if (!string.IsNullOrEmpty(targetEndpoint))
                {
                    return SendToTarget(messageJson, targetEndpoint);
                }

                // 否则广播给所有客户端
                return BroadcastToAll(messageJson);
            }
            catch (Exception ex)
            {
                LogError($"发送聊天消息时发生异常: {ex.Message}");
                OnTransportError?.Invoke($"发送失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 消息发送

        /// <summary>
        /// 发送消息给指定目标
        /// </summary>
        /// <param name="messageJson">消息 JSON</param>
        /// <param name="targetEndpoint">目标端点</param>
        /// <returns>发送是否成功</returns>
        private bool SendToTarget(string messageJson, string targetEndpoint)
        {
            // 优先尝试 Steam P2P
            if (TrySendViaSteamP2P(messageJson, targetEndpoint))
            {
                LogDebug($"通过 Steam P2P 发送消息到: {targetEndpoint}");
                return true;
            }

            // 降级到直连 UDP
            if (TrySendViaDirectUDP(messageJson, targetEndpoint))
            {
                LogDebug($"通过直连 UDP 发送消息到: {targetEndpoint}");
                return true;
            }

            LogWarning($"无法发送消息到: {targetEndpoint}");
            return false;
        }

        /// <summary>
        /// 广播消息给所有客户端
        /// </summary>
        /// <param name="messageJson">消息 JSON</param>
        /// <returns>广播是否成功</returns>
        private bool BroadcastToAll(string messageJson)
        {
            bool anySuccess = false;

            // 尝试通过 Steam P2P 广播
            if (IsSteamAvailable && CurrentLobbyId.IsValid())
            {
                int steamSuccessCount = 0;
                foreach (var kvp in _steamClientMap)
                {
                    if (SendViaSteamP2P(messageJson, kvp.Key))
                    {
                        steamSuccessCount++;
                        anySuccess = true;
                    }
                }

                if (steamSuccessCount > 0)
                {
                    LogDebug($"通过 Steam P2P 广播消息给 {steamSuccessCount} 个客户端");
                }
            }

            // 同时通过直连 UDP 广播（兼容性）
            if (IsDirectNetworkAvailable && NetService.Instance.IsServer)
            {
                int udpSuccessCount = 0;
                foreach (var peer in NetService.Instance.playerStatuses.Keys)
                {
                    if (peer != null && peer.ConnectionState == ConnectionState.Connected)
                    {
                        if (SendViaDirectUDP(messageJson, peer))
                        {
                            udpSuccessCount++;
                            anySuccess = true;
                        }
                    }
                }

                if (udpSuccessCount > 0)
                {
                    LogDebug($"通过直连 UDP 广播消息给 {udpSuccessCount} 个客户端");
                }
            }

            return anySuccess;
        }

        /// <summary>
        /// 尝试通过 Steam P2P 发送消息
        /// </summary>
        /// <param name="messageJson">消息 JSON</param>
        /// <param name="targetEndpoint">目标端点</param>
        /// <returns>发送是否成功</returns>
        private bool TrySendViaSteamP2P(string messageJson, string targetEndpoint)
        {
            if (!IsSteamAvailable)
                return false;

            // 查找目标的 SteamID
            if (!_endpointToSteamId.TryGetValue(targetEndpoint, out CSteamID targetSteamId))
            {
                LogDebug($"未找到端点的 SteamID 映射: {targetEndpoint}");
                return false;
            }

            return SendViaSteamP2P(messageJson, targetSteamId);
        }

        /// <summary>
        /// 通过 Steam P2P 发送消息
        /// </summary>
        /// <param name="messageJson">消息 JSON</param>
        /// <param name="targetSteamId">目标 Steam ID</param>
        /// <returns>发送是否成功</returns>
        private bool SendViaSteamP2P(string messageJson, CSteamID targetSteamId)
        {
            try
            {
                // 将消息转换为字节数组
                byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(messageJson);

                if (messageBytes.Length > MAX_MESSAGE_SIZE)
                {
                    LogError($"消息大小超过限制: {messageBytes.Length} > {MAX_MESSAGE_SIZE}");
                    return false;
                }

                // 通过 Steam P2P 发送
                bool sent = SteamNetworking.SendP2PPacket(
                    targetSteamId,
                    messageBytes,
                    (uint)messageBytes.Length,
                    EP2PSend.k_EP2PSendReliable,
                    STEAM_CHAT_CHANNEL
                );

                if (sent)
                {
                    LogDebug($"Steam P2P 消息已发送: {targetSteamId}, 大小: {messageBytes.Length} 字节");
                }
                else
                {
                    LogWarning($"Steam P2P 消息发送失败: {targetSteamId}");
                }

                return sent;
            }
            catch (Exception ex)
            {
                LogError($"通过 Steam P2P 发送消息时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 尝试通过直连 UDP 发送消息
        /// </summary>
        /// <param name="messageJson">消息 JSON</param>
        /// <param name="targetEndpoint">目标端点</param>
        /// <returns>发送是否成功</returns>
        private bool TrySendViaDirectUDP(string messageJson, string targetEndpoint)
        {
            if (!IsDirectNetworkAvailable)
                return false;

            // 查找目标的 NetPeer
            NetPeer targetPeer = null;
            foreach (var peer in NetService.Instance.playerStatuses.Keys)
            {
                if (peer != null && peer.EndPoint.ToString() == targetEndpoint)
                {
                    targetPeer = peer;
                    break;
                }
            }

            if (targetPeer == null)
            {
                LogDebug($"未找到端点的 NetPeer: {targetEndpoint}");
                return false;
            }

            return SendViaDirectUDP(messageJson, targetPeer);
        }

        /// <summary>
        /// 通过直连 UDP 发送消息
        /// </summary>
        /// <param name="messageJson">消息 JSON</param>
        /// <param name="targetPeer">目标 NetPeer</param>
        /// <returns>发送是否成功</returns>
        private bool SendViaDirectUDP(string messageJson, NetPeer targetPeer)
        {
            try
            {
                var writer = new NetDataWriter();
                writer.Put((byte)Op.CHAT_MESSAGE_BROADCAST);
                writer.Put(messageJson);

                targetPeer.Send(writer, DeliveryMethod.ReliableOrdered);

                LogDebug($"直连 UDP 消息已发送: {targetPeer.EndPoint}");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"通过直连 UDP 发送消息时发生异常: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 消息接收

        /// <summary>
        /// 处理 Steam P2P 消息
        /// </summary>
        private void ProcessSteamP2PMessages()
        {
            if (!IsSteamAvailable)
                return;

            try
            {
                uint messageSize;

                // 获取当前用户的 Steam ID（用于过滤自己发送的消息）
                CSteamID mySteamId = SteamUser.GetSteamID();

                // 检查是否有待处理的消息
                while (SteamNetworking.IsP2PPacketAvailable(out messageSize, STEAM_CHAT_CHANNEL))
                {
                    if (messageSize > MAX_MESSAGE_SIZE)
                    {
                        LogWarning($"收到超大 Steam P2P 消息，跳过: {messageSize} > {MAX_MESSAGE_SIZE}");
                        continue;
                    }

                    CSteamID senderId;

                    // 读取消息
                    if (SteamNetworking.ReadP2PPacket(_receiveBuffer, messageSize, out messageSize, out senderId, STEAM_CHAT_CHANNEL))
                    {
                        // 过滤掉自己发送的消息（避免无限循环）
                        if (senderId == mySteamId)
                        {
                            LogDebug($"忽略来自自己的 Steam P2P 消息: {senderId}");
                            continue;
                        }

                        // 将字节数组转换为字符串
                        string messageJson = System.Text.Encoding.UTF8.GetString(_receiveBuffer, 0, (int)messageSize);

                        // 检查消息是否已处理（去重）
                        if (!ShouldProcessMessage(messageJson))
                        {
                            LogDebug($"忽略重复的 Steam P2P 消息: {senderId}");
                            continue;
                        }

                        // 获取发送者端点
                        string senderEndpoint = _steamClientMap.ContainsKey(senderId) 
                            ? _steamClientMap[senderId] 
                            : senderId.m_SteamID.ToString();

                        LogDebug($"收到 Steam P2P 聊天消息: {senderEndpoint}, 大小: {messageSize} 字节");

                        // 触发消息接收事件
                        OnChatMessageReceived?.Invoke(messageJson, senderEndpoint);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"处理 Steam P2P 消息时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理直连 UDP 聊天消息（由 NetService 调用）
        /// </summary>
        /// <param name="messageJson">消息 JSON</param>
        /// <param name="senderEndpoint">发送者端点</param>
        public void HandleDirectUDPChatMessage(string messageJson, string senderEndpoint)
        {
            try
            {
                LogDebug($"收到直连 UDP 聊天消息: {senderEndpoint}");

                // 检查消息是否已处理（去重）
                if (!ShouldProcessMessage(messageJson))
                {
                    LogDebug($"忽略重复的 UDP 消息: {senderEndpoint}");
                    return;
                }

                // 触发消息接收事件
                OnChatMessageReceived?.Invoke(messageJson, senderEndpoint);
            }
            catch (Exception ex)
            {
                LogError($"处理直连 UDP 聊天消息时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region Steam 回调处理

        /// <summary>
        /// 处理 P2P 会话请求
        /// </summary>
        /// <param name="callback">回调数据</param>
        private void OnP2PSessionRequest(P2PSessionRequest_t callback)
        {
            try
            {
                LogInfo($"收到 Steam P2P 会话请求: {callback.m_steamIDRemote}");

                // 如果在大厅中，验证请求者是否为大厅成员
                if (CurrentLobbyId.IsValid())
                {
                    int memberCount = SteamMatchmaking.GetNumLobbyMembers(CurrentLobbyId);
                    bool isMember = false;

                    for (int i = 0; i < memberCount; i++)
                    {
                        CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(CurrentLobbyId, i);
                        if (memberId == callback.m_steamIDRemote)
                        {
                            isMember = true;
                            break;
                        }
                    }

                    if (isMember)
                    {
                        // 接受会话请求
                        SteamNetworking.AcceptP2PSessionWithUser(callback.m_steamIDRemote);
                        LogInfo($"已接受 Steam P2P 会话请求: {callback.m_steamIDRemote}");
                        
                        // 发送系统消息到聊天
                        SendSystemChatMessage($"Steam P2P 连接已建立: {callback.m_steamIDRemote}");
                    }
                    else
                    {
                        LogWarning($"拒绝非大厅成员的 P2P 会话请求: {callback.m_steamIDRemote}");
                    }
                }
                else
                {
                    // 如果没有大厅，默认接受（兼容直连模式）
                    SteamNetworking.AcceptP2PSessionWithUser(callback.m_steamIDRemote);
                    LogInfo($"已接受 Steam P2P 会话请求（无大厅验证）: {callback.m_steamIDRemote}");
                    
                    // 发送系统消息到聊天
                    SendSystemChatMessage($"Steam P2P 连接已建立（直连模式）: {callback.m_steamIDRemote}");
                }
            }
            catch (Exception ex)
            {
                LogError($"处理 P2P 会话请求时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 检查消息是否应该被处理（去重）
        /// </summary>
        /// <param name="messageJson">消息 JSON</param>
        /// <returns>是否应该处理</returns>
        private bool ShouldProcessMessage(string messageJson)
        {
            try
            {
                // 尝试从 JSON 中提取消息 ID
                var message = ChatMessage.FromJson(messageJson);
                if (message == null || string.IsNullOrEmpty(message.Id))
                {
                    LogWarning("无法从消息中提取 ID，允许处理");
                    return true;
                }

                // 检查消息 ID 是否已处理
                if (_processedMessageIds.Contains(message.Id))
                {
                    return false; // 已处理，跳过
                }

                // 添加到已处理集合
                _processedMessageIds.Add(message.Id);

                // 如果缓存过大，清理旧的消息 ID
                if (_processedMessageIds.Count > MAX_MESSAGE_CACHE_SIZE)
                {
                    // 简单策略：清空一半
                    var toRemove = _processedMessageIds.Count / 2;
                    var itemsToRemove = new List<string>();
                    
                    foreach (var id in _processedMessageIds)
                    {
                        itemsToRemove.Add(id);
                        if (itemsToRemove.Count >= toRemove)
                            break;
                    }

                    foreach (var id in itemsToRemove)
                    {
                        _processedMessageIds.Remove(id);
                    }

                    LogDebug($"清理消息 ID 缓存，移除 {itemsToRemove.Count} 个旧 ID");
                }

                return true; // 新消息，允许处理
            }
            catch (Exception ex)
            {
                LogError($"检查消息去重时发生异常: {ex.Message}");
                return true; // 出错时允许处理
            }
        }

        /// <summary>
        /// 发送系统消息到聊天
        /// </summary>
        /// <param name="message">系统消息内容</param>
        private void SendSystemChatMessage(string message)
        {
            try
            {
                // 创建系统用户
                var systemUser = new UserInfo(0, "系统")
                {
                    DisplayName = "系统"
                };

                // 创建系统消息
                var systemMessage = new ChatMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    Content = message,
                    Sender = systemUser,
                    Type = MessageType.System,
                    Timestamp = DateTime.UtcNow,
                    Metadata = new Dictionary<string, object>()
                };

                // 直接调用 ModUI 显示系统消息
                var modUI = ModUI.Instance;
                if (modUI != null)
                {
                    modUI.AddChatMessage(systemMessage.GetDisplayText());
                    LogDebug($"系统消息已添加到聊天: {message}");
                }
                else
                {
                    LogWarning("ModUI 实例未找到，无法显示系统消息");
                }
            }
            catch (Exception ex)
            {
                LogError($"发送系统消息时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取传输状态描述
        /// </summary>
        /// <returns>状态描述</returns>
        public string GetTransportStatus()
        {
            var status = $"Steam P2P: {(IsSteamAvailable ? "可用" : "不可用")}, ";
            status += $"直连 UDP: {(IsDirectNetworkAvailable ? "可用" : "不可用")}, ";
            status += $"模式: {(IsHost ? "主机" : "客户端")}, ";
            status += $"Steam 客户端数: {_steamClientMap.Count}";
            return status;
        }

        #endregion

        #region 日志方法

        private void LogInfo(string message)
        {
            Debug.Log($"[UnifiedChatTransport] {message}");
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[UnifiedChatTransport] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[UnifiedChatTransport] {message}");
        }

        private void LogDebug(string message)
        {
            Debug.Log($"[UnifiedChatTransport][DEBUG] {message}");
        }

        #endregion
    }
}
