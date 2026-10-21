using System.Reflection.Emit;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(HealthSimpleBase), "OnHurt")]
internal static class DestructiblePlayerDamagePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var native = AccessTools.PropertyGetter(typeof(CharacterMainControl), nameof(CharacterMainControl.IsMainCharacter));
        var player = AccessTools.Method(typeof(DestructiblePlayerDamagePatch), nameof(IsPlayer));
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(native))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = player;
            }
            yield return instruction;
        }
    }

    private static bool IsPlayer(CharacterMainControl character) => character &&
        (character.IsMainCharacter || character.GetComponent<RemoteReplicaTag>());
}
