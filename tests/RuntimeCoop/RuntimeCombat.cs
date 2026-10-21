using System.Collections;
using System.Reflection;
using Duckov.Utilities;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CoopRuntimeTests;

internal static class RuntimeCombat
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;

    internal static object Trace(Projectile projectile,CharacterMainControl shooter,Assembly mod)
    {
        if(!projectile)return null;
        var context=(ProjectileContext)AccessTools.Field(typeof(Projectile),"context").GetValue(projectile);
        var mask=(LayerMask)AccessTools.Field(typeof(Projectile),"hitLayers").GetValue(projectile);
        var radius=(float)AccessTools.Field(typeof(Projectile),"radius").GetValue(projectile);
        return new {context.team,context.damage,context.firstFrameCheckStartPoint,context.direction,radius,
            fake=AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.FakeProjectileRegistry"),"IsFake").Invoke(null,new object[]{projectile}),
            hits=Physics.SphereCastAll(context.firstFrameCheckStartPoint,radius,context.direction,10,mask,QueryTriggerInteraction.Ignore)
                .Where(h=>h.collider.GetComponentInParent<CharacterMainControl>()!=shooter).OrderBy(h=>h.distance)
                .Select(h=>new {h.collider.name,h.distance,character=h.collider.GetComponentInParent<CharacterMainControl>()?.GetInstanceID(),
                    team=h.collider.GetComponent<DamageReceiver>()?.Team.ToString()}).ToArray()};
    }

    internal static CharacterMainControl RemotePlayer(object service,string playerId=null)
    {
        var server=(bool)AccessTools.Property(service.GetType(),"IsServer").GetValue(service);
        var remotes=(IDictionary)AccessTools.Field(service.GetType(),server?"remoteCharacters":"clientRemoteCharacters").GetValue(service);
        foreach(DictionaryEntry entry in remotes)
        {
            if(entry.Value is not GameObject go || !go)continue;
            var character=go.GetComponent<CharacterMainControl>();
            var args=new object[]{character,null};
            service.GetType().GetMethod("TryGetPlayerId").Invoke(service,args);
            if(playerId==null || (string)args[1]==playerId)return character;
        }
        throw new InvalidOperationException("Remote player absent: "+playerId);
    }

    internal static object AiAttack(JObject command,CharacterMainControl attacker,CharacterMainControl victim)
    {
        var damage=new DamageInfo(attacker)
        {
            damageValue=(float?)command["amount"] ?? 10, ignoreArmor=(bool?)command["ignoreArmor"] ?? false,
            ignoreDifficulty=(bool?)command["ignoreDifficulty"] ?? false,
            critRate=(float?)command["critRate"] ?? 0, critDamageFactor=(float?)command["critFactor"] ?? 2,
            armorPiercing=(float?)command["piercing"] ?? 0, damageType=(DamageTypes)((int?)command["damageType"] ?? 0),
            damagePoint=victim.transform.position+Vector3.up*.8f, damageNormal=Vector3.forward,
            isExplosion=(bool?)command["explosion"] ?? false
        };
        if(command["element"]!=null)damage.AddElementFactor((ElementTypes)(int)command["element"],1);
        var before=victim.Health.CurrentHealth;
        victim.mainDamageReceiver.Hurt(damage);
        return new { before,after=victim.Health.CurrentHealth,attacker=attacker.name,receiver=victim.name };
    }

    internal static int PauseBrain(CharacterMainControl character,bool pause)
    {
        var calls=0;
        foreach(var component in character.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if(!component || component.GetType().Namespace?.StartsWith("NodeCanvas")!=true)continue;
            var method=component.GetType().GetMethods(Flags).FirstOrDefault(m=>m.Name==(pause?"PauseBehaviour":"StartBehaviour") &&
                m.GetParameters().All(p=>p.IsOptional));
            if(method==null)continue;
            method.Invoke(component,method.GetParameters().Select(p=>p.DefaultValue).ToArray());
            calls++;
        }
        if(pause)
        {
            character.GetComponentInChildren<AICharacterController>(true)?.StopMove();
            character.SetMoveInput(Vector3.zero);
            character.GetGun()?.SetTrigger(false,false,true);
        }
        return calls;
    }

    internal static object FakeExplosion(Assembly mod)
    {
        var registry=mod.GetType("EscapeFromDuckovCoopMod.FakeProjectileRegistry",true);
        var fakes=(IEnumerable<Projectile>)AccessTools.Field(registry,"_fakes").GetValue(null);
        var old=fakes.ToHashSet();
        var handle=AccessTools.Field(mod.GetType("EscapeFromDuckovCoopMod.COOPManager",true),"WeaponHandle").GetValue(null);
        var main=CharacterMainControl.Main;
        var origin=main.transform.position+Vector3.up*.8f;
        var context=new ProjectileContext { team=Teams.scav,direction=Vector3.down,speed=600,distance=2,
            damage=10,explosionDamage=10,explosionRange=3,element_Physics=1,
            damageFactorToZombie=1,critDamageFactor=1,bleedChance=1 };
        object boxedContext=context;
        AccessTools.Field(typeof(ProjectileContext),"ignoreDifficulty")?.SetValue(boxedContext,true);
        context=(ProjectileContext)boxedContext;
        var before=main.Health.CurrentHealth;
        AccessTools.Method(handle.GetType(),"SpawnVisualProjectile").Invoke(handle,new object[]{
            "VisualProbe",0,origin,Vector3.down,600f,2f,true,context,null,null,0,true});
        var projectile=fakes.First(p=>p && !old.Contains(p));
        var captured=(ProjectileContext)AccessTools.Field(typeof(Projectile),"context").GetValue(projectile);
        AccessTools.Method(typeof(Projectile),"Update").Invoke(projectile,null);
        return new { before,after=main.Health.CurrentHealth,contextDamage=captured.damage,
            contextExplosion=captured.explosionDamage,contextBleed=captured.bleedChance };
    }
}
