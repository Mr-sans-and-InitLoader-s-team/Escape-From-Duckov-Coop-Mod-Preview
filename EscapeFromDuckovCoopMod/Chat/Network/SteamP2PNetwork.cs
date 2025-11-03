using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Steamworks;
using EscapeFromDuckovCoopMod.Chat.Models;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// Steam P2P 网络适配器实现
    /// 提供基于 Steam 平台的 P2P 网络通信功能
    /// </summary>
    public class SteamP2PNetwork : NetworkAdapter, ISteamP2PNetwork
    {
        #region 常量定义
        
        /// <summary>
        /// Steam P2P 通信频道
        /// </summary>
        private const int STEAM_P2P_CHANNEL = 0;
        
        /// <summary>
        /// 最大消息大小（字节）
        /// </summary>
        private const int MAX_MESSAGE_SIZE = 1024 * 1024; // 1MB
        
        /// <summary>
        /// 心跳消息间隔（毫秒）
        /// </summary>
        private const int HEARTBEAT_INTERVAL_MS = 5000;
        
        #endregion
        
        #region 字段和属性
        
        /// <summary>
        /// 当前网络类型
        /// </summary>
        public override NetworkType CurrentNetworkType => NetworkType.SteamP2P;
        
        /// <summary>
        /// 当前 Steam 大厅 ID
        /// </summary>
        public CSteamID CurrentLobbyId { get; private set; }
        
        /// <summary>
        /// 是否为大厅主机
        /// </summary>
        public bool IsLobbyHost { get; private set; }
        
        /// <summary>
        /// 连接的客户端列表
        /// </summary>
        private readonly Dictionary<CSteamID, SteamP2PClient> _connectedClients = new Dictionary<CSteamID, SteamP2PClient>();
        
        /// <summary>
        /// Steam 回调处理器
        /// </summary>
        private Callback<P2PSessionRequest_t> _p2pSessionRequestCallback;
        private Callback<P2PSessionConnectFail_t> _p2pSessionConnectFailCallback;
        private Callback<LobbyCreated_t> _lobbyCreatedCallback;
        private Callback<LobbyEnter_t> _lobbyEnterCallback;
        private Callback<LobbyChatUpdate_t> _lobbyChatUpdateCallback;
        
        /// <summary>
        /// 心跳定时器
        /// </summary>
        private float _heartbeatTimer;
        
        /// <summary>
        /// 是否正在处理消息
        /// </summary>
        private bool _isProcessingMessages;
        
        /// <summary>
        /// 消息处理缓冲区
        /// </summary>
        private readonly byte[] _messageBuffer = new byte[MAX_MESSAGE_SIZE];
        
        /// <summary>
        /// 可靠传输管理器
        /// </summary>
        private SteamReliableTransmission _reliableTransmission;
        
        /// <summary>
        /// Steam 身份验证管理器
        /// </summary>
        private SteamAuthenticationManager _authManager;
        
        #endregion
        
        #region 构造函数和初始化
        
        /// <summary>
        /// 构造函数
        /// </summary>
        public SteamP2PNetwork()
        {
            InitializeSteamCallbacks();
        }
        
        /// <summary>
        /// 初始化 Steam 回调
        /// </summary>
        private void InitializeSteamCallbacks()
        {
            try
            {
                if (!SteamManager.Initialized)
                {
                    LogError("Steam 未初始化，无法创建 Steam P2P 网络适配器");
                    return;
                }
                
                // 注册 Steam 回调
                _p2pSessionRequestCallback = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
                _p2pSessionConnectFailCallback = Callback<P2PSessionConnectFail_t>.Create(OnP2PSessionConnectFail);
                _lobbyCreatedCallback = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
                _lobbyEnterCallback = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
                _lobbyChatUpdateCallback = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
                
                // 初始化可靠传输管理器
                _reliableTransmission = new SteamReliableTransmission();
                _reliableTransmission.SendMessageCallback = SendP2PMessageDirect;
                _reliableTransmission.OnMessageReceived += OnReliableMessageReceived;
                _reliableTransmission.OnMessageSendFailed += OnReliableMessageSendFailed;
                
                // 初始化身份验证管理器
                _authManager = new SteamAuthenticationManager();
                _authManager.OnAuthenticationCompleted += OnAuthenticationCompleted;
                _authManager.OnFriendStatusChanged += OnFriendStatusChanged;
                _authManager.OnLobbySearchCompleted += OnLobbySearchCompleted;
                
                IsInitialized = true;
                LogInfo("Steam P2P 网络适配器初始化完成");
            }
            catch (Exception ex)
            {
                LogError($"初始化 Steam P2P 网络适配器时发生异常: {ex.Message}");
            }
        }
        
        #endregion
        
        #region 网络适配器实现
        
        /// <summary>
        /// 启动主机服务的具体实现
        /// </summary>
        /// <param name="config">网络配置</param>
        /// <returns>启动是否成功</returns>
        protected override async Task<bool> StartHostInternal(NetworkConfig config)
        {
            try
            {
                if (!SteamManager.Initialized)
                {
                    LogError("Steam 未初始化，无法启动主机服务");
                    return false;
                }
                
                // 如果指定了大厅 ID，尝试加入现有大厅
                if (config.SteamLobbyId != 0)
                {
                    return await JoinSteamLobby(config.SteamLobbyId);
                }
                
                // 确保用户已通过身份验证
                if (!_authManager.IsAuthenticated)
                {
                    LogInfo("开始 Steam 身份验证...");
                    if (!await _authManager.StartAuthentication())
                    {
                        LogError("Steam 身份验证失败");
                        return false;
                    }
                }
                
                // 否则创建新的大厅
                var lobbySettings = new LobbySettings
                {
                    MaxMembers = 4, // 默认最大4人
                    LobbyType = ELobbyType.k_ELobbyTypeFriendsOnly,
                    IsJoinable = true
                };
                
                return await CreateSteamLobby(lobbySettings);
            }
            catch (Exception ex)
            {
                LogError($"启动 Steam P2P 主机服务时发生异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 连接到主机的具体实现
        /// </summary>
        /// <param name="endpoint">主机端点（Steam 大厅 ID）</param>
        /// <returns>连接是否成功</returns>
        protected override async Task<bool> ConnectToHostInternal(string endpoint)
        {
            try
            {
                if (!SteamManager.Initialized)
                {
                    LogError("Steam 未初始化，无法连接主机");
                    return false;
                }
                
                // 确保用户已通过身份验证
                if (!_authManager.IsAuthenticated)
                {
                    LogInfo("开始 Steam 身份验证...");
                    if (!await _authManager.StartAuthentication())
                    {
                        LogError("Steam 身份验证失败");
                        return false;
                    }
                }
                
                // 解析大厅 ID
                if (!ulong.TryParse(endpoint, out ulong lobbyId))
                {
                    LogError($"无效的 Steam 大厅 ID: {endpoint}");
                    return false;
                }
                
                return await JoinSteamLobby(lobbyId);
            }
            catch (Exception ex)
            {
                LogError($"连接 Steam P2P 主机时发生异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 断开连接的具体实现
        /// </summary>
        protected override void DisconnectInternal()
        {
            try
            {
                // 关闭所有 P2P 会话
                foreach (var client in _connectedClients.Values)
                {
                    SteamNetworking.CloseP2PSessionWithUser(client.SteamId);
                }
                _connectedClients.Clear();
                
                // 离开大厅
                if (CurrentLobbyId.IsValid())
                {
                    SteamMatchmaking.LeaveLobby(CurrentLobbyId);
                    CurrentLobbyId = CSteamID.Nil;
                }
                
                IsLobbyHost = false;
                _isProcessingMessages = false;
                
                LogInfo("Steam P2P 连接已断开");
            }
            catch (Exception ex)
            {
                LogError($"断开 Steam P2P 连接时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 发送消息的具体实现
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <param name="targetId">目标 Steam ID</param>
        /// <returns>发送是否成功</returns>
        protected override async Task<bool> SendMessageInternal(byte[] data, string targetId)
        {
            try
            {
                if (data.Length > MAX_MESSAGE_SIZE)
                {
                    LogError($"消息大小超过限制: {data.Length} > {MAX_MESSAGE_SIZE}");
                    return false;
                }
                
                // 如果没有指定目标，广播给所有连接的客户端
                if (string.IsNullOrEmpty(targetId))
                {
                    return await BroadcastToAllClients(data);
                }
                
                // 发送给指定目标
                if (ulong.TryParse(targetId, out ulong steamId))
                {
                    var targetSteamId = new CSteamID(steamId);
                    return SendP2PMessageDirect(targetSteamId, data);
                }
                
                LogError($"无效的目标 Steam ID: {targetId}");
                return false;
            }
            catch (Exception ex)
            {
                LogError($"发送 Steam P2P 消息时发生异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 发送可靠网络消息
        /// </summary>
        /// <param name="message">网络消息</param>
        /// <param name="targetId">目标 Steam ID</param>
        /// <returns>发送是否成功</returns>
        public async Task<bool> SendReliableNetworkMessage(SteamNetworkMessage message, CSteamID targetId)
        {
            try
            {
                if (_reliableTransmission == null)
                {
                    LogError("可靠传输管理器未初始化");
                    return false;
                }
                
                return await _reliableTransmission.SendReliableMessage(message, targetId);
            }
            catch (Exception ex)
            {
                LogError($"发送可靠网络消息时发生异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 广播可靠网络消息
        /// </summary>
        /// <param name="message">网络消息</param>
        /// <returns>广播是否成功</returns>
        public async Task<bool> BroadcastReliableNetworkMessage(SteamNetworkMessage message)
        {
            bool allSuccess = true;
            
            foreach (var client in _connectedClients.Values)
            {
                if (!await SendReliableNetworkMessage(message, client.SteamId))
                {
                    allSuccess = false;
                    LogWarning($"向客户端发送可靠消息失败: {client.SteamId}");
                }
            }
            
            return allSuccess;
        }
        
        #endregion
        
        #region Steam P2P 特定功能
        
        /// <summary>
        /// 创建 Steam 大厅
        /// </summary>
        /// <param name="settings">大厅设置</param>
        /// <returns>创建是否成功</returns>
        public async Task<bool> CreateSteamLobby(LobbySettings settings)
        {
            try
            {
                if (!SteamManager.Initialized)
                {
                    LogError("Steam 未初始化，无法创建大厅");
                    return false;
                }
                
                LogInfo($"正在创建 Steam 大厅，最大成员数: {settings.MaxMembers}");
                
                // 创建大厅
                var createLobbyCall = SteamMatchmaking.CreateLobby(settings.LobbyType, settings.MaxMembers);
                
                // 等待大厅创建完成（简化实现，实际应该使用回调）
                await Task.Delay(2000);
                
                return CurrentLobbyId.IsValid();
            }
            catch (Exception ex)
            {
                LogError($"创建 Steam 大厅时发生异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 加入 Steam 大厅
        /// </summary>
        /// <param name="lobbyId">大厅 ID</param>
        /// <returns>加入是否成功</returns>
        public async Task<bool> JoinSteamLobby(ulong lobbyId)
        {
            try
            {
                if (!SteamManager.Initialized)
                {
                    LogError("Steam 未初始化，无法加入大厅");
                    return false;
                }
                
                var steamLobbyId = new CSteamID(lobbyId);
                LogInfo($"正在加入 Steam 大厅: {lobbyId}");
                
                // 加入大厅
                SteamMatchmaking.JoinLobby(steamLobbyId);
                
                // 等待加入完成（简化实现，实际应该使用回调）
                await Task.Delay(2000);
                
                return CurrentLobbyId == steamLobbyId;
            }
            catch (Exception ex)
            {
                LogError($"加入 Steam 大厅时发生异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 获取可用的大厅列表
        /// </summary>
        /// <returns>大厅信息列表</returns>
        public async Task<List<LobbyInfo>> GetAvailableLobbies()
        {
            try
            {
                if (!SteamManager.Initialized)
                {
                    LogWarning("Steam 未初始化，无法获取大厅列表");
                    return new List<LobbyInfo>();
                }
                
                if (!_authManager.IsAuthenticated)
                {
                    LogWarning("用户未通过身份验证，无法获取大厅列表");
                    return new List<LobbyInfo>();
                }
                
                LogInfo("开始搜索可用大厅...");
                
                // 使用身份验证管理器搜索大厅
                var searchStarted = _authManager.SearchLobbies(10);
                if (!searchStarted)
                {
                    LogError("无法启动大厅搜索");
                    return new List<LobbyInfo>();
                }
                
                // 等待搜索完成（简化实现，实际应该使用事件）
                await Task.Delay(3000);
                
                // 搜索结果将通过事件返回
                return new List<LobbyInfo>();
            }
            catch (Exception ex)
            {
                LogError($"获取可用大厅列表时发生异常: {ex.Message}");
                return new List<LobbyInfo>();
            }
        }
        
        /// <summary>
        /// 邀请好友
        /// </summary>
        /// <param name="friendId">好友 Steam ID</param>
        public void InviteFriend(ulong friendId)
        {
            try
            {
                if (!SteamManager.Initialized || !CurrentLobbyId.IsValid())
                {
                    LogWarning("Steam 未初始化或未在大厅中，无法邀请好友");
                    return;
                }
                
                var friendSteamId = new CSteamID(friendId);
                SteamMatchmaking.InviteUserToLobby(CurrentLobbyId, friendSteamId);
                
                LogInfo($"已邀请好友加入大厅: {friendId}");
            }
            catch (Exception ex)
            {
                LogError($"邀请好友时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 获取在线好友列表
        /// </summary>
        /// <returns>好友信息列表</returns>
        public List<FriendInfo> GetOnlineFriends()
        {
            try
            {
                if (!SteamManager.Initialized)
                {
                    LogWarning("Steam 未初始化，无法获取好友列表");
                    return new List<FriendInfo>();
                }
                
                if (!_authManager.IsAuthenticated)
                {
                    LogWarning("用户未通过身份验证，无法获取好友列表");
                    return new List<FriendInfo>();
                }
                
                return _authManager.GetOnlineFriends();
            }
            catch (Exception ex)
            {
                LogError($"获取在线好友列表时发生异常: {ex.Message}");
                return new List<FriendInfo>();
            }
        }
        
        /// <summary>
        /// 验证 Steam 用户
        /// </summary>
        /// <param name="steamId">Steam ID</param>
        /// <returns>是否为有效用户</returns>
        public bool ValidateSteamUser(ulong steamId)
        {
            try
            {
                if (!SteamManager.Initialized || !_authManager.IsAuthenticated)
                {
                    return false;
                }
                
                var userSteamId = new CSteamID(steamId);
                
                // 检查用户是否在当前大厅中
                if (CurrentLobbyId.IsValid())
                {
                    return _authManager.ValidateLobbyMember(CurrentLobbyId, userSteamId);
                }
                
                // 如果没有大厅，检查是否为好友
                var validationTask = _authManager.ValidateUser(steamId);
                validationTask.Wait(1000); // 等待1秒
                
                return validationTask.IsCompleted && validationTask.Result.IsValid;
            }
            catch (Exception ex)
            {
                LogError($"验证 Steam 用户时发生异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 获取 Steam 用户信息
        /// </summary>
        /// <param name="steamId">Steam ID</param>
        /// <returns>用户信息</returns>
        public UserInfo GetSteamUserInfo(ulong steamId)
        {
            try
            {
                if (!SteamManager.Initialized || !_authManager.IsAuthenticated)
                {
                    return null;
                }
                
                // 如果是当前用户，直接返回
                if (steamId == _authManager.CurrentUserId.m_SteamID)
                {
                    return _authManager.CurrentUserInfo;
                }
                
                // 尝试从好友缓存获取
                var userSteamId = new CSteamID(steamId);
                var friendInfo = _authManager.GetFriendInfo(userSteamId);
                if (friendInfo != null)
                {
                    return new UserInfo
                    {
                        SteamId = steamId,
                        UserName = friendInfo.Name,
                        DisplayName = friendInfo.Name,
                        Status = friendInfo.IsOnline ? UserStatus.Online : UserStatus.Offline
                    };
                }
                
                // 如果不是好友，创建基本用户信息
                var userName = SteamFriends.GetFriendPersonaName(userSteamId);
                var personaState = SteamFriends.GetFriendPersonaState(userSteamId);
                
                return new UserInfo
                {
                    SteamId = steamId,
                    UserName = userName,
                    DisplayName = userName,
                    Status = ConvertPersonaStateToUserStatus(personaState)
                };
            }
            catch (Exception ex)
            {
                LogError($"获取 Steam 用户信息时发生异常: {ex.Message}");
                return null;
            }
        }
        
        #endregion
        
        #region 消息处理
        
        /// <summary>
        /// 更新消息处理（需要在主线程中调用）
        /// </summary>
        public void Update()
        {
            if (!IsConnected || _isProcessingMessages)
            {
                return;
            }
            
            try
            {
                _isProcessingMessages = true;
                
                // 处理接收到的消息
                ProcessIncomingMessages();
                
                // 处理心跳
                ProcessHeartbeat();
                
                // 更新可靠传输
                _reliableTransmission?.Update();
            }
            finally
            {
                _isProcessingMessages = false;
            }
        }
        
        /// <summary>
        /// 处理接收到的消息
        /// </summary>
        private void ProcessIncomingMessages()
        {
            try
            {
                uint messageSize;
                CSteamID senderId;
                
                // 检查是否有待处理的消息
                while (SteamNetworking.IsP2PPacketAvailable(out messageSize, STEAM_P2P_CHANNEL))
                {
                    if (messageSize > MAX_MESSAGE_SIZE)
                    {
                        LogWarning($"收到超大消息，跳过处理: {messageSize} > {MAX_MESSAGE_SIZE}");
                        continue;
                    }
                    
                    // 读取消息
                    if (SteamNetworking.ReadP2PPacket(_messageBuffer, messageSize, out messageSize, out senderId, STEAM_P2P_CHANNEL))
                    {
                        // 创建消息数据副本
                        var messageData = new byte[messageSize];
                        Array.Copy(_messageBuffer, messageData, messageSize);
                        
                        // 通过可靠传输管理器处理消息
                        _reliableTransmission?.ProcessReceivedMessage(messageData, senderId);
                        
                        LogDebug($"收到 P2P 消息: 发送者={senderId}, 大小={messageSize}字节");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"处理接收消息时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 处理心跳
        /// </summary>
        private void ProcessHeartbeat()
        {
            _heartbeatTimer += Time.deltaTime * 1000; // 转换为毫秒
            
            if (_heartbeatTimer >= HEARTBEAT_INTERVAL_MS)
            {
                _heartbeatTimer = 0;
                SendHeartbeat();
            }
        }
        
        /// <summary>
        /// 发送心跳消息
        /// </summary>
        private void SendHeartbeat()
        {
            try
            {
                var currentUserId = SteamUser.GetSteamID().m_SteamID.ToString();
                var heartbeatMessage = SteamNetworkMessage.CreateHeartbeatMessage(currentUserId);
                
                foreach (var client in _connectedClients.Values)
                {
                    _ = SendReliableNetworkMessage(heartbeatMessage, client.SteamId);
                }
            }
            catch (Exception ex)
            {
                LogError($"发送心跳消息时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 广播消息给所有客户端
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <returns>广播是否成功</returns>
        private async Task<bool> BroadcastToAllClients(byte[] data)
        {
            bool allSuccess = true;
            
            foreach (var client in _connectedClients.Values)
            {
                if (!SendP2PMessageDirect(client.SteamId, data))
                {
                    allSuccess = false;
                    LogWarning($"向客户端发送消息失败: {client.SteamId}");
                }
            }
            
            return allSuccess;
        }
        
        /// <summary>
        /// 直接发送 P2P 消息（不经过可靠传输）
        /// </summary>
        /// <param name="targetId">目标 Steam ID</param>
        /// <param name="data">消息数据</param>
        /// <returns>发送是否成功</returns>
        private bool SendP2PMessageDirect(CSteamID targetId, byte[] data)
        {
            try
            {
                return SteamNetworking.SendP2PPacket(targetId, data, (uint)data.Length, EP2PSend.k_EP2PSendReliable, STEAM_P2P_CHANNEL);
            }
            catch (Exception ex)
            {
                LogError($"发送 P2P 消息时发生异常: {ex.Message}");
                return false;
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
                LogInfo($"收到 P2P 会话请求: {callback.m_steamIDRemote}");
                
                // 验证用户是否在大厅中
                if (ValidateSteamUser(callback.m_steamIDRemote.m_SteamID))
                {
                    // 接受会话请求
                    SteamNetworking.AcceptP2PSessionWithUser(callback.m_steamIDRemote);
                    
                    // 添加到连接客户端列表
                    var client = new SteamP2PClient
                    {
                        SteamId = callback.m_steamIDRemote,
                        ConnectedTime = DateTime.UtcNow
                    };
                    
                    _connectedClients[callback.m_steamIDRemote] = client;
                    
                    // 触发客户端连接事件
                    TriggerClientConnected(callback.m_steamIDRemote.m_SteamID.ToString());
                }
                else
                {
                    LogWarning($"拒绝未授权的 P2P 会话请求: {callback.m_steamIDRemote}");
                }
            }
            catch (Exception ex)
            {
                LogError($"处理 P2P 会话请求时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 处理 P2P 会话连接失败
        /// </summary>
        /// <param name="callback">回调数据</param>
        private void OnP2PSessionConnectFail(P2PSessionConnectFail_t callback)
        {
            try
            {
                LogError($"P2P 会话连接失败: {callback.m_steamIDRemote}, 错误: {callback.m_eP2PSessionError}");
                
                // 从连接列表中移除
                if (_connectedClients.ContainsKey(callback.m_steamIDRemote))
                {
                    _connectedClients.Remove(callback.m_steamIDRemote);
                    TriggerClientDisconnected(callback.m_steamIDRemote.m_SteamID.ToString());
                }
                
                // 触发网络错误事件
                var error = new NetworkError(
                    NetworkErrorType.ConnectionFailed,
                    $"P2P 连接失败: {callback.m_eP2PSessionError}",
                    callback.m_steamIDRemote.ToString()
                );
                TriggerNetworkError(error);
            }
            catch (Exception ex)
            {
                LogError($"处理 P2P 会话连接失败时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 处理大厅创建完成
        /// </summary>
        /// <param name="callback">回调数据</param>
        private void OnLobbyCreated(LobbyCreated_t callback)
        {
            try
            {
                if (callback.m_eResult == EResult.k_EResultOK)
                {
                    CurrentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
                    IsLobbyHost = true;
                    
                    LogInfo($"Steam 大厅创建成功: {CurrentLobbyId}");
                }
                else
                {
                    LogError($"Steam 大厅创建失败: {callback.m_eResult}");
                    
                    var error = new NetworkError(
                        NetworkErrorType.ServiceStartFailed,
                        $"大厅创建失败: {callback.m_eResult}"
                    );
                    TriggerNetworkError(error);
                }
            }
            catch (Exception ex)
            {
                LogError($"处理大厅创建回调时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 处理大厅加入完成
        /// </summary>
        /// <param name="callback">回调数据</param>
        private void OnLobbyEnter(LobbyEnter_t callback)
        {
            try
            {
                if (callback.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
                {
                    CurrentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
                    IsLobbyHost = false;
                    
                    LogInfo($"成功加入 Steam 大厅: {CurrentLobbyId}");
                    
                    // 连接到大厅主机
                    ConnectToLobbyHost();
                }
                else
                {
                    LogError($"加入 Steam 大厅失败: {callback.m_EChatRoomEnterResponse}");
                    
                    var error = new NetworkError(
                        NetworkErrorType.ConnectionFailed,
                        $"加入大厅失败: {callback.m_EChatRoomEnterResponse}"
                    );
                    TriggerNetworkError(error);
                }
            }
            catch (Exception ex)
            {
                LogError($"处理大厅加入回调时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 处理大厅聊天更新
        /// </summary>
        /// <param name="callback">回调数据</param>
        private void OnLobbyChatUpdate(LobbyChatUpdate_t callback)
        {
            try
            {
                var userSteamId = new CSteamID(callback.m_ulSteamIDUserChanged);
                var userName = SteamFriends.GetFriendPersonaName(userSteamId);
                
                if ((callback.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0)
                {
                    LogInfo($"用户加入大厅: {userName} ({userSteamId})");
                }
                else if ((callback.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeLeft) != 0)
                {
                    LogInfo($"用户离开大厅: {userName} ({userSteamId})");
                    
                    // 从连接列表中移除
                    if (_connectedClients.ContainsKey(userSteamId))
                    {
                        _connectedClients.Remove(userSteamId);
                        TriggerClientDisconnected(userSteamId.m_SteamID.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"处理大厅聊天更新时发生异常: {ex.Message}");
            }
        }
        
        #endregion
        
        #region 辅助方法
        
        /// <summary>
        /// 连接到大厅主机
        /// </summary>
        private void ConnectToLobbyHost()
        {
            try
            {
                if (!CurrentLobbyId.IsValid())
                {
                    return;
                }
                
                var hostId = SteamMatchmaking.GetLobbyOwner(CurrentLobbyId);
                if (hostId.IsValid() && hostId != SteamUser.GetSteamID())
                {
                    LogInfo($"正在连接到大厅主机: {hostId}");
                    
                    // 发送连接请求
                    var currentUserId = SteamUser.GetSteamID().m_SteamID.ToString();
                    var connectMessage = new SteamNetworkMessage
                    {
                        Type = SteamMessageType.ConnectRequest,
                        SenderId = currentUserId,
                        Payload = "CONNECT_REQUEST",
                        Flags = SteamMessageFlags.RequireAck
                    };
                    
                    _ = SendReliableNetworkMessage(connectMessage, hostId);
                }
            }
            catch (Exception ex)
            {
                LogError($"连接到大厅主机时发生异常: {ex.Message}");
            }
        }

        
        /// <summary>
        /// 转换 Steam 状态到用户状态
        /// </summary>
        /// <param name="personaState">Steam 状态</param>
        /// <returns>用户状态</returns>
        private UserStatus ConvertPersonaStateToUserStatus(EPersonaState personaState)
        {
            switch (personaState)
            {
                case EPersonaState.k_EPersonaStateOnline:
                case EPersonaState.k_EPersonaStateBusy:
                case EPersonaState.k_EPersonaStateLookingToPlay:
                case EPersonaState.k_EPersonaStateLookingToTrade:
                    return UserStatus.Online;
                case EPersonaState.k_EPersonaStateAway:
                case EPersonaState.k_EPersonaStateSnooze:
                    return UserStatus.Away;
                case EPersonaState.k_EPersonaStateOffline:
                default:
                    return UserStatus.Offline;
            }
        }
        
        #endregion
        
        #region 可靠传输事件处理
        
        /// <summary>
        /// 处理可靠消息接收
        /// </summary>
        /// <param name="message">网络消息</param>
        /// <param name="senderId">发送者Steam ID</param>
        private void OnReliableMessageReceived(SteamNetworkMessage message, CSteamID senderId)
        {
            try
            {
                // 根据消息类型进行处理
                switch (message.Type)
                {
                    case SteamMessageType.ChatMessage:
                        if (message.Payload is Models.ChatMessage chatMessage)
                        {
                            // 将聊天消息转换为字节数组并触发接收事件
                            var chatData = System.Text.Encoding.UTF8.GetBytes(chatMessage.ToJson());
                            TriggerMessageReceived(chatData, senderId.m_SteamID.ToString());
                        }
                        break;
                        
                    case SteamMessageType.UserJoined:
                        if (message.Payload is Models.UserInfo userInfo)
                        {
                            LogInfo($"用户加入: {userInfo.UserName} ({userInfo.SteamId})");
                            TriggerClientConnected(userInfo.SteamId.ToString());
                        }
                        break;
                        
                    case SteamMessageType.UserLeft:
                        if (message.Payload is Models.UserInfo leftUserInfo)
                        {
                            LogInfo($"用户离开: {leftUserInfo.UserName} ({leftUserInfo.SteamId})");
                            TriggerClientDisconnected(leftUserInfo.SteamId.ToString());
                        }
                        break;
                        
                    case SteamMessageType.Heartbeat:
                        // 更新客户端活动时间
                        if (_connectedClients.ContainsKey(senderId))
                        {
                            _connectedClients[senderId].LastActivity = DateTime.UtcNow;
                        }
                        break;
                        
                    case SteamMessageType.ConnectRequest:
                        HandleConnectRequest(message, senderId);
                        break;
                        
                    case SteamMessageType.ConnectResponse:
                        HandleConnectResponse(message, senderId);
                        break;
                        
                    default:
                        LogDebug($"收到未处理的消息类型: {message.Type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                LogError($"处理可靠消息时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 处理可靠消息发送失败
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <param name="targetId">目标Steam ID</param>
        /// <param name="reason">失败原因</param>
        private void OnReliableMessageSendFailed(string messageId, CSteamID targetId, string reason)
        {
            LogError($"可靠消息发送失败: ID={messageId}, 目标={targetId}, 原因={reason}");
            
            // 触发网络错误事件
            var error = new NetworkError(
                NetworkErrorType.MessageSendFailed,
                $"消息发送失败: {reason}",
                $"MessageId: {messageId}, Target: {targetId}"
            );
            TriggerNetworkError(error);
        }
        
        /// <summary>
        /// 处理连接请求
        /// </summary>
        /// <param name="message">连接请求消息</param>
        /// <param name="senderId">发送者Steam ID</param>
        private void HandleConnectRequest(SteamNetworkMessage message, CSteamID senderId)
        {
            try
            {
                LogInfo($"收到连接请求: {senderId}");
                
                // 验证用户是否在大厅中
                if (ValidateSteamUser(senderId.m_SteamID))
                {
                    // 发送连接响应
                    var currentUserId = SteamUser.GetSteamID().m_SteamID.ToString();
                    var responseMessage = new SteamNetworkMessage
                    {
                        Type = SteamMessageType.ConnectResponse,
                        SenderId = currentUserId,
                        Payload = "ACCEPTED",
                        Flags = SteamMessageFlags.RequireAck
                    };
                    
                    _ = SendReliableNetworkMessage(responseMessage, senderId);
                    
                    // 添加到连接客户端列表
                    if (!_connectedClients.ContainsKey(senderId))
                    {
                        var client = new SteamP2PClient
                        {
                            SteamId = senderId,
                            ConnectedTime = DateTime.UtcNow
                        };
                        
                        _connectedClients[senderId] = client;
                        TriggerClientConnected(senderId.m_SteamID.ToString());
                    }
                }
                else
                {
                    LogWarning($"拒绝未授权的连接请求: {senderId}");
                }
            }
            catch (Exception ex)
            {
                LogError($"处理连接请求时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 处理连接响应
        /// </summary>
        /// <param name="message">连接响应消息</param>
        /// <param name="senderId">发送者Steam ID</param>
        private void HandleConnectResponse(SteamNetworkMessage message, CSteamID senderId)
        {
            try
            {
                LogInfo($"收到连接响应: {senderId}, 响应: {message.Payload}");
                
                if (message.Payload?.ToString() == "ACCEPTED")
                {
                    // 连接成功，添加到客户端列表
                    if (!_connectedClients.ContainsKey(senderId))
                    {
                        var client = new SteamP2PClient
                        {
                            SteamId = senderId,
                            ConnectedTime = DateTime.UtcNow
                        };
                        
                        _connectedClients[senderId] = client;
                        TriggerClientConnected(senderId.m_SteamID.ToString());
                    }
                }
                else
                {
                    LogWarning($"连接被拒绝: {senderId}");
                    
                    var error = new NetworkError(
                        NetworkErrorType.ConnectionFailed,
                        "连接被主机拒绝",
                        senderId.ToString()
                    );
                    TriggerNetworkError(error);
                }
            }
            catch (Exception ex)
            {
                LogError($"处理连接响应时发生异常: {ex.Message}");
            }
        }
        
        #endregion
        
        #region 身份验证事件处理
        
        /// <summary>
        /// 处理身份验证完成
        /// </summary>
        /// <param name="success">是否成功</param>
        /// <param name="message">消息</param>
        private void OnAuthenticationCompleted(bool success, string message)
        {
            if (success)
            {
                LogInfo($"Steam 身份验证成功: {message}");
            }
            else
            {
                LogError($"Steam 身份验证失败: {message}");
                
                var error = new NetworkError(
                    NetworkErrorType.NetworkUnavailable,
                    "Steam 身份验证失败",
                    message
                );
                TriggerNetworkError(error);
            }
        }
        
        /// <summary>
        /// 处理好友状态变化
        /// </summary>
        /// <param name="friendInfo">好友信息</param>
        private void OnFriendStatusChanged(FriendInfo friendInfo)
        {
            LogDebug($"好友状态变化: {friendInfo.Name} -> {friendInfo.PersonaState}");
        }
        
        /// <summary>
        /// 处理大厅搜索完成
        /// </summary>
        /// <param name="lobbies">大厅列表</param>
        private void OnLobbySearchCompleted(List<LobbyInfo> lobbies)
        {
            LogInfo($"大厅搜索完成，找到 {lobbies.Count} 个大厅");
            
            // 可以在这里触发自定义事件通知上层
            // OnAvailableLobbiesUpdated?.Invoke(lobbies);
        }
        
        #endregion
        
        #region 析构和清理
        
        /// <summary>
        /// 析构函数
        /// </summary>
        ~SteamP2PNetwork()
        {
            DisconnectInternal();
            _authManager?.Dispose();
            _reliableTransmission?.Clear();
        }
        
        #endregion
    }
    
    /// <summary>
    /// Steam P2P 客户端信息
    /// </summary>
    public class SteamP2PClient
    {
        /// <summary>
        /// Steam ID
        /// </summary>
        public CSteamID SteamId { get; set; }
        
        /// <summary>
        /// 连接时间
        /// </summary>
        public DateTime ConnectedTime { get; set; }
        
        /// <summary>
        /// 最后活动时间
        /// </summary>
        public DateTime LastActivity { get; set; }
        
        /// <summary>
        /// 是否活跃
        /// </summary>
        public bool IsActive => (DateTime.UtcNow - LastActivity).TotalSeconds < 30;
        
        public SteamP2PClient()
        {
            LastActivity = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// 大厅设置
    /// </summary>
    public class LobbySettings
    {
        /// <summary>
        /// 最大成员数
        /// </summary>
        public int MaxMembers { get; set; } = 4;
        
        /// <summary>
        /// 大厅类型
        /// </summary>
        public ELobbyType LobbyType { get; set; } = ELobbyType.k_ELobbyTypeFriendsOnly;
        
        /// <summary>
        /// 是否可加入
        /// </summary>
        public bool IsJoinable { get; set; } = true;
        
        /// <summary>
        /// 大厅名称
        /// </summary>
        public string LobbyName { get; set; } = "游戏房间";
        
        /// <summary>
        /// 大厅描述
        /// </summary>
        public string Description { get; set; } = "";
    }
    
    /// <summary>
    /// 大厅信息
    /// </summary>
    public class LobbyInfo
    {
        /// <summary>
        /// 大厅 ID
        /// </summary>
        public ulong LobbyId { get; set; }
        
        /// <summary>
        /// 大厅名称
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// 当前成员数
        /// </summary>
        public int CurrentMembers { get; set; }
        
        /// <summary>
        /// 最大成员数
        /// </summary>
        public int MaxMembers { get; set; }
        
        /// <summary>
        /// 主机名称
        /// </summary>
        public string HostName { get; set; }
        
        /// <summary>
        /// 是否有密码
        /// </summary>
        public bool HasPassword { get; set; }
    }
    
    /// <summary>
    /// 好友信息
    /// </summary>
    public class FriendInfo
    {
        /// <summary>
        /// Steam ID
        /// </summary>
        public ulong SteamId { get; set; }
        
        /// <summary>
        /// 好友名称
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// 是否在线
        /// </summary>
        public bool IsOnline { get; set; }
        
        /// <summary>
        /// Steam 状态
        /// </summary>
        public EPersonaState PersonaState { get; set; }
    }
}