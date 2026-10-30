using Duckov.Scenes;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeTemporaryLoadouts
{
    private static readonly Dictionary<string, byte[]> Profiles = new();
    private static MultiSceneCore _core;
    private static int _sceneHandle;

    private static void CheckScene()
    {
        var core = MultiSceneCore.Instance;
        var handle = core ? core.gameObject.scene.handle : 0;
        if (core == _core && handle == _sceneHandle) return;
        Profiles.Clear();
        _core = core;
        _sceneHandle = handle;
    }

    internal static byte[] Load(string key)
    {
        CheckScene();
        return Profiles.TryGetValue(key, out var bytes) ? bytes : null;
    }

    internal static void Save(string key, byte[] bytes)
    {
        CheckScene();
        Profiles[key] = bytes;
    }

    internal static void Reset()
    {
        Profiles.Clear();
        _core = null;
        _sceneHandle = 0;
    }
}
