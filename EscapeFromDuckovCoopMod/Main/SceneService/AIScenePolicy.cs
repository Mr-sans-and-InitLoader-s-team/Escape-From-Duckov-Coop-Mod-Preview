using Duckov.Scenes;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

internal static class AIScenePolicy
{
    private static MultiSceneCore _core;
    private static int _sceneHandle;
    private static int _lookupFrame = -1;
    private static SceneInfoEntry _sceneInfo;

    internal static bool IsBase
    {
        get
        {
            var core = MultiSceneCore.Instance;
            if (!core) return false;
            var sceneHandle = core.gameObject.scene.handle;
            if (core != _core || sceneHandle != _sceneHandle ||
                (_sceneInfo == null && _lookupFrame != Time.frameCount))
            {
                _core = core;
                _sceneHandle = sceneHandle;
                _lookupFrame = Time.frameCount;
                _sceneInfo = core.SceneInfo;
            }
            return _sceneInfo?.ID == "Base";
        }
    }
}
