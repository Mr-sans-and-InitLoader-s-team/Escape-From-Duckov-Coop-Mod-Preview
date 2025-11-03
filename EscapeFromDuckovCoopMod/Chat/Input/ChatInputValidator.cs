using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Chat.Input
{
    /// <summary>
    /// 聊天输入验证器
    /// </summary>
    public static class ChatInputValidator
    {
        /// <summary>
        /// 最大消息长度
        /// </summary>
        public const int MAX_MESSAGE_LENGTH = 200;

        /// <summary>
        /// 最小消息长度
        /// </summary>
        public const int MIN_MESSAGE_LENGTH = 1;

        /// <summary>
        /// 禁用词列表（可以从配置文件加载）
        /// </summary>
        private static readonly string[] BANNED_WORDS = {
            // 这里可以添加需要过滤的词汇
        };

        /// <summary>
        /// 特殊字符正则表达式
        /// </summary>
        private static readonly Regex SPECIAL_CHARS_REGEX = new Regex(@"[^\u4e00-\u9fa5\u0030-\u0039\u0041-\u005a\u0061-\u007a\s\.,!?;:()[\]{}\-_+=@#$%^&*~`'""]", RegexOptions.Compiled);

        /// <summary>
        /// 验证消息是否有效
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <returns>验证结果</returns>
        public static ValidationResult ValidateMessage(string message)
        {
            var result = new ValidationResult();

            // 检查空消息
            if (string.IsNullOrEmpty(message))
            {
                result.IsValid = false;
                result.ErrorMessage = "消息不能为空";
                return result;
            }

            // 去除首尾空格后再次检查
            var trimmedMessage = message.Trim();
            if (string.IsNullOrEmpty(trimmedMessage))
            {
                result.IsValid = false;
                result.ErrorMessage = "消息不能只包含空格";
                return result;
            }

            // 检查消息长度
            if (trimmedMessage.Length < MIN_MESSAGE_LENGTH)
            {
                result.IsValid = false;
                result.ErrorMessage = $"消息长度不能少于{MIN_MESSAGE_LENGTH}个字符";
                return result;
            }

            if (trimmedMessage.Length > MAX_MESSAGE_LENGTH)
            {
                result.IsValid = false;
                result.ErrorMessage = $"消息长度不能超过{MAX_MESSAGE_LENGTH}个字符";
                return result;
            }

            // 检查禁用词
            if (ContainsBannedWords(trimmedMessage))
            {
                result.IsValid = false;
                result.ErrorMessage = "消息包含不当内容";
                return result;
            }

            // 检查特殊字符
            if (ContainsMaliciousContent(trimmedMessage))
            {
                result.IsValid = false;
                result.ErrorMessage = "消息包含不支持的特殊字符";
                return result;
            }

            // 检查是否为垃圾信息
            if (IsSpamMessage(trimmedMessage))
            {
                result.IsValid = false;
                result.ErrorMessage = "检测到垃圾信息";
                return result;
            }

            result.IsValid = true;
            result.CleanedMessage = trimmedMessage;
            return result;
        }

        /// <summary>
        /// 清理消息内容
        /// </summary>
        /// <param name="message">原始消息</param>
        /// <returns>清理后的消息</returns>
        public static string CleanMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return string.Empty;

            // 去除首尾空格
            var cleaned = message.Trim();

            // 替换多个连续空格为单个空格
            cleaned = Regex.Replace(cleaned, @"\s+", " ");

            // 移除控制字符
            cleaned = Regex.Replace(cleaned, @"[\x00-\x1F\x7F]", "");

            return cleaned;
        }

        /// <summary>
        /// 检查是否包含禁用词
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <returns>是否包含禁用词</returns>
        private static bool ContainsBannedWords(string message)
        {
            if (BANNED_WORDS.Length == 0)
                return false;

            var lowerMessage = message.ToLower();
            foreach (var bannedWord in BANNED_WORDS)
            {
                if (lowerMessage.Contains(bannedWord.ToLower()))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 检查是否包含恶意内容
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <returns>是否包含恶意内容</returns>
        private static bool ContainsMaliciousContent(string message)
        {
            // 检查是否包含过多特殊字符
            var specialCharMatches = SPECIAL_CHARS_REGEX.Matches(message);
            if (specialCharMatches.Count > message.Length * 0.3f) // 超过30%的特殊字符
            {
                return true;
            }

            // 检查是否包含HTML/脚本标签
            if (message.Contains("<script") || message.Contains("</script>") ||
                message.Contains("<iframe") || message.Contains("javascript:"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 检查是否为垃圾信息
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <returns>是否为垃圾信息</returns>
        private static bool IsSpamMessage(string message)
        {
            // 检查重复字符
            if (HasExcessiveRepeatedChars(message))
            {
                return true;
            }

            // 检查是否全为数字或特殊字符
            if (Regex.IsMatch(message, @"^[\d\W]+$") && message.Length > 10)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 检查是否有过多重复字符
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <returns>是否有过多重复字符</returns>
        private static bool HasExcessiveRepeatedChars(string message)
        {
            if (message.Length < 5)
                return false;

            int maxRepeats = 0;
            int currentRepeats = 1;
            char lastChar = message[0];

            for (int i = 1; i < message.Length; i++)
            {
                if (message[i] == lastChar)
                {
                    currentRepeats++;
                }
                else
                {
                    maxRepeats = Math.Max(maxRepeats, currentRepeats);
                    currentRepeats = 1;
                    lastChar = message[i];
                }
            }

            maxRepeats = Math.Max(maxRepeats, currentRepeats);

            // 如果有超过5个连续相同字符，认为是垃圾信息
            return maxRepeats > 5;
        }

        /// <summary>
        /// 格式化消息用于显示
        /// </summary>
        /// <param name="message">原始消息</param>
        /// <returns>格式化后的消息</returns>
        public static string FormatMessageForDisplay(string message)
        {
            if (string.IsNullOrEmpty(message))
                return string.Empty;

            var formatted = CleanMessage(message);

            // 转义HTML字符
            formatted = formatted.Replace("<", "&lt;").Replace(">", "&gt;");

            return formatted;
        }

        /// <summary>
        /// 检查消息发送频率
        /// </summary>
        /// <param name="lastMessageTime">上次发送消息时间</param>
        /// <param name="minInterval">最小间隔（秒）</param>
        /// <returns>是否可以发送</returns>
        public static bool CheckMessageFrequency(DateTime lastMessageTime, float minInterval = 1.0f)
        {
            var timeSinceLastMessage = (DateTime.UtcNow - lastMessageTime).TotalSeconds;
            return timeSinceLastMessage >= minInterval;
        }
    }

    /// <summary>
    /// 验证结果类
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 清理后的消息
        /// </summary>
        public string CleanedMessage { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public ValidationResult()
        {
            IsValid = false;
            ErrorMessage = string.Empty;
            CleanedMessage = string.Empty;
        }
    }
}