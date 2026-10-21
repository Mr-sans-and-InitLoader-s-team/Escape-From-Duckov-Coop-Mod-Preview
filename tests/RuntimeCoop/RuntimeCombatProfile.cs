using System.Diagnostics;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace CoopRuntimeTests;

internal sealed class RuntimeCombatProfile : MonoBehaviour
{
    private sealed class Sample { public int Calls; public long Ticks; public long MaxTicks; }
    private static readonly Dictionary<MethodBase,Sample> Samples=new();
    private static readonly List<float> Frames=new();
    private static readonly Dictionary<(int Target,int Actor),(int Hits,float Damage)> AppliedDamage=new();
    private static bool _installed,_active;
    private static long _allocated;
    private static int _collections;
    private static int _hitMarkers,_killMarkers;

    internal static void Begin(Assembly mod)
    {
        if(!_installed)
        {
            _installed=true;
            HitMarker.OnHitMarker+=()=>{if(_active)_hitMarkers++;};
            HitMarker.OnKillMarker+=()=>{if(_active)_killMarkers++;};
            var harmony=new Harmony("local.coop.combat-profile");
            harmony.Patch(AccessTools.Method(typeof(Health),nameof(Health.Hurt)),
                prefix:new HarmonyMethod(typeof(HurtLedger),"Enter"),postfix:new HarmonyMethod(typeof(HurtLedger),"Leave"));
            foreach(var (type,names) in new[]{
                (mod.GetType("EscapeFromDuckovCoopMod.LocalHitKillFx"),new[]{"ClientPlayForPlayer","PlayUiHitKill","FindHitMarkerSingleton","PlayHurtVisual","FindHurtVisualOn","ClientPlayForDestructible"}),
                (mod.GetType("EscapeFromDuckovCoopMod.HealthM"),new[]{"ForceSetHealth","EnsureHealthBar"}),
                (mod.GetType("EscapeFromDuckovCoopMod.HealthBarNameDisplay"),new[]{"TryApplyToHealthBar"}),
                (mod.GetType("EscapeFromDuckovCoopMod.AISyncService"),new[]{"Client_ReportAiHealth","Client_ApplyHealthBroadcast"}),
                (typeof(Health),new[]{"Hurt"}), (typeof(DamageReceiver),new[]{"Hurt"}),
                (typeof(HurtVisual),new[]{"OnHurt"}), (typeof(HitMarker),new[]{"OnHit","OnKill"})})
            foreach(var name in names)
            {
                var method=AccessTools.Method(type,name);
                if(method==null)continue;
                harmony.Patch(method,prefix:new HarmonyMethod(typeof(RuntimeCombatProfile),nameof(Enter)),
                    postfix:new HarmonyMethod(typeof(RuntimeCombatProfile),nameof(Leave)));
            }
            var tracker=new GameObject("CombatProfile");
            UnityEngine.Object.DontDestroyOnLoad(tracker);
            tracker.AddComponent<RuntimeCombatProfile>();
        }
        _hitMarkers=0;_killMarkers=0;
        Samples.Clear();Frames.Clear();AppliedDamage.Clear();_collections=GC.CollectionCount(0);
        _allocated=GC.GetAllocatedBytesForCurrentThread();_active=true;
    }

    private static void Enter(out long __state)=>__state=_active?Stopwatch.GetTimestamp():0;
    private static void Leave(MethodBase __originalMethod,long __state)
    {
        if(!_active || __state==0)return;
        var ticks=Stopwatch.GetTimestamp()-__state;
        if(!Samples.TryGetValue(__originalMethod,out var sample))Samples[__originalMethod]=sample=new Sample();
        sample.Calls++;sample.Ticks+=ticks;sample.MaxTicks=Math.Max(sample.MaxTicks,ticks);
    }

    private void Update(){if(_active)Frames.Add(Time.unscaledDeltaTime*1000f);}

    internal static object End()
    {
        _active=false;
        var allocated=GC.GetAllocatedBytesForCurrentThread()-_allocated;
        var frames=Frames.OrderBy(v=>v).ToArray();
        float Percentile(float p)=>frames.Length==0?0:frames[Math.Min(frames.Length-1,(int)(frames.Length*p))];
        return new {frames=frames.Length,medianMs=Percentile(.5f),p95Ms=Percentile(.95f),p99Ms=Percentile(.99f),
            allocatedBytes=allocated>0?(long?)allocated:null,gen0Collections=GC.CollectionCount(0)-_collections,
            hitMarkers=_hitMarkers,killMarkers=_killMarkers,
            appliedDamage=AppliedDamage.Select(p=>new {target=p.Key.Target,actor=p.Key.Actor,hits=p.Value.Hits,damage=p.Value.Damage}).ToArray(),
            methods=Samples.Select(p=>new {method=p.Key.DeclaringType.Name+"."+p.Key.Name,calls=p.Value.Calls,
                totalMs=p.Value.Ticks*1000d/Stopwatch.Frequency,maxMs=p.Value.MaxTicks*1000d/Stopwatch.Frequency}).ToArray()};
    }

    private static class HurtLedger
    {
        private static void Enter(Health __instance,out float __state)=>__state=_active && __instance?__instance.CurrentHealth:0;
        private static void Leave(Health __instance,DamageInfo damageInfo,bool __result,float __state)
        {
            if(!_active || !__result || !__instance || !damageInfo.fromCharacter)return;
            var key=(__instance.TryGetCharacter().GetInstanceID(),damageInfo.fromCharacter.GetInstanceID());
            AppliedDamage.TryGetValue(key,out var old);
            AppliedDamage[key]=(old.Hits+1,old.Damage+Math.Max(0,__state-__instance.CurrentHealth));
        }
    }

    internal static async UniTask<object> MarkerLifecycle(Assembly mod)
    {
        var fx=mod.GetType("EscapeFromDuckovCoopMod.LocalHitKillFx");
        var find=AccessTools.Method(fx,"FindHitMarkerSingleton");
        var original=(HitMarker)find.Invoke(null,null);
        if(!original)throw new InvalidOperationException("Native hit marker absent");
        AccessTools.Method(fx,"ForgetHitMarker").Invoke(null,new object[]{original});
        var probe=new GameObject("HitMarkerCacheProbe").AddComponent<HitMarker>();
        probe.enabled=false;
        var registered=ReferenceEquals(find.Invoke(null,null),probe);
        UnityEngine.Object.Destroy(probe.gameObject);
        await UniTask.NextFrame();await UniTask.NextFrame();
        var forgotten=AccessTools.Field(fx,"_hitMarker").GetValue(null)==null;
        var restored=ReferenceEquals(find.Invoke(null,null),original);
        var start=Stopwatch.GetTimestamp();
        var stable=true;
        for(var i=0;i<1000;i++)stable&=ReferenceEquals(find.Invoke(null,null),original);
        return new {registered,forgotten,restored,stable,lookup1000Ms=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency};
    }

    internal static async UniTask<object> HitStream(CharacterMainControl target,float seconds,float rate)
    {
        var shooter=CharacterMainControl.Main;
        var before=target.Health.CurrentHealth;
        var start=Time.realtimeSinceStartup;var hits=0;
        while(Time.realtimeSinceStartup-start<seconds)
        {
            var wanted=(int)((Time.realtimeSinceStartup-start)*rate);
            for(var i=0;i<64 && hits<wanted;i++,hits++)
                target.mainDamageReceiver.Hurt(new DamageInfo(shooter){damageValue=5,ignoreArmor=true,ignoreDifficulty=true,
                    damagePoint=target.transform.position+Vector3.up,damageNormal=Vector3.back,critRate=0,bleedChance=0});
            await UniTask.NextFrame();
        }
        return new {hits,seconds=Time.realtimeSinceStartup-start,before,after=target.Health.CurrentHealth};
    }

    internal static async UniTask<object> GunStream(CharacterMainControl target,float seconds)
    {
        var shooter=CharacterMainControl.Main;var gun=shooter.GetGun();
        var start=Time.realtimeSinceStartup;var consumed=0;var previous=gun.GunItemSetting.BulletCount;var frames=0;
        try
        {
            while(Time.realtimeSinceStartup-start<seconds)
            {
                var current=gun.GunItemSetting.BulletCount;
                consumed+=Math.Max(0,previous-current);previous=current;
                var aim=target.mainDamageReceiver.GetComponent<Collider>().bounds.center;
                shooter.SetAimPoint(aim);gun.OverrideShootPoint(aim);
                shooter.Trigger(true,frames++==0,false);
                await UniTask.NextFrame();
            }
        }
        finally{shooter.Trigger(false,false,true);}
        consumed+=Math.Max(0,previous-gun.GunItemSetting.BulletCount);
        return new {consumed,frames,seconds=Time.realtimeSinceStartup-start,triggerMode=gun.GunItemSetting.currentTriggerMode.ToString()};
    }
}
