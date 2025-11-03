using System;
using System.Threading.Tasks;
using EscapeFromDuckovCoopMod.Chat.Models;
using Steamworks;

namespace EscapeFromDuckovCoopMod.Chat.Services
{
    /// <summary>
    /// Steam用户服务接口
    /// </summary>
    public interface ISteamUserService
    {
        /// <summary>
        /// 用户信息更新事件
        /// </summary>
        event Action<UserInfo> OnUserInfoUpdated;

        /// <summary>
        /// Steam API状态改变事件
        /// </summary>
        event Action<bool> OnSteamAPIStatusChanged;

        /// <summary>
        /// 检查Steam API是否已初始化
        /// </summary>
        bool IsSteamAPIInitialized { get; }

        /// <summary>
        /// 获取当前用户名
        /// </summary>
        /// <returns>用户名</returns>
        Task<string> GetCurrentUserName();

        /// <summary>
        /// 获取当前用户ID
        /// </summary>
        /// <returns>Steam用户ID</returns>
        Task<ulong> GetCurrentUserId();

        /// <summary>
        /// 获取当前用户信息
        /// </summary>
        /// <returns>用户信息</returns>
        Task<UserInfo> GetCurrentUserInfo();

        /// <summary>
        /// 获取指定用户信息
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <returns>用户信息</returns>
        Task<UserInfo> GetUserInfo(ulong steamId);

        /// <summary>
        /// 获取指定用户信息
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <returns>用户信息</returns>
        Task<UserInfo> GetUserInfo(CSteamID steamId);

        /// <summary>
        /// 检查用户是否在线
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <returns>是否在线</returns>
        bool IsUserOnline(ulong steamId);

        /// <summary>
        /// 检查用户是否在线
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <returns>是否在线</returns>
        bool IsUserOnline(CSteamID steamId);

        /// <summary>
        /// 刷新用户信息
        /// </summary>
        void RefreshUserInfo();

        /// <summary>
        /// 刷新指定用户信息
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        void RefreshUserInfo(ulong steamId);

        /// <summary>
        /// 初始化Steam API
        /// </summary>
        /// <returns>是否初始化成功</returns>
        Task<bool> InitializeSteamAPI();

        /// <summary>
        /// 关闭Steam API
        /// </summary>
        void ShutdownSteamAPI();

        /// <summary>
        /// 获取用户头像URL
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <param name="size">头像大小</param>
        /// <returns>头像URL</returns>
        Task<string> GetUserAvatarUrl(ulong steamId, AvatarSize size = AvatarSize.Medium);

        /// <summary>
        /// 获取用户状态
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <returns>用户状态</returns>
        UserStatus GetUserStatus(ulong steamId);

        /// <summary>
        /// 检查是否为好友
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <returns>是否为好友</returns>
        bool IsFriend(ulong steamId);

        /// <summary>
        /// 获取好友列表
        /// </summary>
        /// <returns>好友用户信息列表</returns>
        Task<UserInfo[]> GetFriendsList();
    }

    /// <summary>
    /// 头像大小枚举
    /// </summary>
    public enum AvatarSize
    {
        /// <summary>
        /// 小头像 (32x32)
        /// </summary>
        Small = 0,

        /// <summary>
        /// 中等头像 (64x64)
        /// </summary>
        Medium = 1,

        /// <summary>
        /// 大头像 (184x184)
        /// </summary>
        Large = 2
    }
}