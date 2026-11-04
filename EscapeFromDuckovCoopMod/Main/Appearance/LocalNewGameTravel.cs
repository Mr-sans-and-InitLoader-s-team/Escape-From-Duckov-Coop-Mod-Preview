using Duckov.Scenes;

namespace EscapeFromDuckovCoopMod;

internal static class LocalNewGameTravel
{
    internal static bool IsTutorial => MultiSceneCore.MainScene.HasValue &&
        MultiSceneCore.MainSceneID == "Level_Guide_Main" && Allows("Level_Guide_Main");

    internal static bool Allows(string sceneId)
    {
        var service = NetService.Instance;
        if (service == null || !service.networkStarted || service.IsServer || SharedHome.Enabled ||
            MultiSceneCore.GetVisited(SceneInfoCollection.BaseSceneID)) return false;
        return sceneId == "Prologue_Main" || sceneId == "Level_Guide_Main" ||
            sceneId == SceneInfoCollection.BaseSceneID && MultiSceneCore.MainScene.HasValue &&
            (MultiSceneCore.MainSceneID == "Prologue_Main" || MultiSceneCore.MainSceneID == "Level_Guide_Main");
    }
}
