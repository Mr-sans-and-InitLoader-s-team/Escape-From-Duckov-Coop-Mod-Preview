using System.Collections.Generic;
using System.Threading.Tasks;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// Steam P2P 网络接口
    /// </summary>
    public interface ISteamP2PNetwork : INetworkAdapter
    {
        /// <summary>
        /// 创建 Steam 大厅
        /// </summary>
        /// <param name="settings">大厅设置</param>
        /// <returns>创建是否成功</returns>
        Task<bool> CreateSteamLobby(LobbySettings settings);

        /// <summary>
        /// 加入 Steam 大厅
        /// </summary>
        /// <param name="lobbyId">大厅ID</param>
        /// <returns>加入是否成功</returns>
        Task<bool> JoinSteamLobby(ulong lobbyId);

        /// <summary>
        /// 获取可用的大厅列表
        /// </summary>
        /// <returns>大厅信息列表</returns>
        Task<List<LobbyInfo>> GetAvailableLobbies();

        /// <summary>
        /// 邀请好友
        /// </summary>
        /// <param name="friendId">好友Steam ID</param>
        void InviteFriend(ulong friendId);

        /// <summary>
        /// 获取在线好友列表
        /// </summary>
        /// <returns>好友信息列表</returns>
        List<FriendInfo> GetOnlineFriends();

        /// <summary>
        /// 验证Steam用户
        /// </summary>
        /// <param name="steamId">Steam ID</param>
        /// <returns>是否为有效用户</returns>
        bool ValidateSteamUser(ulong steamId);

        /// <summary>
        /// 获取Steam用户信息
        /// </summary>
        /// <param name="steamId">Steam ID</param>
        /// <returns>用户信息</returns>
        Models.UserInfo GetSteamUserInfo(ulong steamId);
    }
}