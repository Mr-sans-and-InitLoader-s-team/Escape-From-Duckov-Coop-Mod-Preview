namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.LoadBaseScene))]
internal static class ReturnToBasePatch
{
    private static void Prefix()
    {
        if (NetService.Instance?.networkStarted == true)
            SceneNet.Instance?.AuthorizeLocalSceneLoad(SceneInfoCollection.BaseSceneID, throughSettlement: true);
    }
}
