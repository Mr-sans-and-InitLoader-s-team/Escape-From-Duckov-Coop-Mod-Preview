// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspicuous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

using LiteNetLib;

namespace EscapeFromDuckovCoopMod.Net;

/// <summary>
/// 网络数据包优先级系统（参考 Fika 架构设计）
/// </summary>
public enum PacketPriority : byte
{
    /// <summary>
    /// 关键功能：投票、伤害、拾取、交互
    /// - 必须送达
    /// - 严格有序
    /// - 使用 ReliableOrdered
    /// </summary>
    Critical = 0,

    /// <summary>
    /// 重要状态：血量、装备、弹药
    /// - 必须送达
    /// - 只保留最新
    /// - 使用 ReliableSequenced
    /// </summary>
    Important = 1,

    /// <summary>
    /// 普通事件：NPC 状态、物品生成、环境交互
    /// - 必须送达
    /// - 顺序无关
    /// - 使用 ReliableUnordered
    /// </summary>
    Normal = 2,

    /// <summary>
    /// 高频更新：位置、旋转、姿态、动画
    /// - 可以丢弃
    /// - 下一帧覆盖
    /// - 使用 Unreliable
    /// </summary>
    Frequent = 3,

    /// <summary>
    /// 语音数据：VOIP 语音流
    /// - 可以丢弃
    /// - 只保留最新
    /// - 使用 Sequenced
    /// </summary>
    Voice = 4
}

/// <summary>
/// PacketPriority 扩展方法
/// </summary>
public static class PacketPriorityExtensions
{
    /// <summary>
    /// 根据优先级获取对应的 LiteNetLib DeliveryMethod
    /// </summary>
    public static DeliveryMethod GetDeliveryMethod(this PacketPriority priority)
    {
        return priority switch
        {
            PacketPriority.Critical => DeliveryMethod.ReliableOrdered,
            PacketPriority.Important => DeliveryMethod.ReliableSequenced,
            PacketPriority.Normal => DeliveryMethod.ReliableUnordered,
            PacketPriority.Frequent => DeliveryMethod.Unreliable,
            PacketPriority.Voice => DeliveryMethod.Sequenced,
            _ => DeliveryMethod.ReliableOrdered  // 默认最高优先级（安全第一）
        };
    }

    /// <summary>
    /// 获取推荐的通道编号（用于多通道系统）
    /// </summary>
    public static byte GetChannelNumber(this PacketPriority priority)
    {
        return priority switch
        {
            PacketPriority.Critical => 0,   // 通道0 - 最高优先级
            PacketPriority.Important => 1,  // 通道1 - 重要状态
            PacketPriority.Normal => 2,     // 通道2 - 普通事件
            PacketPriority.Frequent => 3,   // 通道3 - 高频更新
            PacketPriority.Voice => 3,      // 通道3 - 语音（和高频共享）
            _ => 0
        };
    }

    /// <summary>
    /// 获取优先级的可读描述
    /// </summary>
    public static string GetDescription(this PacketPriority priority)
    {
        return priority switch
        {
            PacketPriority.Critical => "关键（投票、伤害、拾取）",
            PacketPriority.Important => "重要（血量、装备）",
            PacketPriority.Normal => "普通（NPC、物品生成）",
            PacketPriority.Frequent => "高频（位置、动画）",
            PacketPriority.Voice => "语音（VOIP）",
            _ => "未知"
        };
    }

    /// <summary>
    /// 判断是否需要可靠传输
    /// </summary>
    public static bool IsReliable(this PacketPriority priority)
    {
        return priority switch
        {
            PacketPriority.Critical => true,
            PacketPriority.Important => true,
            PacketPriority.Normal => true,
            PacketPriority.Frequent => false,
            PacketPriority.Voice => false,
            _ => true
        };
    }
}

