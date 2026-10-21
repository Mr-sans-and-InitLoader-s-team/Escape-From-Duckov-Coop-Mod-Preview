namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(LevelManager), nameof(LevelManager.SetControllingCharacter))]
internal static class SpectatorControlPatch
{
    private static bool Prefix(LevelManager __instance, CharacterMainControl character)
    {
        var main = CharacterMainControl.Main;
        // Native Update repeatedly restores control while the dead owner's object is inactive.
        // Keep input ownership, without re-firing the camera target event every frame.
        return Spectator.Instance?._spectatorActive != true || !main ||
               (character && character != main) || __instance.ControllingCharacter != main;
    }
}
