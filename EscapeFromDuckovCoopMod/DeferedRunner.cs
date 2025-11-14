// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

/// <summary>
/// 延迟执行器 - 将任务延迟到帧结束时执行，减少场景加载时的性能压力
/// </summary>
internal class DeferedRunner : MonoBehaviour
{
    static DeferedRunner runner;
    static readonly Queue<Action> tasks = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        if (runner)
        {
            return;
        }
        var go = new GameObject("[EscapeFromDuckovCoopModDeferedRunner]")
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        DontDestroyOnLoad(go);
        runner = go.AddComponent<DeferedRunner>();
        runner.StartCoroutine(runner.EofLoop());
    }

    /// <summary>
    /// 将任务延迟到帧结束时执行
    /// </summary>
    public static void EndOfFrame(Action a)
    {
        tasks.Enqueue(a);
    }

    IEnumerator EofLoop()
    {
        var eof = new WaitForEndOfFrame();
        while (true)
        {
            yield return eof;
            while (tasks.Count > 0)
            {
                SafeInvoke(tasks.Dequeue());
            }
        }
    }

    static void SafeInvoke(Action a)
    {
        try
        {
            a?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[DeferedRunner] 延迟任务执行失败: {e}");
        }
    }
}

/// <summary>
/// 在帧内缓存重调用结果。
/// 基于假设：调用的结果在同一帧内不会变化，或caller可以忍受一帧的延迟。
/// </summary>
public static class FrameCache
{
    private static bool FlagsClearRegistered = false;
    private static readonly Dictionary<Delegate, bool> Flags = [];

    /// <summary>
    /// 每帧缓存访问：如果函数第一次调用返回 null，则本帧后续不再调用。
    /// </summary>
    public static T Get<T>(Func<T> fn) where T : UnityEngine.Object
    {
        if (fn == null)
        {
            throw new ArgumentNullException(nameof(fn));
        }

        // 如果已经标记为 null，本帧直接返回 null
        if (Flags.TryGetValue(fn, out var isNull) && isNull)
        {
            return null;
        }

        T result = fn();

        if (result == null)
        {
            // 标记本帧跳过
            Flags[fn] = true;
            // 注册帧结束时清理标记
            if (!FlagsClearRegistered)
            {
                FlagsClearRegistered = true;
                DeferedRunner.EndOfFrame(() =>
                {
                    Flags.Clear();
                    FlagsClearRegistered = false;
                });
            }
            return null;
        }

        return result;
    }
}