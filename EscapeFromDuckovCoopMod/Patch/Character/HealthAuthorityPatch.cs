using System.Reflection.Emit;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(Health), nameof(Health.CurrentHealth), MethodType.Setter)]
internal static class RemoteHealthWritePatch
{
    private static bool Prefix(Health __instance) => RemoteHealthState.Get(__instance)?.HasSnapshot != true;
}

[HarmonyPatch(typeof(Health), nameof(Health.MaxHealth), MethodType.Getter)]
internal static class RemoteMaxHealthPatch
{
    private static bool Prefix(Health __instance, ref float __result)
    {
        var state = RemoteHealthState.Get(__instance);
        if (state?.HasSnapshot != true) return true;
        __result = state.MaxHealth;
        return false;
    }
}

[HarmonyPatch(typeof(Health), nameof(Health.Hurt))]
internal static class PlayerDamageAiCombatFactorPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var preset = AccessTools.Field(typeof(CharacterMainControl), nameof(CharacterMainControl.characterPreset));
        var resolve = AccessTools.Method(typeof(PlayerDamageAiCombatFactorPatch), nameof(AiCombatPreset));
        foreach (var instruction in instructions)
        {
            if (instruction.LoadsField(preset))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = resolve;
            }
            yield return instruction;
        }
    }

    private static CharacterRandomPreset AiCombatPreset(CharacterMainControl character)
    {
        // Native AI-vs-AI combat scaling must not classify a remote player as an AI.
        return character.GetComponent<RemoteReplicaTag>() ? null : character.characterPreset;
    }
}
