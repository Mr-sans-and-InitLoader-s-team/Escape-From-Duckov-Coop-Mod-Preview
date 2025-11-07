// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspiculous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod.Net;

/// <summary>
/// 网络发送智能扩展方法（参考 Fika 架构）
/// </summary>
public static class NetworkExtensions
{
    /// <summary>
    /// 智能发送：根据 Op 自动选择合适的传输方式
    /// 这是对所有 SendToAll 调用的核心优化
    /// </summary>
    public static void SendSmart(this NetManager netManager, NetDataWriter writer, Op op)
    {
        DeliveryMethod method = op.GetDeliveryMethod();
        byte channel = op.GetChannelNumber();

        // LiteNetLib API: SendToAll(byte[] data, int start, int length, byte channelNumber, DeliveryMethod deliveryMethod)
        netManager.SendToAll(writer.Data, 0, writer.Length, channel, method);

#if DEBUG
        // 调试日志（编译时控制）
        if (Time.frameCount % 300 == 0) // 每5秒输出一次
        {
            Debug.Log($"[NetSmart] Op={op}, Priority={op.GetPriority()}, " +
                     $"Method={method}, Channel={channel}");
        }
#endif
    }

    /// <summary>
    /// 智能发送给特定客户端
    /// </summary>
    public static void SendSmart(this NetPeer peer, NetDataWriter writer, Op op)
    {
        DeliveryMethod method = op.GetDeliveryMethod();
        byte channel = op.GetChannelNumber();

        // LiteNetLib API: Send(byte[] data, int start, int length, byte channelNumber, DeliveryMethod deliveryMethod)
        peer.Send(writer.Data, 0, writer.Length, channel, method);
    }

    /// <summary>
    /// 智能发送给所有客户端（排除某个客户端）
    /// </summary>
    public static void SendSmartExcept(this NetManager netManager, NetDataWriter writer, Op op, NetPeer excludedPeer)
    {
        DeliveryMethod method = op.GetDeliveryMethod();
        byte channel = op.GetChannelNumber();

        // LiteNetLib API: SendToAll(byte[] data, int start, int length, byte channelNumber, DeliveryMethod deliveryMethod, NetPeer excludedPeer)
        netManager.SendToAll(writer.Data, 0, writer.Length, channel, method, excludedPeer);
    }

    /// <summary>
    /// 强制使用指定的传输方式（用于特殊情况）
    /// </summary>
    public static void SendForced(this NetManager netManager, NetDataWriter writer, DeliveryMethod method, byte channel = 0)
    {
        // LiteNetLib API: SendToAll(byte[] data, int start, int length, byte channelNumber, DeliveryMethod deliveryMethod)
        netManager.SendToAll(writer.Data, 0, writer.Length, channel, method);
    }

    /// <summary>
    /// 批量发送（用于优化）
    /// </summary>
    public static void SendBatch<T>(this NetManager netManager, Op op, System.Collections.Generic.IEnumerable<T> items,
        System.Action<NetDataWriter, T> writeItem)
    {
        var writer = new NetDataWriter();
        writer.Put((byte)op);

        int count = 0;
        foreach (var item in items)
        {
            count++;
        }

        writer.Put((ushort)count);

        foreach (var item in items)
        {
            writeItem(writer, item);
        }

        netManager.SendSmart(writer, op);
    }

    /// <summary>
    /// 获取网络统计信息（用于监控）
    /// </summary>
    public static string GetNetworkStats(this NetManager netManager)
    {
        var stats = new System.Text.StringBuilder();
        stats.AppendLine("=== 网络统计 ===");
        stats.AppendLine($"连接数: {netManager.ConnectedPeersCount}");
        stats.AppendLine($"运行状态: {(netManager.IsRunning ? "运行中" : "已停止")}");

        if (netManager.FirstPeer != null)
        {
            var peer = netManager.FirstPeer;
            var statistics = peer.Statistics;
            stats.AppendLine($"\n=== 第一个Peer统计 ===");
            stats.AppendLine($"RTT (往返时间): {peer.Ping}ms");
            stats.AppendLine($"发送字节: {statistics.BytesSent}");
            stats.AppendLine($"接收字节: {statistics.BytesReceived}");
            stats.AppendLine($"丢包数: {statistics.PacketLoss}");
            stats.AppendLine($"发送包数: {statistics.PacketsSent}");
            stats.AppendLine($"接收包数: {statistics.PacketsReceived}");
        }

        return stats.ToString();
    }
}

