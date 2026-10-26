using Duckov;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(AudioManager), "OnFootStepSound")]
internal static class FootstepCapturePatch
{
    private static void Postfix(CharacterSoundMaker.FootStepTypes type, CharacterMainControl character) => FootstepSync.Capture(type, character);
}

[HarmonyPatch(typeof(CharacterSoundMaker), nameof(CharacterSoundMaker.PostFootStepSound))]
internal static class ReplicaFootstepPatch
{
    private static bool Prefix(CharacterSoundMaker __instance) => !FootstepSync.IsReplica(__instance.characterMainControl);
}
