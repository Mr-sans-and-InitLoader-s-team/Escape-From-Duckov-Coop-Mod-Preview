using System.Reflection;
using System.Reflection.Emit;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(Grenade), "Explode")]
internal static class GrenadeEffectAudioPatch
{
    [ThreadStatic] private static bool _networkEffect;

    private static void Prefix(Grenade __instance, out (bool Previous, IDisposable Audio) __state)
    {
        var networked = NetService.Instance?.networkStarted == true && __instance.GetComponent<NetGrenadeTag>();
        __state = (_networkEffect, networked ? CoopAudioSync.BeginSuppress() : null);
        _networkEffect = networked;
    }
    private static void Finalizer((bool Previous, IDisposable Audio) __state)
    {
        _networkEffect = __state.Previous;
        __state.Audio?.Dispose();
    }
    private static GameObject SpawnEffect(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        var instance = Object.Instantiate(prefab, position, rotation);
        if (_networkEffect) instance.AddComponent<NetworkEffectAudio>();
        return instance;
    }
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Object) &&
                method.Name == nameof(Object.Instantiate) && method.IsGenericMethod &&
                method.GetGenericArguments().SequenceEqual(new[] { typeof(GameObject) }) &&
                method.GetParameters().Length == 3 && method.GetParameters()[1].ParameterType == typeof(Vector3))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(GrenadeEffectAudioPatch), nameof(SpawnEffect));
            }
            yield return instruction;
        }
    }
}
