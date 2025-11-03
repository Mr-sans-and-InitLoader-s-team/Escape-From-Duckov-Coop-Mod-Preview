using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.Models;

namespace EscapeFromDuckovCoopMod.Chat.Routing
{
    /// <summary>
    /// 消息验证器
    /// 负责验证消息的格式、内容和安全性
    /// </summary>
    public class MessageValidator
    {
        #region 常量定义

        /// <summary>
        /// 最大消息长度
        /// </summary>
        private const int MAX_MESSAGE_LENGTH = 500;

        /// <summary>
        /// 最小消息长度
        /// </summary>
        private const int MIN_MESSAGE_LENGTH = 1;

        /// <summary>
        /// 最大用户名长度
        /// </summary>
        private const int MAX_USERNAME_LENGTH = 50;

        /// <summary>
        /// 消息时间戳容差（分钟）
        /// </summary>
        private const int TIMESTAMP_TOLERANCE_MINUTES = 60;

        #endregion

        #region 字段和属性

        /// <summary>
        /// 禁用词列表
        /// </summary>
        private readonly HashSet<string> _bannedWords;

        /// <summary>
        /// 危险字符模式
        /// </summary>
        private readonly Regex _dangerousCharPattern;

        /// <summary>
        /// URL模式
        /// </summary>
        private readonly Regex _urlPattern;

        /// <summary>
        /// 验证配置
        /// </summary>
        private ValidationConfig _config;

        /// <summary>
        /// 验证统计信息
        /// </summary>
        private ValidationStatistics _statistics;

        #endregion

        #region 构造函数

        /// <summary>
        /// 初始化消息验证器
        /// </summary>
        public MessageValidator()
        {
            // 初始化禁用词列表
            _bannedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // 这里可以添加需要过滤的词汇
                "spam", "hack", "cheat"
            };

            // 初始化正则表达式
            _dangerousCharPattern = new Regex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);
            _urlPattern = new Regex(@"https?://[^\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // 初始化配置
            _config = new ValidationConfig();

            // 初始化统计信息
            _statistics = new ValidationStatistics();

            LogDebug("消息验证器已初始化");
        }

        #endregion

        #region 主要验证方法

        /// <summary>
        /// 验证消息
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <returns>验证结果</returns>
        public ValidationResult ValidateMessage(ChatMessage message)
        {
            try
            {
                _statistics.TotalValidationAttempts++;

                if (message == null)
                {
                    return CreateFailureResult("消息不能为空");
                }

                // 基础字段验证
                var basicResult = ValidateBasicFields(message);
                if (!basicResult.IsValid)
                {
                    return basicResult;
                }

                // 内容验证
                var contentResult = ValidateContent(message.Content);
                if (!contentResult.IsValid)
                {
                    return contentResult;
                }

                // 发送者验证
                var senderResult = ValidateSender(message.Sender);
                if (!senderResult.IsValid)
                {
                    return senderResult;
                }

                // 时间戳验证
                var timestampResult = ValidateTimestamp(message.Timestamp);
                if (!timestampResult.IsValid)
                {
                    return timestampResult;
                }

                // 元数据验证
                var metadataResult = ValidateMetadata(message.Metadata);
                if (!metadataResult.IsValid)
                {
                    return metadataResult;
                }

                // 安全性验证
                var securityResult = ValidateSecurity(message);
                if (!securityResult.IsValid)
                {
                    return securityResult;
                }

                _statistics.TotalValidationsPassed++;
                return CreateSuccessResult();
            }
            catch (Exception ex)
            {
                LogError($"验证消息时发生异常: {ex.Message}");
                _statistics.TotalValidationErrors++;
                return CreateFailureResult($"验证异常: {ex.Message}");
            }
        }

        #endregion

        #region 具体验证方法

        /// <summary>
        /// 验证基础字段
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <returns>验证结果</returns>
        private ValidationResult ValidateBasicFields(ChatMessage message)
        {
            // 验证消息ID
            if (string.IsNullOrEmpty(message.Id))
            {
                return CreateFailureResult("消息ID不能为空");
            }

            if (message.Id.Length > 100)
            {
                return CreateFailureResult("消息ID过长");
            }

            // 验证消息类型
            if (!Enum.IsDefined(typeof(MessageType), message.Type))
            {
                return CreateFailureResult("无效的消息类型");
            }

            return CreateSuccessResult();
        }

        /// <summary>
        /// 验证消息内容
        /// </summary>
        /// <param name="content">消息内容</param>
        /// <returns>验证结果</returns>
        private ValidationResult ValidateContent(string content)
        {
            // 检查内容长度
            if (string.IsNullOrEmpty(content))
            {
                if (_config.AllowEmptyContent)
                {
                    return CreateSuccessResult();
                }
                return CreateFailureResult("消息内容不能为空");
            }

            if (content.Length < MIN_MESSAGE_LENGTH)
            {
                return CreateFailureResult($"消息内容过短，最少需要 {MIN_MESSAGE_LENGTH} 个字符");
            }

            if (content.Length > MAX_MESSAGE_LENGTH)
            {
                return CreateFailureResult($"消息内容过长，最多允许 {MAX_MESSAGE_LENGTH} 个字符");
            }

            // 检查危险字符
            if (_config.FilterDangerousChars && _dangerousCharPattern.IsMatch(content))
            {
                return CreateFailureResult("消息包含危险字符");
            }

            // 检查禁用词
            if (_config.FilterBannedWords && ContainsBannedWords(content))
            {
                return CreateFailureResult("消息包含禁用词汇");
            }

            // 检查URL
            if (_config.FilterUrls && _urlPattern.IsMatch(content))
            {
                return CreateFailureResult("消息不允许包含URL链接");
            }

            // 检查重复字符
            if (_config.FilterRepeatedChars && HasExcessiveRepeatedChars(content))
            {
                return CreateFailureResult("消息包含过多重复字符");
            }

            return CreateSuccessResult();
        }

        /// <summary>
        /// 验证发送者信息
        /// </summary>
        /// <param name="sender">发送者信息</param>
        /// <returns>验证结果</returns>
        private ValidationResult ValidateSender(UserInfo sender)
        {
            if (sender == null)
            {
                return CreateFailureResult("发送者信息不能为空");
            }

            // 验证用户名
            if (string.IsNullOrEmpty(sender.UserName))
            {
                return CreateFailureResult("发送者用户名不能为空");
            }

            if (sender.UserName.Length > MAX_USERNAME_LENGTH)
            {
                return CreateFailureResult($"发送者用户名过长，最多允许 {MAX_USERNAME_LENGTH} 个字符");
            }

            // 验证用户名格式
            if (_config.ValidateUsernameFormat && !IsValidUsername(sender.UserName))
            {
                return CreateFailureResult("发送者用户名格式无效");
            }

            // 验证Steam ID
            if (_config.ValidateSteamId && sender.SteamId == 0)
            {
                return CreateFailureResult("无效的Steam ID");
            }

            return CreateSuccessResult();
        }

        /// <summary>
        /// 验证时间戳
        /// </summary>
        /// <param name="timestamp">时间戳</param>
        /// <returns>验证结果</returns>
        private ValidationResult ValidateTimestamp(DateTime timestamp)
        {
            if (!_config.ValidateTimestamp)
            {
                return CreateSuccessResult();
            }

            var now = DateTime.UtcNow;
            var timeDiff = Math.Abs((now - timestamp).TotalMinutes);

            if (timeDiff > TIMESTAMP_TOLERANCE_MINUTES)
            {
                return CreateFailureResult($"消息时间戳异常，与当前时间相差 {timeDiff:F1} 分钟");
            }

            return CreateSuccessResult();
        }

        /// <summary>
        /// 验证元数据
        /// </summary>
        /// <param name="metadata">元数据</param>
        /// <returns>验证结果</returns>
        private ValidationResult ValidateMetadata(Dictionary<string, object> metadata)
        {
            if (!_config.ValidateMetadata || metadata == null)
            {
                return CreateSuccessResult();
            }

            // 检查元数据大小
            if (metadata.Count > _config.MaxMetadataEntries)
            {
                return CreateFailureResult($"元数据条目过多，最多允许 {_config.MaxMetadataEntries} 条");
            }

            // 检查每个元数据项
            foreach (var kvp in metadata)
            {
                if (string.IsNullOrEmpty(kvp.Key))
                {
                    return CreateFailureResult("元数据键不能为空");
                }

                if (kvp.Key.Length > _config.MaxMetadataKeyLength)
                {
                    return CreateFailureResult($"元数据键过长: {kvp.Key}");
                }

                // 检查值的大小（简单估算）
                var valueSize = EstimateObjectSize(kvp.Value);
                if (valueSize > _config.MaxMetadataValueSize)
                {
                    return CreateFailureResult($"元数据值过大: {kvp.Key}");
                }
            }

            return CreateSuccessResult();
        }

        /// <summary>
        /// 验证安全性
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <returns>验证结果</returns>
        private ValidationResult ValidateSecurity(ChatMessage message)
        {
            if (!_config.EnableSecurityValidation)
            {
                return CreateSuccessResult();
            }

            // 检查消息是否可能是恶意的
            if (IsPotentiallyMalicious(message))
            {
                return CreateFailureResult("检测到潜在恶意消息");
            }

            // 检查消息频率（需要外部频率限制器支持）
            // 这里可以添加更多安全检查

            return CreateSuccessResult();
        }

        #endregion

        #region 辅助验证方法

        /// <summary>
        /// 检查是否包含禁用词
        /// </summary>
        /// <param name="content">内容</param>
        /// <returns>是否包含禁用词</returns>
        private bool ContainsBannedWords(string content)
        {
            if (string.IsNullOrEmpty(content))
                return false;

            var words = content.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Any(word => _bannedWords.Contains(word.Trim()));
        }

        /// <summary>
        /// 检查是否有过多重复字符
        /// </summary>
        /// <param name="content">内容</param>
        /// <returns>是否有过多重复字符</returns>
        private bool HasExcessiveRepeatedChars(string content)
        {
            if (string.IsNullOrEmpty(content))
                return false;

            const int maxRepeatedChars = 5;
            int currentCount = 1;
            char lastChar = content[0];

            for (int i = 1; i < content.Length; i++)
            {
                if (content[i] == lastChar)
                {
                    currentCount++;
                    if (currentCount > maxRepeatedChars)
                    {
                        return true;
                    }
                }
                else
                {
                    currentCount = 1;
                    lastChar = content[i];
                }
            }

            return false;
        }

        /// <summary>
        /// 验证用户名格式
        /// </summary>
        /// <param name="username">用户名</param>
        /// <returns>是否有效</returns>
        private bool IsValidUsername(string username)
        {
            if (string.IsNullOrEmpty(username))
                return false;

            // 用户名只能包含字母、数字、下划线和连字符
            var usernamePattern = new Regex(@"^[a-zA-Z0-9_\-\u4e00-\u9fa5]+$");
            return usernamePattern.IsMatch(username);
        }

        /// <summary>
        /// 检查是否为潜在恶意消息
        /// </summary>
        /// <param name="message">消息</param>
        /// <returns>是否潜在恶意</returns>
        private bool IsPotentiallyMalicious(ChatMessage message)
        {
            // 简单的恶意检测逻辑
            var content = message.Content?.ToLower() ?? "";

            // 检查是否包含脚本标签
            if (content.Contains("<script") || content.Contains("javascript:"))
            {
                return true;
            }

            // 检查是否包含SQL注入模式
            if (content.Contains("drop table") || content.Contains("delete from"))
            {
                return true;
            }

            // 检查是否全部为大写字母（可能是垃圾信息）
            if (content.Length > 10 && content.All(c => char.IsUpper(c) || !char.IsLetter(c)))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 估算对象大小
        /// </summary>
        /// <param name="obj">对象</param>
        /// <returns>估算大小（字节）</returns>
        private int EstimateObjectSize(object obj)
        {
            if (obj == null)
                return 0;

            if (obj is string str)
                return str.Length * 2; // Unicode字符

            if (obj is int || obj is float)
                return 4;

            if (obj is long || obj is double)
                return 8;

            if (obj is bool)
                return 1;

            // 其他类型的简单估算
            return obj.ToString().Length * 2;
        }

        #endregion

        #region 结果创建方法

        /// <summary>
        /// 创建成功结果
        /// </summary>
        /// <returns>验证结果</returns>
        private ValidationResult CreateSuccessResult()
        {
            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// 创建失败结果
        /// </summary>
        /// <param name="errorMessage">错误消息</param>
        /// <returns>验证结果</returns>
        private ValidationResult CreateFailureResult(string errorMessage)
        {
            _statistics.TotalValidationsFailed++;
            return new ValidationResult 
            { 
                IsValid = false, 
                ErrorMessage = errorMessage 
            };
        }

        #endregion

        #region 配置管理

        /// <summary>
        /// 设置验证配置
        /// </summary>
        /// <param name="config">验证配置</param>
        public void SetValidationConfig(ValidationConfig config)
        {
            _config = config ?? new ValidationConfig();
            LogDebug("验证配置已更新");
        }

        /// <summary>
        /// 获取验证配置
        /// </summary>
        /// <returns>验证配置</returns>
        public ValidationConfig GetValidationConfig()
        {
            return _config;
        }

        /// <summary>
        /// 添加禁用词
        /// </summary>
        /// <param name="word">禁用词</param>
        public void AddBannedWord(string word)
        {
            if (!string.IsNullOrEmpty(word))
            {
                _bannedWords.Add(word);
                LogDebug($"已添加禁用词: {word}");
            }
        }

        /// <summary>
        /// 移除禁用词
        /// </summary>
        /// <param name="word">禁用词</param>
        public void RemoveBannedWord(string word)
        {
            if (_bannedWords.Remove(word))
            {
                LogDebug($"已移除禁用词: {word}");
            }
        }

        #endregion

        #region 统计信息

        /// <summary>
        /// 获取验证统计信息
        /// </summary>
        /// <returns>验证统计信息</returns>
        public ValidationStatistics GetStatistics()
        {
            return _statistics.Clone();
        }

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void ResetStatistics()
        {
            _statistics.Reset();
            LogDebug("验证统计信息已重置");
        }

        #endregion

        #region 日志方法

        private void LogDebug(string message)
        {
            Debug.Log($"[MessageValidator][DEBUG] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[MessageValidator] {message}");
        }

        #endregion
    }

    /// <summary>
    /// 验证结果
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
    }

    /// <summary>
    /// 验证配置
    /// </summary>
    public class ValidationConfig
    {
        /// <summary>
        /// 是否允许空内容
        /// </summary>
        public bool AllowEmptyContent { get; set; } = false;

        /// <summary>
        /// 是否过滤危险字符
        /// </summary>
        public bool FilterDangerousChars { get; set; } = true;

        /// <summary>
        /// 是否过滤禁用词
        /// </summary>
        public bool FilterBannedWords { get; set; } = true;

        /// <summary>
        /// 是否过滤URL
        /// </summary>
        public bool FilterUrls { get; set; } = false;

        /// <summary>
        /// 是否过滤重复字符
        /// </summary>
        public bool FilterRepeatedChars { get; set; } = true;

        /// <summary>
        /// 是否验证用户名格式
        /// </summary>
        public bool ValidateUsernameFormat { get; set; } = true;

        /// <summary>
        /// 是否验证Steam ID
        /// </summary>
        public bool ValidateSteamId { get; set; } = true;

        /// <summary>
        /// 是否验证时间戳
        /// </summary>
        public bool ValidateTimestamp { get; set; } = true;

        /// <summary>
        /// 是否验证元数据
        /// </summary>
        public bool ValidateMetadata { get; set; } = true;

        /// <summary>
        /// 是否启用安全验证
        /// </summary>
        public bool EnableSecurityValidation { get; set; } = true;

        /// <summary>
        /// 最大元数据条目数
        /// </summary>
        public int MaxMetadataEntries { get; set; } = 10;

        /// <summary>
        /// 最大元数据键长度
        /// </summary>
        public int MaxMetadataKeyLength { get; set; } = 50;

        /// <summary>
        /// 最大元数据值大小
        /// </summary>
        public int MaxMetadataValueSize { get; set; } = 1024;
    }

    /// <summary>
    /// 验证统计信息
    /// </summary>
    public class ValidationStatistics
    {
        /// <summary>
        /// 总验证尝试次数
        /// </summary>
        public long TotalValidationAttempts { get; set; }

        /// <summary>
        /// 总验证通过次数
        /// </summary>
        public long TotalValidationsPassed { get; set; }

        /// <summary>
        /// 总验证失败次数
        /// </summary>
        public long TotalValidationsFailed { get; set; }

        /// <summary>
        /// 总验证错误次数
        /// </summary>
        public long TotalValidationErrors { get; set; }

        /// <summary>
        /// 验证成功率
        /// </summary>
        public double SuccessRate => TotalValidationAttempts > 0 ? 
            (double)TotalValidationsPassed / TotalValidationAttempts * 100 : 0;

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void Reset()
        {
            TotalValidationAttempts = 0;
            TotalValidationsPassed = 0;
            TotalValidationsFailed = 0;
            TotalValidationErrors = 0;
        }

        /// <summary>
        /// 克隆统计信息
        /// </summary>
        /// <returns>统计信息副本</returns>
        public ValidationStatistics Clone()
        {
            return new ValidationStatistics
            {
                TotalValidationAttempts = TotalValidationAttempts,
                TotalValidationsPassed = TotalValidationsPassed,
                TotalValidationsFailed = TotalValidationsFailed,
                TotalValidationErrors = TotalValidationErrors
            };
        }
    }
}