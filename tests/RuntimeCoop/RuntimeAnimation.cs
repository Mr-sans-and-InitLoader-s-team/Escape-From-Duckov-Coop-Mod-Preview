using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object=UnityEngine.Object;

namespace CoopRuntimeTests;

internal static class RuntimeAnimation
{
    internal static async UniTask<object> Probe(Assembly mod)
    {
        var type=AccessTools.TypeByName("KINEMATION.MagicBlend.Runtime.MagicBlending");
        var original=CharacterMainControl.Main.GetComponentsInChildren<Component>(true).First(c=>c && c.GetType()==type);
        var asset=AccessTools.Property(type,"BlendAsset").GetValue(original);
        if(asset==null)throw new InvalidOperationException("Native source has no blend asset");
        var copy=Object.Instantiate(original.gameObject);
        copy.name="AnimationInitializationProbe";
        // Held prop agents do not carry runtime Item/Holder bindings into an avatar clone.
        foreach(var agent in copy.GetComponentsInChildren<DuckovItemAgent>(true))agent.enabled=false;
        var blending=copy.GetComponent(type);
        bool Ready()=>new[]{"_poseJobPlayable","_overlayJobPlayable","_layeringJobPlayable"}.All(n=>
            ((AnimationScriptPlayable)AccessTools.Field(type,n).GetValue(blending)).IsValid());
        try
        {
            var before=Ready();
            AccessTools.Method(type,"UpdateMagicBlendAsset").Invoke(blending,new[]{asset,(object)true,.37f,false});
            var pendingType=mod.GetType("EscapeFromDuckovCoopMod.PendingMagicBlend",true);
            var queued=copy.GetComponent(pendingType)!=null;
            await UniTask.NextFrame();await UniTask.NextFrame();await UniTask.NextFrame();
            var ready=Ready();
            var replayed=Mathf.Approximately((float)AccessTools.Field(type,"_desiredBlendTime").GetValue(blending),.37f);
            return new {before,queued,ready,replayed,pending=copy.GetComponent(pendingType)!=null,
                mainAlive=CharacterMainControl.Main && !CharacterMainControl.Main.Health.IsDead};
        }
        finally { Object.Destroy(copy); }
    }
}
