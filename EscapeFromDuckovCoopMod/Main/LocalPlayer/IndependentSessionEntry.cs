using Duckov.Scenes;

namespace EscapeFromDuckovCoopMod;

internal static class IndependentSessionEntry
{
    internal static string BlockReason(bool shared)
    {
        if (shared) return null;
        if (SceneLoader.IsSceneLoading || !LevelManager.Instance || !LevelManager.LevelInited ||
            !CharacterMainControl.Main || !MultiSceneCore.Instance)
            return "ui.coopMode.independent.enterSave";
        return MultiSceneCore.GetVisited(SceneInfoCollection.BaseSceneID)
            ? null : "ui.coopMode.independent.tutorialRequired";
    }

    internal static bool Check(bool shared)
    {
        var reason = BlockReason(shared);
        if (reason == null) return true;
        var message = CoopLocalization.Get(reason);
        if (NetService.Instance) NetService.Instance.status = message;
        MModUI.Instance?.ShowConnectionError(CoopLocalization.Get("ui.coopMode.independent.title"), message);
        CoopSessionLog.Write("Connection", $"Independent entry blocked: {reason}");
        return false;
    }
}
