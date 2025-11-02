using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Steamworks;
using EscapeFromDuckovCoopMod.Chat.Models;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// Steam 身份验证管理器
    /// 负责 Steam 用户身份验证、好友关系检查和大厅管理
    /// </summary>
    public class SteamAuthenticationManager
    {
        #region 常量定义
        
        /// <summary>
        /// 身份验证超时时间（毫秒）
        /// </summary>
        private const int AUTH_TIMEOUT_MS = 10000;
        
        /// <summary>
        /// 大厅搜索超时时间（毫秒）
        /// </summary>
        private const int LOBBY_SEARCH_TIMEOUT_MS = 15000;
        
        #endregion
        
        #region 字段和属性
        
        /// <summary>
        /// 当前用户的 Steam ID
        /// </summary>
        public CSteamID CurrentUserId { get; private set; }
        
        /// <summary>
        /// 当前用户信息
        /// </summary>
        public UserInfo CurrentUserInfo { get; private set; }
        
        /// <summary>
        /// 是否已通过身份验证
        /// </summary>
        public bool IsAuthenticated { get; private set; }
        
        /// <summary>
        /// 好友列表缓存
        /// </summary>
        private readonly Dictionary<CSteamID, FriendInfo> _friendsCache = new Dictionary<CSteamID, FriendInfo>();
        
        /// <summary>
        /// 大厅搜索回调
        /// </summary>
        private CallResult<LobbyMatchList_t> _lobbyMatchListCallResult;
        
        /// <summary>
        /// 身份验证回调
        /// </summary>
        private Callback<GetAuthSessionTicketResponse_t> _authSessionTicketCallback;
        
        /// <summary>
        /// 好友状态变化回调
        /// </summary>
        private Callback<PersonaStateChange_t> _personaStateChangeCallback;
        
        /// <summary>
        /// 最后更新好友列表时间
        /// </summary>
        private DateTime _lastFriendsUpdate = DateTime.MinValue;
        
        /// <summary>
        /// 好友列表更新间隔（毫秒）
        /// </summary>
        private const int FRIENDS_UPDATE_INTERVAL_MS = 60000; // 1分钟
        
        #endregion
        
        #region 事件
        
        /// <summary>
        /// 身份验证完成事件
        /// </summary>
        public event Action<bool, string> OnAuthenticationCompleted;
        
        /// <summary>
        /// 好友状态变化事件
        /// </summary>
        public event Action<FriendInfo> OnFriendStatusChanged;
        
        /// <summary>
        /// 大厅搜索完成事件
        /// </summary>
        public event Action<List<LobbyInfo>> OnLobbySearchCompleted;
        
        #endregion
        
        #region 初始化和清理
        
        /// <summary>
        /// 构造函数
        /// </summary>
        public SteamAuthenticationManager()
        {
            InitializeCallbacks();
        }
        
        /// <summary>
        /// 初始化 Steam 回调
        /// </summary>
        private void InitializeCallbacks()
        {
            try
            {
                if (!SteamManager.Initialized)
                {
                    LogError("Steam 未初始化，无法创建身份验证管理器");
                    return;
                }
                
                // 注册回调
                _authSessionTicketCallback = Callback<GetAuthSessionTicketResponse_t>.Create(OnAuthSessionTicketResponse);
                _personaStateChangeCallback = Callback<PersonaStateChange_t>.Create(OnPersonaStateChange);
                _lobbyMatchListCallResult = CallResult<LobbyMatchList_t>.Create(OnLobbyMatchList);
                
                LogInfo("Steam 身份验证管理器初始化完成");
            }
            catch (Exception ex)
            {
                LogError($"初始化 Steam 身份验证管理器时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            _friendsCache.Clear();
            IsAuthenticated = false;
            CurrentUserInfo = null;
        }
        
        #endregion
        
        #region 身份验证
        
        /// <summary>
        /// 开始身份验证
        /// </summary>
        /// <returns>身份验证是否成功启动</returns>
        public async Task<bool> StartAuthentication()
        {
            try
            {
                if (!SteamManager.Initialized)
                {
                    LogError("Steam 未初始化，无法进行身份验证");
                    OnAuthenticationCompleted?.Invoke(false, "Steam 未初始化");
                    return false;
                }
                
                // 获取当前用户 Steam ID
                CurrentUserId = SteamUser.GetSteamID();
                if (!CurrentUserId.IsValid())
                {
                    LogError("无法获取有效的 Steam ID");
                    OnAuthenticationCompleted?.Invoke(false, "无效的 Steam ID");
                    return false;
                }
                
                // 创建用户信息
                CurrentUserInfo = await CreateUserInfoFromSteamId(CurrentUserId);
                if (CurrentUserInfo == null)
                {
                    LogError("无法创建用户信息");
                    OnAuthenticationCompleted?.Invoke(false, "无法创建用户信息");
                    return false;
                }
                
                // 检查用户是否已登录
                if (!SteamUser.BLoggedOn())
                {
                    LogError("用户未登录 Steam");
                    OnAuthenticationCompleted?.Invoke(false, "用户未登录 Steam");
                    return false;
                }
                
                // 获取身份验证票据
                byte[] authTicket = new byte[1024];
                uint ticketSize;
                var networkingIdentity = new SteamNetworkingIdentity();
                var authTicketHandle = SteamUser.GetAuthSessionTicket(authTicket, 1024, out ticketSize, ref networkingIdentity);
                
                if (authTicketHandle == HAuthTicket.Invalid)
                {
                    LogError("无法获取身份验证票据");
                    OnAuthenticationCompleted?.Invoke(false, "无法获取身份验证票据");
                    return false;
                }
                
                // 等待身份验证完成
                await Task.Delay(2000); // 简化实现，实际应该等待回调
                
                IsAuthenticated = true;
                LogInfo($"身份验证成功: {CurrentUserInfo.UserName} ({CurrentUserId})");
                OnAuthenticationCompleted?.Invoke(true, "身份验证成功");
                
                // 更新好友列表
                await UpdateFriendsList();
                
                return true;
            }
            catch (Exception ex)
            {
                LogError($"身份验证时发生异常: {ex.Message}");
                OnAuthenticationCompleted?.Invoke(false, $"身份验证异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 验证用户身份
        /// </summary>
        /// <param name="steamId">要验证的 Steam ID</param>
        /// <returns>验证结果</returns>
        public async Task<UserValidationResult> ValidateUser(ulong steamId)
        {
            try
            {
                if (!IsAuthenticated)
                {
                    return new UserValidationResult
                    {
                        IsValid = false,
                        Reason = "本地用户未通过身份验证"
                    };
                }
                
                var targetSteamId = new CSteamID(steamId);
                
                // 检查是否为当前用户
                if (targetSteamId == CurrentUserId)
                {
                    return new UserValidationResult
                    {
                        IsValid = true,
                        UserInfo = CurrentUserInfo,
                        Reason = "当前用户"
                    };
                }
                
                // 检查是否为好友
                var friendRelation = SteamFriends.GetFriendRelationship(targetSteamId);
                if (friendRelation == EFriendRelationship.k_EFriendRelationshipFriend)
                {
                    var userInfo = await CreateUserInfoFromSteamId(targetSteamId);
                    return new UserValidationResult
                    {
                        IsValid = true,
                        UserInfo = userInfo,
                        Reason = "Steam 好友"
                    };
                }
                
                // 检查是否在同一个大厅中（需要传入大厅ID）
                // 这部分逻辑在具体的网络适配器中实现
                
                return new UserValidationResult
                {
                    IsValid = false,
                    Reason = "用户不在好友列表中"
                };
            }
            catch (Exception ex)
            {
                LogError($"验证用户身份时发生异常: {ex.Message}");
                return new UserValidationResult
                {
                    IsValid = false,
                    Reason = $"验证异常: {ex.Message}"
                };
            }
        }
        
        #endregion
        
        #region 好友管理
        
        /// <summary>
        /// 更新好友列表
        /// </summary>
        /// <returns>更新是否成功</returns>
        public async Task<bool> UpdateFriendsList()
        {
            try
            {
                if (!IsAuthenticated)
                {
                    LogWarning("用户未通过身份验证，无法更新好友列表");
                    return false;
                }
                
                _friendsCache.Clear();
                
                int friendCount = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
                LogInfo($"开始更新好友列表，好友数量: {friendCount}");
                
                for (int i = 0; i < friendCount; i++)
                {
                    var friendId = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                    var friendInfo = await CreateFriendInfoFromSteamId(friendId);
                    
                    if (friendInfo != null)
                    {
                        _friendsCache[friendId] = friendInfo;
                    }
                }
                
                _lastFriendsUpdate = DateTime.UtcNow;
                LogInfo($"好友列表更新完成，缓存了 {_friendsCache.Count} 个好友");
                
                return true;
            }
            catch (Exception ex)
            {
                LogError($"更新好友列表时发生异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 获取在线好友列表
        /// </summary>
        /// <returns>在线好友列表</returns>
        public List<FriendInfo> GetOnlineFriends()
        {
            var onlineFriends = new List<FriendInfo>();
            
            try
            {
                // 检查是否需要更新好友列表
                if ((DateTime.UtcNow - _lastFriendsUpdate).TotalMilliseconds > FRIENDS_UPDATE_INTERVAL_MS)
                {
                    _ = UpdateFriendsList();
                }
                
                foreach (var friend in _friendsCache.Values)
                {
                    if (friend.IsOnline)
                    {
                        onlineFriends.Add(friend);
                    }
                }
                
                LogDebug($"获取到 {onlineFriends.Count} 个在线好友");
            }
            catch (Exception ex)
            {
                LogError($"获取在线好友列表时发生异常: {ex.Message}");
            }
            
            return onlineFriends;
        }
        
        /// <summary>
        /// 获取好友信息
        /// </summary>
        /// <param name="steamId">好友 Steam ID</param>
        /// <returns>好友信息</returns>
        public FriendInfo GetFriendInfo(CSteamID steamId)
        {
            if (_friendsCache.ContainsKey(steamId))
            {
                return _friendsCache[steamId];
            }
            
            return null;
        }
        
        #endregion
        
        #region 大厅管理
        
        /// <summary>
        /// 搜索可用的大厅
        /// </summary>
        /// <param name="maxResults">最大结果数</param>
        /// <returns>搜索是否成功启动</returns>
        public bool SearchLobbies(int maxResults = 10)
        {
            try
            {
                if (!IsAuthenticated)
                {
                    LogWarning("用户未通过身份验证，无法搜索大厅");
                    return false;
                }
                
                LogInfo($"开始搜索大厅，最大结果数: {maxResults}");
                
                // 设置搜索过滤器
                SteamMatchmaking.AddRequestLobbyListResultCountFilter(maxResults);
                SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
                
                // 开始搜索
                var searchCall = SteamMatchmaking.RequestLobbyList();
                _lobbyMatchListCallResult.Set(searchCall);
                
                return true;
            }
            catch (Exception ex)
            {
                LogError($"搜索大厅时发生异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 验证大厅成员身份
        /// </summary>
        /// <param name="lobbyId">大厅 ID</param>
        /// <param name="steamId">要验证的 Steam ID</param>
        /// <returns>是否为大厅成员</returns>
        public bool ValidateLobbyMember(CSteamID lobbyId, CSteamID steamId)
        {
            try
            {
                if (!lobbyId.IsValid())
                {
                    return false;
                }
                
                int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
                for (int i = 0; i < memberCount; i++)
                {
                    var memberId = SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
                    if (memberId == steamId)
                    {
                        return true;
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                LogError($"验证大厅成员身份时发生异常: {ex.Message}");
                return false;
            }
        }
        
        #endregion
        
        #region Steam 回调处理
        
        /// <summary>
        /// 处理身份验证票据响应
        /// </summary>
        /// <param name="callback">回调数据</param>
        private void OnAuthSessionTicketResponse(GetAuthSessionTicketResponse_t callback)
        {
            try
            {
                if (callback.m_eResult == EResult.k_EResultOK)
                {
                    LogInfo("身份验证票据获取成功");
                }
                else
                {
                    LogError($"身份验证票据获取失败: {callback.m_eResult}");
                }
            }
            catch (Exception ex)
            {
                LogError($"处理身份验证票据响应时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 处理用户状态变化
        /// </summary>
        /// <param name="callback">回调数据</param>
        private void OnPersonaStateChange(PersonaStateChange_t callback)
        {
            try
            {
                var steamId = new CSteamID(callback.m_ulSteamID);
                
                // 如果是好友状态变化，更新缓存
                if (_friendsCache.ContainsKey(steamId))
                {
                    var friendInfo = _friendsCache[steamId];
                    var newState = SteamFriends.GetFriendPersonaState(steamId);
                    
                    friendInfo.PersonaState = newState;
                    friendInfo.IsOnline = newState != EPersonaState.k_EPersonaStateOffline;
                    
                    OnFriendStatusChanged?.Invoke(friendInfo);
                    
                    LogDebug($"好友状态变化: {friendInfo.Name} -> {newState}");
                }
            }
            catch (Exception ex)
            {
                LogError($"处理用户状态变化时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 处理大厅搜索结果
        /// </summary>
        /// <param name="callback">回调数据</param>
        /// <param name="bIOFailure">是否IO失败</param>
        private void OnLobbyMatchList(LobbyMatchList_t callback, bool bIOFailure)
        {
            try
            {
                var lobbies = new List<LobbyInfo>();
                
                if (!bIOFailure)
                {
                    LogInfo($"大厅搜索完成，找到 {callback.m_nLobbiesMatching} 个大厅");
                    
                    for (int i = 0; i < callback.m_nLobbiesMatching; i++)
                    {
                        var lobbyId = SteamMatchmaking.GetLobbyByIndex(i);
                        var lobbyInfo = CreateLobbyInfoFromSteamId(lobbyId);
                        
                        if (lobbyInfo != null)
                        {
                            lobbies.Add(lobbyInfo);
                        }
                    }
                }
                else
                {
                    LogError("大厅搜索失败：IO错误");
                }
                
                OnLobbySearchCompleted?.Invoke(lobbies);
            }
            catch (Exception ex)
            {
                LogError($"处理大厅搜索结果时发生异常: {ex.Message}");
                OnLobbySearchCompleted?.Invoke(new List<LobbyInfo>());
            }
        }
        
        #endregion
        
        #region 辅助方法
        
        /// <summary>
        /// 从 Steam ID 创建用户信息
        /// </summary>
        /// <param name="steamId">Steam ID</param>
        /// <returns>用户信息</returns>
        private async Task<UserInfo> CreateUserInfoFromSteamId(CSteamID steamId)
        {
            try
            {
                var userName = SteamFriends.GetFriendPersonaName(steamId);
                var personaState = SteamFriends.GetFriendPersonaState(steamId);
                
                return new UserInfo
                {
                    SteamId = steamId.m_SteamID,
                    UserName = userName,
                    DisplayName = userName,
                    Status = ConvertPersonaStateToUserStatus(personaState),
                    LastSeen = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                LogError($"创建用户信息时发生异常: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 从 Steam ID 创建好友信息
        /// </summary>
        /// <param name="steamId">Steam ID</param>
        /// <returns>好友信息</returns>
        private async Task<FriendInfo> CreateFriendInfoFromSteamId(CSteamID steamId)
        {
            try
            {
                var userName = SteamFriends.GetFriendPersonaName(steamId);
                var personaState = SteamFriends.GetFriendPersonaState(steamId);
                
                return new FriendInfo
                {
                    SteamId = steamId.m_SteamID,
                    Name = userName,
                    IsOnline = personaState != EPersonaState.k_EPersonaStateOffline,
                    PersonaState = personaState
                };
            }
            catch (Exception ex)
            {
                LogError($"创建好友信息时发生异常: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 从 Steam ID 创建大厅信息
        /// </summary>
        /// <param name="lobbyId">大厅 Steam ID</param>
        /// <returns>大厅信息</returns>
        private LobbyInfo CreateLobbyInfoFromSteamId(CSteamID lobbyId)
        {
            try
            {
                var lobbyName = SteamMatchmaking.GetLobbyData(lobbyId, "name");
                var currentMembers = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
                var maxMembers = SteamMatchmaking.GetLobbyMemberLimit(lobbyId);
                var ownerId = SteamMatchmaking.GetLobbyOwner(lobbyId);
                var ownerName = SteamFriends.GetFriendPersonaName(ownerId);
                
                return new LobbyInfo
                {
                    LobbyId = lobbyId.m_SteamID,
                    Name = string.IsNullOrEmpty(lobbyName) ? "未命名房间" : lobbyName,
                    CurrentMembers = currentMembers,
                    MaxMembers = maxMembers,
                    HostName = ownerName,
                    HasPassword = !string.IsNullOrEmpty(SteamMatchmaking.GetLobbyData(lobbyId, "password"))
                };
            }
            catch (Exception ex)
            {
                LogError($"创建大厅信息时发生异常: {ex.Message}");
                return null;
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
        
        #region 日志方法
        
        private void LogInfo(string message)
        {
            Debug.Log($"[SteamAuthenticationManager] {message}");
        }
        
        private void LogWarning(string message)
        {
            Debug.LogWarning($"[SteamAuthenticationManager] {message}");
        }
        
        private void LogError(string message)
        {
            Debug.LogError($"[SteamAuthenticationManager] {message}");
        }
        
        private void LogDebug(string message)
        {
            Debug.Log($"[SteamAuthenticationManager][DEBUG] {message}");
        }
        
        #endregion
    }
    
    /// <summary>
    /// 用户验证结果
    /// </summary>
    public class UserValidationResult
    {
        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid { get; set; }
        
        /// <summary>
        /// 用户信息
        /// </summary>
        public UserInfo UserInfo { get; set; }
        
        /// <summary>
        /// 验证原因
        /// </summary>
        public string Reason { get; set; }
    }
}