using Duckov.Scenes;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(LevelManager), "GetPlayerStartLocation")]
internal static class TeleporterSpawnEntryPatch
{
    private static bool Prefix(ref (string sceneID, SubSceneEntry.Location locationData) __result)
    {
        if (!TeleporterSpawnEntry.Matches(MultiSceneCore.MainSceneID)) return true;
        LevelManager.loadLevelBeaconIndex = TeleporterSpawnEntry.Beacon;
        if (!TeleporterSpawnEntry.TryResolve(out __result)) return true;
        LevelManager.loadLevelBeaconIndex = 0;
        return false;
    }
    private static void Postfix((string sceneID, SubSceneEntry.Location locationData) __result)
    {
        if (NetService.Instance?.networkStarted == true) TeleporterSpawnEntry.Publish(__result);
    }
}
