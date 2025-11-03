namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 连接状态枚举
    /// </summary>
    public enum ConnectionStatus
    {
        /// <summary>
        /// 已断开连接
        /// </summary>
        Disconnected,

        /// <summary>
        /// 正在连接
        /// </summary>
        Connecting,

        /// <summary>
        /// 已连接
        /// </summary>
        Connected,

        /// <summary>
        /// 正在主机
        /// </summary>
        Hosting,

        /// <summary>
        /// 连接失败
        /// </summary>
        Failed
    }
}