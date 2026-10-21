using Duckov.Scenes;

namespace EscapeFromDuckovCoopMod;

internal static class LocalAppearanceTravel
{
    private static string _editorScene, _returnScene;
    private static bool _entered;

    internal static void Begin(string editorScene)
    {
        _editorScene = editorScene;
        _returnScene = MultiSceneCore.MainSceneID;
        _entered = false;
        LevelManager.OnLevelInitialized -= OnLevelInitialized;
        LevelManager.OnLevelInitialized += OnLevelInitialized;
        SceneNet.Instance.AuthorizeLocalSceneLoad(editorScene, throughSettlement: true);
    }

    internal static bool Allows(string sceneId) => _editorScene != null &&
        (sceneId == _editorScene || sceneId == _returnScene);

    private static void OnLevelInitialized()
    {
        if (MultiSceneCore.MainSceneID == _editorScene) _entered = true;
        else if (_entered) Reset();
    }

    internal static void Reset()
    {
        LevelManager.OnLevelInitialized -= OnLevelInitialized;
        _editorScene = _returnScene = null;
        _entered = false;
    }
}
