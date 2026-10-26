using System.Diagnostics;
using UnityEngine.SceneManagement;

namespace EscapeFromDuckovCoopMod;

internal static class CoopSceneLog
{
    private static long _started;

    internal static void Install()
    {
        SceneLoader.onStartedLoadingScene += context =>
        {
            _started = Stopwatch.GetTimestamp();
            CoopSessionLog.Write("Scene", $"Started target={context.sceneName} from={SceneManager.GetActiveScene().name} dead={CharacterMainControl.Main?.Health?.IsDead}");
        };
        SceneLoader.onAfterSceneInitialize += context => CoopSessionLog.Write("Scene", $"Initialized target={context.sceneName}");
        SceneLoader.onFinishedLoadingScene += context =>
        {
            var elapsed = (Stopwatch.GetTimestamp() - _started) * 1000.0 / Stopwatch.Frequency;
            CoopSessionLog.Write("Scene", $"Finished target={context.sceneName} elapsedMs={elapsed:F1}");
        };
    }
}
