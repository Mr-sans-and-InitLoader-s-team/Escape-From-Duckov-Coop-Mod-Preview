using Duckov.Buildings;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(Showcase), "Save")]
internal static class SharedHomeDisplaySavePatch
{
    private static bool Prefix() => !SharedHomeSave.OverlayActive && !SharedHome.Reloading && !SharedHomeDisplays.Loading;
}

[HarmonyPatch(typeof(Showcase), "Awake")]
internal static class SharedHomeDisplayRegisterPatch
{
    private static void Postfix(Showcase __instance) => SharedHomeDisplays.Displays.Add(__instance);
}

[HarmonyPatch(typeof(Showcase), "OnTreeChanged")]
internal static class SharedHomeDisplayChangedPatch
{
    private static void Postfix() => SharedHome.MarkDirty();
}

[HarmonyPatch(typeof(Showcase), nameof(Showcase.ShowUI))]
internal static class SharedHomeDisplayEditPatch
{
    // The display is a replica. Its contents stay on the host until a transfer is
    // confirmed; opening the unmodified editor would let it clone those items.
    private static bool Prefix()
    {
        if (!SharedHome.Guest) return true;
        MModUI.ShowTip(CoopLocalization.Get("ui.sharedHome.displayHostOnly"));
        return false;
    }
}
