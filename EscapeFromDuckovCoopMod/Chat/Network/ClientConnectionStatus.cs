namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 客机连接状态枚举
    /// </summary>
    public enum ClientConnectionStatus
    {
        /// <summary>
        /// 未连接
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
        /// 连接失败
        /// </summary>
        Failed,
        
        /// <summary>
        /// 重连中
        /// </summary>
        Reconnecting
    }
}