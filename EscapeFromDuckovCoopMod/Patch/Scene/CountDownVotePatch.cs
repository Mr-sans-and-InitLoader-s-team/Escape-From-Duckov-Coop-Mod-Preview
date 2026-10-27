namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(CountDownArea), "OnCountdownSucceed")]
internal static class CountDownVotePatch
{
    private static void Postfix(CountDownArea __instance) => CountDownVoteRecovery.Record(__instance);
}
