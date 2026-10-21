using System.Collections;
using System.Reflection;
using Duckov.Sounds;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace CoopRuntimeTests;

internal static class RuntimeHearingProbe
{
    private static SoundVisualization _view;
    private static int _peak;
    private static int Count()
    {
        if (!_view) return 0;
        var pool=AccessTools.Field(typeof(SoundVisualization),"_displayPool").GetValue(_view);
        if (pool==null) return 0;
        var entries=AccessTools.Property(pool.GetType(),"ActiveEntries").GetValue(pool);
        return entries is ICollection collection?collection.Count:((IEnumerable)entries).Cast<object>().Count();
    }
    internal static void SetEnabled(bool enabled)=>CharacterMainControl.Main.CharacterItem.GetStat("SoundVisable".GetHashCode()).BaseValue=enabled?1:0;
    internal static void Begin(){_view=Object.FindObjectsByType<SoundVisualization>(FindObjectsSortMode.None).FirstOrDefault();_peak=Count();}
    internal static void Tick()=>_peak=Math.Max(_peak,Count());
    internal static object Snapshot()=>new {enabled=CharacterMainControl.Main.SoundVisable,peak=_peak,remaining=Count()};

    internal static Dictionary<string,bool> Verify(Assembly mod)
    {
        Begin();
        var tag=Object.FindObjectsByType(mod.GetType("EscapeFromDuckovCoopMod.RemoteAIReplicaTag"),FindObjectsSortMode.None).Cast<Component>()
            .First(t=>t.GetComponentInParent<CharacterMainControl>() is CharacterMainControl c && c && Team.IsEnemy(Teams.player,c.Team) && c.GetGun());
        var source=tag.GetComponentInParent<CharacterMainControl>();
        var type=mod.GetType("EscapeFromDuckovCoopMod.AISoundEventRpc");var message=Activator.CreateInstance(type);
        AccessTools.Field(type,"SourceAiId").SetValue(message,AccessTools.Field(tag.GetType(),"Id").GetValue(tag));
        AccessTools.Field(type,"Team").SetValue(message,source.Team);
        AccessTools.Field(type,"Position").SetValue(message,CharacterMainControl.Main.transform.position+Vector3.forward*80);
        AccessTools.Field(type,"Radius").SetValue(message,2000f);
        AccessTools.Field(type,"SoundType").SetValue(message,SoundTypes.combatSound);
        var heard=0;var retained=true;var stat=CharacterMainControl.Main.CharacterItem.GetStat("SoundVisable".GetHashCode());var old=stat.BaseValue;
        void Hearing(AISound sound){heard++;retained&=sound.fromCharacter==source;}
        AIMainBrain.OnPlayerHearSound+=Hearing;
        var before=Count();
        try
        {
            stat.BaseValue=1;
            for(var i=0;i<100;i++) AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.AIAwarenessSync"),"Client_HandleSound").Invoke(null,new[]{message});
        }
        finally{stat.BaseValue=old;AIMainBrain.OnPlayerHearSound-=Hearing;}
        return new(){["hearing_retains_source"]=heard==100 && retained,["native_hearing_indicator_reused"]=Count()<=before+1};
    }
}
