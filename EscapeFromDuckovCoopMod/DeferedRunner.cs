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
