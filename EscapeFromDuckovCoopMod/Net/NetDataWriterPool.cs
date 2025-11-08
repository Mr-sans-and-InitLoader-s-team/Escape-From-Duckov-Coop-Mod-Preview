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

namespace EscapeFromDuckovCoopMod;

/// <summary>
/// 【优化】NetDataWriter 对象池，减少网络包序列化时的 GC 压力
/// </summary>
public static class NetDataWriterPool
{
    private static readonly Stack<NetDataWriter> _pool = new();
    private const int MAX_POOL_SIZE = 10;
    private static readonly object _lock = new();

    /// <summary>
    /// 从对象池获取一个 NetDataWriter
    /// </summary>
    public static NetDataWriter Get()
    {
        lock (_lock)
        {
            if (_pool.Count > 0)
            {
                var writer = _pool.Pop();
                writer.Reset();
                return writer;
            }
        }

        return new NetDataWriter();
    }

    /// <summary>
    /// 将 NetDataWriter 归还到对象池
    /// </summary>
    public static void Return(NetDataWriter writer)
    {
        if (writer == null) return;

        lock (_lock)
        {
            if (_pool.Count < MAX_POOL_SIZE)
            {
                writer.Reset();
                _pool.Push(writer);
            }
        }
    }

    /// <summary>
    /// 清空对象池
    /// </summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _pool.Clear();
        }
    }

    /// <summary>
    /// 获取当前对象池大小
    /// </summary>
    public static int Count
    {
        get
        {
            lock (_lock)
            {
                return _pool.Count;
            }
        }
    }
}

