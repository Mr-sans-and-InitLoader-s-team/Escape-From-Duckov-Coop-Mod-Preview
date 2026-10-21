using System.Reflection;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch]
internal static class MagicBlendInitializationPatch
{
    private static readonly Type BlendType = AccessTools.TypeByName("KINEMATION.MagicBlend.Runtime.MagicBlending");
    private static readonly string[] JobFields = { "_poseJobPlayable", "_overlayJobPlayable", "_layeringJobPlayable" };
    private static readonly FieldInfo[] Jobs = BlendType == null ? Array.Empty<FieldInfo>() : JobFields.Select(n => AccessTools.Field(BlendType, n)).ToArray();
    internal static readonly MethodInfo Method = BlendType == null ? null : AccessTools.Method(BlendType, "UpdateMagicBlendAsset");
    private static bool Prepare() => Method != null && Jobs.All(f => f != null);
    private static MethodBase TargetMethod() => Method;
    internal static bool Ready(Component owner) => Jobs.All(f => ((AnimationScriptPlayable)f.GetValue(owner)).IsValid());

    private static bool Prefix(Component __instance, object[] __args)
    {
        if (__args[0] is not UnityEngine.Object asset || !asset || Ready(__instance)) return true;
        var pending = __instance.GetComponent<PendingMagicBlend>();
        if (!pending) pending = __instance.gameObject.AddComponent<PendingMagicBlend>();
        pending.Owner = __instance;
        pending.Arguments = (object[])__args.Clone();
        return false;
    }
}

internal sealed class PendingMagicBlend : MonoBehaviour
{
    internal Component Owner;
    internal object[] Arguments;

    private void LateUpdate()
    {
        if (!Owner) { UnityEngine.Object.Destroy(this); return; }
        if (!MagicBlendInitializationPatch.Ready(Owner)) return;
        var arguments = Arguments;
        UnityEngine.Object.Destroy(this);
        MagicBlendInitializationPatch.Method.Invoke(Owner, arguments);
    }
}
