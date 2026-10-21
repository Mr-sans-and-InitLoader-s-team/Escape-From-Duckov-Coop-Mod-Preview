namespace EscapeFromDuckovCoopMod;

internal sealed class CoopModeDuckAnimation : MonoBehaviour
{
    private static readonly System.Reflection.FieldInfo BlendPlayback = AccessTools.Field(MagicBlendInitializationPatch.Method.DeclaringType, "_blendPlayback");
    private Component _blending;
    private Animator _animator;
    private CoopModeDuckPose _pose;
    private bool _hovered;
    private readonly HashSet<string> _parameters = new();
    internal bool Preparing { get; set; } = true;

    internal bool Settled => _parameters.Count > 0 && _blending &&
        MagicBlendInitializationPatch.Ready(_blending) &&
        Mathf.Approximately((float)BlendPlayback.GetValue(_blending), 1f) &&
        Enumerable.Range(0, _animator.layerCount).All(layer => !_animator.IsInTransition(layer));

    internal void Initialize(CharacterModel model, CoopModeDuckPose pose)
    {
        _pose = pose;
        _animator = model.GetComponentInChildren<Animator>(true);
        _blending = _animator.GetComponent(MagicBlendInitializationPatch.Method.DeclaringType);
        _animator.applyRootMotion = false;
        _animator.fireEvents = false;
        _animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;
        foreach (var behaviour in model.GetComponentsInChildren<Behaviour>(true))
            if (VisualOnly(behaviour)) behaviour.enabled = true;
    }

    private static bool VisualOnly(Behaviour component)
    {
        var type = component.GetType();
        return component is Animator or CustomFaceInstance or CustomFacePart or CoopModeDuckAnimation ||
            type.Namespace == "UnityEngine.ProBuilder" ||
            type.FullName == "KINEMATION.MagicBlend.Runtime.MagicBlending" ||
            type.FullName == "KINEMATION.KAnimationCore.Runtime.Rig.KRigComponent" ||
            component is PendingMagicBlend;
    }

    private void Start()
    {
        foreach (var parameter in _animator.parameters) _parameters.Add(parameter.name);
        // Native animation Awake may add an audio poster; previews never fire gameplay/audio events.
        foreach (var behaviour in GetComponentsInChildren<Behaviour>(true))
            if (!VisualOnly(behaviour)) behaviour.enabled = false;
    }

    internal void SetHovered(bool hovered) => _hovered = hovered;

    private void Update()
    {
        if (_parameters.Contains("HandState")) _animator.SetInteger("HandState", (int)HandheldAnimationType.gun);
        if (_parameters.Contains("GunReady")) _animator.SetBool("GunReady", true);
        if (_parameters.Contains("MoveDirY")) _animator.SetFloat("MoveDirY", 1);
        if (_parameters.Contains("MoveSpeed"))
            _animator.SetFloat("MoveSpeed", _hovered ? _pose.hoverSpeed : 0, .15f, Time.unscaledDeltaTime);
    }
}

[HarmonyPatch]
internal static class CoopModePreviewInitializationPatch
{
    private static System.Reflection.MethodBase TargetMethod() => MagicBlendInitializationPatch.Method;

    [HarmonyPriority(Priority.First)]
    private static void Prefix(Component __instance, object[] __args)
    {
        if (__instance.GetComponentInParent<CoopModeDuckAnimation>()?.Preparing == true) __args[1] = false;
    }
}
