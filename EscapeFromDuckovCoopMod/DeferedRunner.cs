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
            Debug.LogException(e);
        }
    }
}
