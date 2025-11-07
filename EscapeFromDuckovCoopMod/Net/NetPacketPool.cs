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

using LiteNetLib.Utils;
using System.Collections.Concurrent;

namespace EscapeFromDuckovCoopMod.Net;

/// <summary>
/// 网络数据包对象池（参考 Fika 架构）
/// 用于减少 GC 压力，提高性能
/// </summary>
public static class NetPacketPool
{
    private static readonly ConcurrentBag<NetDataWriter> _writerPool = new();
    private const int MAX_POOL_SIZE = 100; // 最大池大小

    /// <summary>
    /// 从池中获取一个 NetDataWriter
    /// </summary>
    public static NetDataWriter GetWriter()
    {
        if (_writerPool.TryTake(out var writer))
        {
            writer.Reset();
            return writer;
        }

        return new NetDataWriter();
    }

    /// <summary>
    /// 将 NetDataWriter 归还到池中
    /// </summary>
    public static void ReturnWriter(NetDataWriter writer)
    {
        if (writer == null) return;

        // 限制池大小，避免无限增长
        if (_writerPool.Count < MAX_POOL_SIZE)
        {
            writer.Reset();
            _writerPool.Add(writer);
        }
    }

    /// <summary>
    /// 获取池统计信息
    /// </summary>
    public static PoolStats GetStats()
    {
        return new PoolStats
        {
            AvailableCount = _writerPool.Count,
            MaxSize = MAX_POOL_SIZE
        };
    }

    /// <summary>
    /// 清空对象池（用于清理或重置）
    /// </summary>
    public static void Clear()
    {
        while (_writerPool.TryTake(out _))
        {
            // 清空所有对象
        }
    }

    public struct PoolStats
    {
        public int AvailableCount;
        public int MaxSize;

        public float UtilizationRate => MaxSize > 0 ? (float)(MaxSize - AvailableCount) / MaxSize : 0f;

        public override string ToString()
        {
            return $"Pool: {AvailableCount}/{MaxSize} available ({UtilizationRate:P1} in use)";
        }
    }
}

