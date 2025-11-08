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

using System.Collections;

namespace EscapeFromDuckovCoopMod;

/// <summary>
/// 【优化】场景初始化管理器：分批延迟执行初始化任务，避免场景加载后卡顿
/// </summary>
public class SceneInitManager : MonoBehaviour
{
    public static SceneInitManager Instance { get; private set; }

    private readonly Queue<Action> _taskQueue = new();
    private bool _isProcessing = false;
    private const float MAX_FRAME_TIME_MS = 3f; // 【优化】每帧最多3ms，更平滑

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

    /// <summary>
    /// 添加初始化任务到队列
    /// </summary>
    public void EnqueueTask(Action task, string taskName = "Unknown")
    {
        if (task == null) return;

        _taskQueue.Enqueue(() =>
        {
            try
            {
                task();
            }
            catch (Exception e)
            {
                Debug.LogError($"[SceneInit] Task '{taskName}' failed: {e}");
            }
        });

        // 如果没有在处理，开始处理
        if (!_isProcessing)
        {
            StartCoroutine(ProcessTaskQueue());
        }
    }

    /// <summary>
    /// 延迟添加任务（在指定秒数后添加）
    /// </summary>
    public void EnqueueDelayedTask(Action task, float delaySeconds, string taskName = "Unknown")
    {
        StartCoroutine(DelayedEnqueue(task, delaySeconds, taskName));
    }

    private IEnumerator DelayedEnqueue(Action task, float delaySeconds, string taskName)
    {
        yield return new WaitForSeconds(delaySeconds);
        EnqueueTask(task, taskName);
    }

    /// <summary>
    /// 批量添加任务
    /// </summary>
    public void EnqueueBatch(IEnumerable<Action> tasks, string batchName = "Batch")
    {
        int count = 0;
        foreach (var task in tasks)
        {
            var taskIndex = count++;
            EnqueueTask(task, $"{batchName}_{taskIndex}");
        }
    }

    /// <summary>
    /// 处理任务队列（帧预算控制）
    /// </summary>
    private IEnumerator ProcessTaskQueue()
    {
        _isProcessing = true;

        while (_taskQueue.Count > 0)
        {
            var frameStartTime = Time.realtimeSinceStartup;

            // 每帧处理多个任务，但不超过帧预算
            while (_taskQueue.Count > 0)
            {
                var elapsed = (Time.realtimeSinceStartup - frameStartTime) * 1000f;
                if (elapsed > MAX_FRAME_TIME_MS) break;

                var task = _taskQueue.Dequeue();
                task?.Invoke();
            }

            yield return null; // 下一帧继续
        }

        _isProcessing = false;
    }

    /// <summary>
    /// 清空所有待处理任务
    /// </summary>
    public void ClearQueue()
    {
        _taskQueue.Clear();
        _isProcessing = false;
    }

    /// <summary>
    /// 获取待处理任务数量
    /// </summary>
    public int PendingTaskCount => _taskQueue.Count;

    /// <summary>
    /// 是否正在处理任务
    /// </summary>
    public bool IsProcessing => _isProcessing;
}

