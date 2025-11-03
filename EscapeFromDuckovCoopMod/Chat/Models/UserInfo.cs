using System;
using Newtonsoft.Json;
using Steamworks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Chat.Models
{
    /// <summary>
    /// 用户信息数据模型
    /// </summary>
    [Serializable]
    public class UserInfo
    {
        /// <summary>
        /// Steam用户ID
        /// </summary>
        public ulong SteamId { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 最后在线时间
        /// </summary>
        public DateTime LastSeen { get; set; }

        /// <summary>
        /// 用户状态
        /// </summary>
        public UserStatus Status { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public UserInfo()
        {
            LastSeen = DateTime.UtcNow;
            Status = UserStatus.Online;
        }

        /// <summary>
        /// 带参数的构造函数
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <param name="userName">用户名</param>
        public UserInfo(ulong steamId, string userName) : this()
        {
            SteamId = steamId;
            UserName = userName;
            DisplayName = userName;
        }

        /// <summary>
        /// 从Steam用户ID创建用户信息
        /// </summary>
        /// <param name="steamId">Steam用户ID</param>
        /// <returns>用户信息对象</returns>
        public static UserInfo FromSteamId(CSteamID steamId)
        {
            var userInfo = new UserInfo
            {
                SteamId = steamId.m_SteamID
            };

            // 尝试获取Steam用户名
            try
            {
                if (SteamAPI.Init())
                {
                    var userName = SteamFriends.GetFriendPersonaName(steamId);
                    if (!string.IsNullOrEmpty(userName))
                    {
                        userInfo.UserName = userName;
                        userInfo.DisplayName = userName;
                    }
                    else
                    {
                        userInfo.UserName = $"Player_{steamId.m_SteamID}";
                        userInfo.DisplayName = userInfo.UserName;
                    }
                }
                else
                {
                    userInfo.UserName = $"Player_{steamId.m_SteamID}";
                    userInfo.DisplayName = userInfo.UserName;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"获取Steam用户名失败: {ex.Message}");
                userInfo.UserName = $"Player_{steamId.m_SteamID}";
                userInfo.DisplayName = userInfo.UserName;
            }

            return userInfo;
        }

        /// <summary>
        /// 序列化为JSON字符串
        /// </summary>
        /// <returns>JSON字符串</returns>
        public string ToJson()
        {
            try
            {
                return JsonConvert.SerializeObject(this);
            }
            catch (Exception ex)
            {
                Debug.LogError($"用户信息序列化失败: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 从JSON字符串反序列化
        /// </summary>
        /// <param name="json">JSON字符串</param>
        /// <returns>用户信息对象</returns>
        public static UserInfo FromJson(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<UserInfo>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"用户信息反序列化失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 验证用户信息是否有效
        /// </summary>
        /// <returns>是否有效</returns>
        public bool IsValid()
        {
            return SteamId > 0 && !string.IsNullOrEmpty(UserName);
        }

        /// <summary>
        /// 更新最后在线时间
        /// </summary>
        public void UpdateLastSeen()
        {
            LastSeen = DateTime.UtcNow;
        }

        /// <summary>
        /// 获取显示用的用户名
        /// </summary>
        /// <returns>显示用户名</returns>
        public string GetDisplayName()
        {
            return !string.IsNullOrEmpty(DisplayName) ? DisplayName : UserName;
        }

        /// <summary>
        /// 重写ToString方法
        /// </summary>
        /// <returns>字符串表示</returns>
        public override string ToString()
        {
            return $"{GetDisplayName()} ({SteamId})";
        }

        /// <summary>
        /// 重写Equals方法
        /// </summary>
        /// <param name="obj">比较对象</param>
        /// <returns>是否相等</returns>
        public override bool Equals(object obj)
        {
            if (obj is UserInfo other)
            {
                return SteamId == other.SteamId;
            }
            return false;
        }

        /// <summary>
        /// 重写GetHashCode方法
        /// </summary>
        /// <returns>哈希码</returns>
        public override int GetHashCode()
        {
            return SteamId.GetHashCode();
        }
    }
}