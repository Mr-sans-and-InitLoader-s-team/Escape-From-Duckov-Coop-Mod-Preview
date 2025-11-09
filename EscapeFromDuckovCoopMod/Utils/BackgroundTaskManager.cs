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

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Utils
{
    /// <summary>
    /// 【优化】后台任务管理器：将耗时的计算密集型操作放到后台线程，避免阻塞主线程
    /// </summary>
    public class BackgroundTaskManager : MonoBehaviour
    {
        public static BackgroundTaskManager Instance { get; private set; }

        // 主线程回调队列
        private readonly ConcurrentQueue<Action> _mainThreadCallbacks = new();
        
        // 活跃任务计数
        private int _activeTaskCount = 0;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Update()
        {
            // 在主线程处理回调
            while (_mainThreadCallbacks.TryDequeue(out var callback))
            {
                try
                {
                    callback?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BackgroundTask] 主线程回调失败: {e}");
                }
            }
        }

        /// <summary>
        /// 在后台线程执行任务，完成后在主线程回调
        /// </summary>
        /// <param name="backgroundWork">后台工作（在后台线程执行）</param>
        /// <param name="mainThreadCallback">主线程回调（在主线程执行）</param>
        /// <param name="taskName">任务名称（用于日志）</param>
        public void RunOnBackground(Action backgroundWork, Action mainThreadCallback = null, string taskName = "Unknown")
        {
            if (backgroundWork == null) return;

            Interlocked.Increment(ref _activeTaskCount);

            Task.Run(() =>
            {
                try
                {
                    backgroundWork();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BackgroundTask] 后台任务 '{taskName}' 失败: {e}");
                }
                finally
                {
                    // 完成后在主线程回调
                    if (mainThreadCallback != null)
                    {
                        _mainThreadCallbacks.Enqueue(mainThreadCallback);
                    }

                    Interlocked.Decrement(ref _activeTaskCount);
                }
            });
        }

        /// <summary>
        /// 在后台线程执行任务，返回结果后在主线程回调
        /// </summary>
        public void RunOnBackground<T>(Func<T> backgroundWork, Action<T> mainThreadCallback, string taskName = "Unknown")
        {
            if (backgroundWork == null) return;

            Interlocked.Increment(ref _activeTaskCount);

            Task.Run(() =>
            {
                T result = default;
                try
                {
                    result = backgroundWork();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BackgroundTask] 后台任务 '{taskName}' 失败: {e}");
                }
                finally
                {
                    // 将结果传递给主线程回调
                    if (mainThreadCallback != null)
                    {
                        _mainThreadCallbacks.Enqueue(() => mainThreadCallback(result));
                    }

                    Interlocked.Decrement(ref _activeTaskCount);
                }
            });
        }

        /// <summary>
        /// 获取当前活跃任务数量
        /// </summary>
        public int ActiveTaskCount => _activeTaskCount;

        /// <summary>
        /// 是否有任务正在运行
        /// </summary>
        public bool HasActiveTasks => _activeTaskCount > 0;
    }
}

