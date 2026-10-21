using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace CoopRuntimeTests;

internal static class RuntimeWorldInteractions
{
    private static int DoorKey(Assembly mod,global::Door door)=>(int)AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.Door"),"TryGetDoorKey")
        .Invoke(null,new object[]{door,true});
    private static uint HealthId(Assembly mod,HealthSimpleBase health)
    {
        var tag=health.GetComponent(mod.GetType("EscapeFromDuckovCoopMod.NetDestructibleTag"));
        return tag?(uint)AccessTools.Field(tag.GetType(),"id").GetValue(tag):0;
    }
    internal static object Catalog(Assembly mod)=>new
    {
        doors=Object.FindObjectsByType<global::Door>(FindObjectsInactive.Include,FindObjectsSortMode.None)
            .Where(d=>d.gameObject.scene.IsValid()).Select(d=>new {key=DoorKey(mod,d),instance=d.GetInstanceID(),name=d.name,
                scene=d.gameObject.scene.name,active=d.gameObject.activeInHierarchy,open=d.IsOpen,unlocked=d.NoRequireItem,
                position=new[]{d.transform.position.x,d.transform.position.y,d.transform.position.z}}).ToArray(),
        destructibles=Object.FindObjectsByType<HealthSimpleBase>(FindObjectsInactive.Include,FindObjectsSortMode.None)
            .Where(h=>h.gameObject.scene.IsValid()).Select(h=>new {id=HealthId(mod,h),instance=h.GetInstanceID(),name=h.name,
                scene=h.gameObject.scene.name,active=h.gameObject.activeInHierarchy,hp=h.HealthValue,max=h.maxHealthValue,
                nativeSave=h.GetComponent<Breakable>()?(bool?)h.GetComponent<Breakable>().save:null,
                nativeSaveKey=h.GetComponent<Breakable>()?h.GetComponent<Breakable>().SaveKey:null,
                brokenVisual=h.GetComponent<Breakable>()?.breakedVisual?.activeSelf,
                mainCollider=h.GetComponent<Breakable>()?.mainCollider?.activeSelf,
                explosionOnly=h.onlyReceiveExplosion,position=new[]{h.transform.position.x,h.transform.position.y,h.transform.position.z}}).ToArray()
    };
    internal static object Act(Assembly mod,JObject command)
    {
        if((string)command["kind"]=="door")
        {
            var key=(int)command["key"];
            var door=Object.FindObjectsByType<global::Door>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(d=>DoorKey(mod,d)==key);
            if(!door.NoRequireItem)throw new InvalidOperationException("Door requires a native item; do not bypass its gate");
            if((bool)command["open"])door.Open();else door.Close();
            return new {key,open=door.IsOpen};
        }
        var id=(uint)command["id"];
        var health=Object.FindObjectsByType<HealthSimpleBase>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(h=>HealthId(mod,h)==id);
        var before=health.HealthValue;
        var info=new DamageInfo(CharacterMainControl.Main) {damageValue=(float?)command["amount"]??10,
            damageType=DamageTypes.normal,ignoreArmor=true,ignoreDifficulty=true,isExplosion=health.onlyReceiveExplosion || (bool?)command["explosion"]==true,
            damagePoint=health.transform.position,damageNormal=Vector3.up};
        health.dmgReceiver.Hurt(info);
        return new {id,before,after=health?health.HealthValue:0,active=health && health.gameObject.activeInHierarchy};
    }
}
