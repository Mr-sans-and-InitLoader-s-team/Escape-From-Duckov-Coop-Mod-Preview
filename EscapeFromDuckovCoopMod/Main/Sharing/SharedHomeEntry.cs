using Duckov.Utilities;
using UnityEngine.SceneManagement;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeEntry
{
    private static bool _attempted;
    private static int _generation;
    internal static bool AtMainMenu => !LevelManager.Instance && !SceneLoader.IsSceneLoading && SceneLoader.Instance &&
        SceneManager.GetActiveScene().name == GameplayDataSettings.SceneManagement.MainMenuScene.Name;

    internal static void Reset()
    {
        _attempted = false;
        _generation++;
    }
    internal static void Tick()
    {
        if (!SharedHome.Active || _attempted || !AtMainMenu) return;
        if (SharedHome.Guest && !SharedHomeSave.OverlayActive) return;
        _attempted = true;
        Enter(_generation).Forget();
    }

    internal static async UniTask WaitForTitle()
    {
        if (!AtMainMenu) return;
        await UniTask.NextFrame();
        var title = UnityEngine.Object.FindObjectOfType<Title>();
        var timeline = title ? AccessTools.Field(typeof(Title), "timelineToTitle").GetValue(title) as UnityEngine.Playables.PlayableDirector : null;
        if (timeline) await UniTask.WaitUntil(() => !timeline || timeline.state != UnityEngine.Playables.PlayState.Playing);
        await UniTask.NextFrame();
    }
    private static async UniTaskVoid Enter(int generation)
    {
        try
        {
            await WaitForTitle();
            if (generation != _generation || !SharedHome.Active) return;
            if (!SharedHome.Guest && !Duckov.Scenes.MultiSceneCore.GetVisited(SceneInfoCollection.BaseSceneID))
                await SceneLoader.Instance.LoadScene(GameplayDataSettings.SceneManagement.PrologueScene);
            else await SceneLoader.Instance.LoadBaseScene();
        }
        catch (Exception e)
        {
            Debug.LogError($"[SharedHome] Enter base failed: {e}");
            if (generation != _generation) return;
            NetService.Instance.StopNetwork();
            MModUI.ShowTip(CoopLocalization.Get("net.sharedHome.entryFailed"));
        }
    }
}
