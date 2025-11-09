// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025 Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
// YOU MUST NOT use this software for commercial purposes.
// YOU MUST NOT use this software to run a headless game server.
// YOU MUST include a conspicuous notice of attribution to
// Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Utils
{
    /// <summary>
    /// ✅ 性能监控器 - 跟踪和分析性能数据
    /// </summary>
    public class PerformanceMonitor : MonoBehaviour
    {
        public static PerformanceMonitor Instance { get; private set; }

        // 性能计数器
        public static int GetComponentCalls = 0;
        public static int FindObjectsOfTypeCalls = 0;
        public static int NetworkMessagesSent = 0;
        public static int NetworkMessagesReceived = 0;

        // 帧率追踪
        private readonly Queue<float> _frameTimes = new Queue<float>(300); // 5秒历史
        private float _lastFrameTime;

        // GC 追踪
        private int _lastCollectionCount;
        private float _totalGCTime;

        // 统计输出
        private float _lastStatsTime;
        private const float STATS_INTERVAL = 10f;

        // 性能标记
        private readonly Dictionary<string, PerformanceMarker> _markers = new Dictionary<string, PerformanceMarker>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _lastCollectionCount = GC.CollectionCount(0);
            Debug.Log("[PerformanceMonitor] 性能监控已启动");
        }

        private void Update()
        {
            // 记录帧时间
            var currentTime = Time.realtimeSinceStartup;
            var deltaTime = currentTime - _lastFrameTime;
            _lastFrameTime = currentTime;

            _frameTimes.Enqueue(deltaTime);
            if (_frameTimes.Count > 300)
            {
                _frameTimes.Dequeue();
            }

            // 检查 GC
            var currentCollectionCount = GC.CollectionCount(0);
            if (currentCollectionCount > _lastCollectionCount)
            {
                _totalGCTime += deltaTime * 1000f; // 估算 GC 时间
                _lastCollectionCount = currentCollectionCount;
            }

            // 定期输出统计
            if (Time.time - _lastStatsTime > STATS_INTERVAL)
            {
                LogPerformanceStats();
                _lastStatsTime = Time.time;
            }
        }

        /// <summary>
        /// 开始性能标记
        /// </summary>
        public void BeginMarker(string name)
        {
            if (!_markers.ContainsKey(name))
            {
                _markers[name] = new PerformanceMarker { Name = name };
            }

            _markers[name].StartTime = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// 结束性能标记
        /// </summary>
        public void EndMarker(string name)
        {
            if (_markers.TryGetValue(name, out var marker))
            {
                var elapsed = (Time.realtimeSinceStartup - marker.StartTime) * 1000f;
                marker.TotalTime += elapsed;
                marker.CallCount++;
                marker.LastTime = elapsed;

                if (elapsed > marker.MaxTime) marker.MaxTime = elapsed;
                if (marker.MinTime == 0 || elapsed < marker.MinTime) marker.MinTime = elapsed;
            }
        }

        /// <summary>
        /// 重置标记统计
        /// </summary>
        public void ResetMarker(string name)
        {
            if (_markers.ContainsKey(name))
            {
                _markers.Remove(name);
            }
        }

        /// <summary>
        /// 输出性能统计
        /// </summary>
        public void LogPerformanceStats()
        {
            var stats = GetFrameStats();

            Debug.Log($@"
=== Performance Stats ===
【帧率】
  平均FPS: {stats.AverageFPS:F1}
  最低FPS: {stats.MinFPS:F1}
  最高FPS: {stats.MaxFPS:F1}
  1% Low: {stats.P1FPS:F1}
  
【API调用】
  GetComponent: {GetComponentCalls}
  FindObjectsOfType: {FindObjectsOfTypeCalls}
  
【网络】
  发送消息: {NetworkMessagesSent}
  接收消息: {NetworkMessagesReceived}
  
【内存】
  GC次数: {_lastCollectionCount}
  GC估算时间: {_totalGCTime:F1}ms
  
【性能标记】
{GetMarkerStats()}
========================");

            // 重置计数器
            GetComponentCalls = 0;
            FindObjectsOfTypeCalls = 0;
            NetworkMessagesSent = 0;
            NetworkMessagesReceived = 0;
            _totalGCTime = 0f;
        }

        /// <summary>
        /// 获取帧率统计
        /// </summary>
        public FrameStats GetFrameStats()
        {
            if (_frameTimes.Count == 0)
            {
                return new FrameStats();
            }

            var times = _frameTimes.ToArray();
            Array.Sort(times);

            var avgTime = times.Average();
            var minTime = times.Min();
            var maxTime = times.Max();
            var p1Index = (int)(times.Length * 0.01f);
            var p1Time = times[p1Index];

            return new FrameStats
            {
                AverageFPS = 1f / avgTime,
                MinFPS = 1f / maxTime,
                MaxFPS = 1f / minTime,
                P1FPS = 1f / p1Time,
                AverageFrameTime = avgTime * 1000f,
                MinFrameTime = minTime * 1000f,
                MaxFrameTime = maxTime * 1000f
            };
        }

        /// <summary>
        /// 获取标记统计信息
        /// </summary>
        private string GetMarkerStats()
        {
            if (_markers.Count == 0)
            {
                return "  (无性能标记)";
            }

            var sb = new System.Text.StringBuilder();
            foreach (var marker in _markers.Values.OrderByDescending(m => m.TotalTime))
            {
                var avgTime = marker.CallCount > 0 ? marker.TotalTime / marker.CallCount : 0;
                sb.AppendLine($"  {marker.Name}:");
                sb.AppendLine($"    调用: {marker.CallCount}次");
                sb.AppendLine($"    总计: {marker.TotalTime:F2}ms");
                sb.AppendLine($"    平均: {avgTime:F2}ms");
                sb.AppendLine($"    最小/最大: {marker.MinTime:F2}/{marker.MaxTime:F2}ms");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 获取内存统计
        /// </summary>
        public MemoryStats GetMemoryStats()
        {
            return new MemoryStats
            {
                TotalAllocatedMemory = GC.GetTotalMemory(false) / (1024f * 1024f),
                MonoHeapSize = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / (1024f * 1024f),
                MonoUsedSize = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / (1024f * 1024f),
                TotalReservedMemory = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f),
                TotalAllocatedMemoryLong = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f)
            };
        }

        /// <summary>
        /// 记录网络消息
        /// </summary>
        public static void RecordNetworkSend() => NetworkMessagesSent++;
        public static void RecordNetworkReceive() => NetworkMessagesReceived++;

        /// <summary>
        /// 记录 GetComponent 调用
        /// </summary>
        public static void RecordGetComponent() => GetComponentCalls++;

        /// <summary>
        /// 记录 FindObjectsOfType 调用
        /// </summary>
        public static void RecordFindObjectsOfType() => FindObjectsOfTypeCalls++;

        private class PerformanceMarker
        {
            public string Name;
            public float StartTime;
            public float TotalTime;
            public float LastTime;
            public float MinTime;
            public float MaxTime;
            public int CallCount;
        }
    }

    /// <summary>
    /// 帧率统计数据
    /// </summary>
    public struct FrameStats
    {
        public float AverageFPS;
        public float MinFPS;
        public float MaxFPS;
        public float P1FPS;  // 1% Low FPS
        public float AverageFrameTime;
        public float MinFrameTime;
        public float MaxFrameTime;
    }

    /// <summary>
    /// 内存统计数据
    /// </summary>
    public struct MemoryStats
    {
        public float TotalAllocatedMemory;    // MB
        public float MonoHeapSize;            // MB
        public float MonoUsedSize;            // MB
        public float TotalReservedMemory;     // MB
        public float TotalAllocatedMemoryLong; // MB
    }

    /// <summary>
    /// ✅ 性能监控扩展方法 - 便捷使用
    /// </summary>
    public static class PerformanceMonitorExtensions
    {
        /// <summary>
        /// 使用性能标记包装方法调用
        /// </summary>
        public static void WithPerformanceMarker(this string markerName, Action action)
        {
            if (PerformanceMonitor.Instance != null)
            {
                PerformanceMonitor.Instance.BeginMarker(markerName);
                try
                {
                    action?.Invoke();
                }
                finally
                {
                    PerformanceMonitor.Instance.EndMarker(markerName);
                }
            }
            else
            {
                action?.Invoke();
            }
        }

        /// <summary>
        /// 使用性能标记包装方法调用（带返回值）
        /// </summary>
        public static T WithPerformanceMarker<T>(this string markerName, Func<T> func)
        {
            if (PerformanceMonitor.Instance != null)
            {
                PerformanceMonitor.Instance.BeginMarker(markerName);
                try
                {
                    return func();
                }
                finally
                {
                    PerformanceMonitor.Instance.EndMarker(markerName);
                }
            }
            else
            {
                return func();
            }
        }
    }
}

