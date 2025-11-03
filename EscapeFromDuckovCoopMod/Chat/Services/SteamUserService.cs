using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.Models;
using Steamworks;

namespace EscapeFromDuckovCoopMod.Chat.Services
{
    /// <summary>
    /// Steam用户服务实现类
    /// </summary>
    public class SteamUserService : ISteamUserService
    {
        /// <summary>
        /// 用户信息更新事件
        /// </summary>
        public event Action<UserInfo> OnUserInfoUpdated;

        /// <summary>
        /// Steam API状态改变事件
        /// </summary>
        public event Action<bool> OnSteamAPIStatusChanged;

        /// <summary>
        /// 检查Steam API是否已初始化
        /// </summary>
        public bool IsSteamAPIInitialized { get; private set; }

        private readonly Dictionary<ulong, UserInfo> userInfoCache = new Dictionary<ulong, UserInfo>();
        private readonly Dictionary<ulong, DateTime> cacheTimestamps = new Dictionary<ulong, DateTime>();
        private readonly TimeSpan cacheExpiration = TimeSpan.FromMinutes(5); // 缓存5分钟

        private bool isInitializing = false;

        /// <summary>
        /// 构造函数
        /// </summary>
        public SteamUserService()
        {
            // 检查Steam API初始化状态
            CheckSteamAPIStatus();
        }

        /// <summary>
        /// 初始化Steam API
        /// </summary>
        /// <returns>是否初始化成功</returns>
        public async Task<bool> InitializeSteamAPI()
        {
            if (isInitializing)
            {
                Debug.LogWarning("Steam API正在初始化中");
                return IsSteamAPIInitialized;
            }

            if (IsSteamAPIInitialized)
            {
                Debug.Log("Steam API已经初始化");
                return true;
            }

            isInitializing = true;

            try
            {
                Debug.Log("开始初始化Steam API");

                // 尝试初始化Steam API
                bool initResult = SteamAPI.Init();
                
                if (initResult)
                {
                    IsSteamAPIInitialized = true;
                    Debug.Log("Steam API初始化成功");
                    OnSteamAPIStatusChanged?.Invoke(true);
                }
                else
                {
                    Debug.LogError("Steam API初始化失败");
                    IsSteamAPIInitialized = false;
                    OnSteamAPIStatusChanged?.Invoke(false);
                }

                return initResult;
            }
            catch (Exception ex)
            {
                Debug.LogError($"初始化Steam API时发生异常: {ex.Message}");
                IsSteamAPIInitialized = false;
                OnSteamAPIStatusChanged?.Invoke(false);
                return false;
            }
            finally
            {
                isInitializing = false;
            }
        }

        /// <summary>
        /// 关闭Steam API
        /// </summary>
        public void ShutdownSteamAPI()
        {
            if (IsSteamAPIInitialized)
            {
                try
                {
                    SteamAPI.Shutdown();
                    IsSteamAPIInitialized = false;
                    OnSteamAPIStatusChanged?.Invoke(false);
                    Debug.Log("Steam API已关闭");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"关闭Steam API时发生异常: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 获取当前用户名
        /// </summary>
        /// <returns>用户名</returns>
        public async Task<string> GetCurrentUserName()
        {
            try
            {
                if (!await EnsureSteamAPIInitialized())
                {
                    return GetFallbackUserName();
                }

                var userName = SteamFriends.GetPersonaName();
                if (string.IsNullOrEmpty(userName))
                {
                    Debug.LogWarning("获取Steam用户名为空，使用降级方案");
                    return GetFallbackUserName();
                }

                return userName;
            }
            catch (Exception ex)
            {
                Debug.LogError($"获取当前用户名时发生异常: {ex.Message}");
                return GetFallbackUserName();
            }
        }

        /// <summary>
        /// 获取当前用户ID
        /// </summary>
        /// <returns>Steam用户ID</returns>
        public async Task<ulong> GetCurrentUserId()
        {
            try
            {
                if (!await EnsureSteamAPIInitialized())
                {
                    return 0;
                }

                return SteamUser.GetSteamID().m_SteamID;
            }
            catch (Exception ex)
            {
                Debug.LogError($"获取当前用户ID时发生异常: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 获取当前用户信息
        /// </summary>
        /// <returns>用户信息</returns>
        public async Task<UserInfo> GetCurrentUserInfo()
        {
            try
            {
                var userId = await GetCurrentUserId();
                if (userId == 0)
                {
                    return CreateFallbackUserInfo();
                }

                return await GetUserInfo(userId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"获取当前用户信息时发生异常: {ex.Message}");
                return CreateFallbackUserInfo();
            }
        }

        /// <summary>
        /// 获取指定用户信息
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <returns>用户信息</returns>
        public async Task<UserInfo> GetUserInfo(ulong steamId)
        {
            try
            {
                // 检查缓存
                if (TryGetCachedUserInfo(steamId, out var cachedInfo))
                {
                    return cachedInfo;
                }

                if (!await EnsureSteamAPIInitialized())
                {
                    return CreateFallbackUserInfo(steamId);
                }

                var cSteamId = new CSteamID(steamId);
                var userName = SteamFriends.GetFriendPersonaName(cSteamId);
                
                if (string.IsNullOrEmpty(userName))
                {
                    // 如果获取不到用户名，可能需要请求用户信息
                    SteamFriends.RequestUserInformation(cSteamId, false);
                    userName = $"Player_{steamId}";
                }

                var userInfo = new UserInfo(steamId, userName)
                {
                    Status = GetUserStatus(steamId)
                };

                // 缓存用户信息
                CacheUserInfo(steamId, userInfo);

                return userInfo;
            }
            catch (Exception ex)
            {
                Debug.LogError($"获取用户信息时发生异常: {ex.Message}");
                return CreateFallbackUserInfo(steamId);
            }
        }

        /// <summary>
        /// 获取指定用户信息
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <returns>用户信息</returns>
        public async Task<UserInfo> GetUserInfo(CSteamID steamId)
        {
            return await GetUserInfo(steamId.m_SteamID);
        }

        /// <summary>
        /// 检查用户是否在线
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <returns>是否在线</returns>
        public bool IsUserOnline(ulong steamId)
        {
            try
            {
                if (!IsSteamAPIInitialized)
                    return false;

                var cSteamId = new CSteamID(steamId);
                var personaState = SteamFriends.GetFriendPersonaState(cSteamId);
                return personaState != EPersonaState.k_EPersonaStateOffline;
            }
            catch (Exception ex)
            {
                Debug.LogError($"检查用户在线状态时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检查用户是否在线
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <returns>是否在线</returns>
        public bool IsUserOnline(CSteamID steamId)
        {
            return IsUserOnline(steamId.m_SteamID);
        }

        /// <summary>
        /// 刷新用户信息
        /// </summary>
        public void RefreshUserInfo()
        {
            try
            {
                if (!IsSteamAPIInitialized)
                    return;

                // 清空缓存
                userInfoCache.Clear();
                cacheTimestamps.Clear();

                Debug.Log("用户信息缓存已刷新");
            }
            catch (Exception ex)
            {
                Debug.LogError($"刷新用户信息时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 刷新指定用户信息
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        public void RefreshUserInfo(ulong steamId)
        {
            try
            {
                // 从缓存中移除指定用户
                userInfoCache.Remove(steamId);
                cacheTimestamps.Remove(steamId);

                if (IsSteamAPIInitialized)
                {
                    // 请求更新用户信息
                    var cSteamId = new CSteamID(steamId);
                    SteamFriends.RequestUserInformation(cSteamId, false);
                }

                Debug.Log($"用户 {steamId} 的信息缓存已刷新");
            }
            catch (Exception ex)
            {
                Debug.LogError($"刷新用户信息时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取用户头像URL
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <param name="size">头像大小</param>
        /// <returns>头像URL</returns>
        public async Task<string> GetUserAvatarUrl(ulong steamId, AvatarSize size = AvatarSize.Medium)
        {
            try
            {
                if (!await EnsureSteamAPIInitialized())
                {
                    return string.Empty;
                }

                var cSteamId = new CSteamID(steamId);
                int avatarHandle = 0;

                switch (size)
                {
                    case AvatarSize.Small:
                        avatarHandle = SteamFriends.GetSmallFriendAvatar(cSteamId);
                        break;
                    case AvatarSize.Medium:
                        avatarHandle = SteamFriends.GetMediumFriendAvatar(cSteamId);
                        break;
                    case AvatarSize.Large:
                        avatarHandle = SteamFriends.GetLargeFriendAvatar(cSteamId);
                        break;
                }

                if (avatarHandle > 0)
                {
                    // 这里可以进一步处理头像数据，转换为URL或纹理
                    // 目前返回一个占位符
                    return $"steam://avatar/{steamId}/{(int)size}";
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                Debug.LogError($"获取用户头像时发生异常: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 获取用户状态
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <returns>用户状态</returns>
        public UserStatus GetUserStatus(ulong steamId)
        {
            try
            {
                if (!IsSteamAPIInitialized)
                    return UserStatus.Offline;

                var cSteamId = new CSteamID(steamId);
                var personaState = SteamFriends.GetFriendPersonaState(cSteamId);

                switch (personaState)
                {
                    case EPersonaState.k_EPersonaStateOnline:
                        return UserStatus.Online;
                    case EPersonaState.k_EPersonaStateAway:
                    case EPersonaState.k_EPersonaStateSnooze:
                        return UserStatus.Away;
                    default:
                        return UserStatus.Offline;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"获取用户状态时发生异常: {ex.Message}");
                return UserStatus.Offline;
            }
        }

        /// <summary>
        /// 检查是否为好友
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <returns>是否为好友</returns>
        public bool IsFriend(ulong steamId)
        {
            try
            {
                if (!IsSteamAPIInitialized)
                    return false;

                var cSteamId = new CSteamID(steamId);
                var relationship = SteamFriends.GetFriendRelationship(cSteamId);
                return relationship == EFriendRelationship.k_EFriendRelationshipFriend;
            }
            catch (Exception ex)
            {
                Debug.LogError($"检查好友关系时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取好友列表
        /// </summary>
        /// <returns>好友用户信息列表</returns>
        public async Task<UserInfo[]> GetFriendsList()
        {
            try
            {
                if (!await EnsureSteamAPIInitialized())
                {
                    return new UserInfo[0];
                }

                var friendsList = new List<UserInfo>();
                int friendCount = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);

                for (int i = 0; i < friendCount; i++)
                {
                    var friendId = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                    var friendInfo = await GetUserInfo(friendId);
                    if (friendInfo != null)
                    {
                        friendsList.Add(friendInfo);
                    }
                }

                return friendsList.ToArray();
            }
            catch (Exception ex)
            {
                Debug.LogError($"获取好友列表时发生异常: {ex.Message}");
                return new UserInfo[0];
            }
        }

        /// <summary>
        /// 确保Steam API已初始化
        /// </summary>
        /// <returns>是否已初始化</returns>
        private async Task<bool> EnsureSteamAPIInitialized()
        {
            if (IsSteamAPIInitialized)
                return true;

            return await InitializeSteamAPI();
        }

        /// <summary>
        /// 检查Steam API状态
        /// </summary>
        private void CheckSteamAPIStatus()
        {
            try
            {
                IsSteamAPIInitialized = SteamAPI.IsSteamRunning();
                Debug.Log($"Steam API状态检查: {(IsSteamAPIInitialized ? "运行中" : "未运行")}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"检查Steam API状态时发生异常: {ex.Message}");
                IsSteamAPIInitialized = false;
            }
        }

        /// <summary>
        /// 尝试从缓存获取用户信息
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <param name="userInfo">用户信息</param>
        /// <returns>是否获取成功</returns>
        private bool TryGetCachedUserInfo(ulong steamId, out UserInfo userInfo)
        {
            userInfo = null;

            if (!userInfoCache.ContainsKey(steamId) || !cacheTimestamps.ContainsKey(steamId))
                return false;

            // 检查缓存是否过期
            if (DateTime.UtcNow - cacheTimestamps[steamId] > cacheExpiration)
            {
                userInfoCache.Remove(steamId);
                cacheTimestamps.Remove(steamId);
                return false;
            }

            userInfo = userInfoCache[steamId];
            return true;
        }

        /// <summary>
        /// 缓存用户信息
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <param name="userInfo">用户信息</param>
        private void CacheUserInfo(ulong steamId, UserInfo userInfo)
        {
            userInfoCache[steamId] = userInfo;
            cacheTimestamps[steamId] = DateTime.UtcNow;
        }

        /// <summary>
        /// 获取降级用户名
        /// </summary>
        /// <returns>降级用户名</returns>
        private string GetFallbackUserName()
        {
            return $"Player_{Environment.UserName}_{DateTime.Now.Ticks % 10000}";
        }

        /// <summary>
        /// 创建降级用户信息
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <returns>降级用户信息</returns>
        private UserInfo CreateFallbackUserInfo(ulong steamId = 0)
        {
            var fallbackName = GetFallbackUserName();
            return new UserInfo(steamId, fallbackName)
            {
                Status = UserStatus.Online
            };
        }
    }
}