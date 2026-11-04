namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.LoadMainMenu))]
internal static class IndependentEntryMenuPatch
{
    private static void Prefix()
    {
        var service = NetService.Instance;
        if (service?.networkStarted == true && !SharedHome.Enabled) service.StopNetwork();
    }
}
