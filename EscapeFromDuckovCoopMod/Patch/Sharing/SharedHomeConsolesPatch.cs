using Duckov.MiniGames;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(GamingConsole), "Awake")]
internal static class SharedHomeConsoleRegisterPatch
{
    private static void Postfix(GamingConsole __instance) => SharedHomeConsoles.Consoles.Add(__instance);
}

[HarmonyPatch(typeof(GamingConsole), "Save")]
internal static class SharedHomeConsoleSavePatch
{
    private static bool Prefix(GamingConsole __instance) => !SharedHomeSave.OverlayActive &&
        !SharedHome.Reloading && !SharedHomeConsoles.Loading(__instance);
}

[HarmonyPatch(typeof(GamingConsole), "OnContentChanged")]
internal static class SharedHomeConsoleChangedPatch
{
    private static bool Prefix(GamingConsole __instance) => !SharedHomeConsoles.Loading(__instance);
    private static void Postfix() => SharedHome.MarkDirty();
}

[HarmonyPatch(typeof(GamingConsoleView), "Show")]
internal static class SharedHomeConsoleEditPatch
{
    private static bool Prefix()
    {
        if (!SharedHome.Guest) return true;
        MModUI.ShowTip(CoopLocalization.Get("ui.sharedHome.consoleHostOnly"));
        return false;
    }
}
