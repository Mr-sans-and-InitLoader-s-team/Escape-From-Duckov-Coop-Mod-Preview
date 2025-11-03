namespace EscapeFromDuckovCoopMod.Chat.Models
{
    /// <summary>
    /// 聊天消息类型枚举
    /// </summary>
    public enum MessageType
    {
        /// <summary>
        /// 普通聊天消息
        /// </summary>
        Normal,

        /// <summary>
        /// 系统消息
        /// </summary>
        System,

        /// <summary>
        /// 玩家加入消息
        /// </summary>
        Join,

        /// <summary>
        /// 玩家离开消息
        /// </summary>
        Leave,

        /// <summary>
        /// 错误消息
        /// </summary>
        Error
    }
}