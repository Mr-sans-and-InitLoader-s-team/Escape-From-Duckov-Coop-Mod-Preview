using System.Collections.Generic;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 网络配置类
    /// </summary>
    public class NetworkConfig
    {
        /// <summary>
        /// 网络类型
        /// </summary>
        public NetworkType Type { get; set; }

        /// <summary>
        /// 主机IP地址
        /// </summary>
        public string HostIP { get; set; }

        /// <summary>
        /// 端口号
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Steam大厅ID
        /// </summary>
        public ulong SteamLobbyId { get; set; }

        /// <summary>
        /// 自定义设置
        /// </summary>
        public Dictionary<string, object> CustomSettings { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public NetworkConfig()
        {
            CustomSettings = new Dictionary<string, object>();
        }

        /// <summary>
        /// 验证配置是否有效
        /// </summary>
        /// <returns>是否有效</returns>
        public bool IsValid()
        {
            switch (Type)
            {
                case NetworkType.SteamP2P:
                    // Steam P2P 不需要IP和端口
                    return true;
                case NetworkType.DirectP2P:
                    // 直连P2P需要有效的IP和端口
                    return !string.IsNullOrEmpty(HostIP) && Port > 0 && Port <= 65535;
                default:
                    return false;
            }
        }
    }
}