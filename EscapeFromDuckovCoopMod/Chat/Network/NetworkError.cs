using System;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 网络错误类型枚举
    /// </summary>
    public enum NetworkErrorType
    {
        /// <summary>
        /// 连接失败
        /// </summary>
        ConnectionFailed,

        /// <summary>
        /// 连接丢失
        /// </summary>
        ConnectionLost,

        /// <summary>
        /// 消息发送失败
        /// </summary>
        MessageSendFailed,

        /// <summary>
        /// 历史同步失败
        /// </summary>
        HistorySyncFailed,

        /// <summary>
        /// 服务启动失败
        /// </summary>
        ServiceStartFailed,

        /// <summary>
        /// 无效配置
        /// </summary>
        InvalidConfiguration,

        /// <summary>
        /// 网络不可用
        /// </summary>
        NetworkUnavailable
    }

    /// <summary>
    /// 网络错误信息类
    /// </summary>
    public class NetworkError
    {
        /// <summary>
        /// 错误类型
        /// </summary>
        public NetworkErrorType Type { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 详细信息
        /// </summary>
        public string Details { get; set; }

        /// <summary>
        /// 错误时间
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="type">错误类型</param>
        /// <param name="message">错误消息</param>
        /// <param name="details">详细信息</param>
        public NetworkError(NetworkErrorType type, string message, string details = null)
        {
            Type = type;
            Message = message;
            Details = details;
            Timestamp = DateTime.UtcNow;
        }

        /// <summary>
        /// 重写ToString方法
        /// </summary>
        /// <returns>字符串表示</returns>
        public override string ToString()
        {
            var result = $"[{Type}] {Message}";
            if (!string.IsNullOrEmpty(Details))
            {
                result += $" - {Details}";
            }
            return result;
        }
    }
}