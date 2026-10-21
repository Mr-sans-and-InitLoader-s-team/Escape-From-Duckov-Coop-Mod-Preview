using System.Collections;
using System.Reflection;
using HarmonyLib;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using UnityEngine;
using Object=UnityEngine.Object;

namespace CoopRuntimeTests;

internal static class RuntimeWorld
{
    private static object Read(object value,string name) => value==null?null:
        AccessTools.Field(value.GetType(),name)?.GetValue(value) ?? AccessTools.Property(value.GetType(),name)?.GetValue(value);
    private static object[] Entries(object registry) => ((IEnumerable)Read(registry,"Entries")).Cast<object>().ToArray();
    internal static object Snapshot(Assembly mod)
    {
        var database=mod.GetType("EscapeFromDuckovCoopMod.CoopSyncDatabase",true);
        object Registry(string name)=>database.GetProperty(name).GetValue(null);
        object[] Tags(string name)=>Resources.FindObjectsOfTypeAll(mod.GetType("EscapeFromDuckovCoopMod."+name,true))
            .Cast<Component>().Where(c=>c && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded)
            .Select(c=>new {instance=c.GetInstanceID(),id=Read(c,"Id"),active=c.gameObject.activeInHierarchy,scene=c.gameObject.scene.name,
                character=c.GetComponentInParent<CharacterMainControl>()?.GetInstanceID()}).ToArray();
        var environment=Registry("Environment");
        return new { ai=Entries(Registry("AI")).Select(e=>new {id=Read(e,"Id"),scene=Read(e,"SceneBuildIndex"),status=Read(e,"Status").ToString(),
                hp=Read(e,"CurrentHealth"),max=Read(e,"MaxHealth"),controller=(Read(e,"Controller") as Component)?.GetInstanceID()}).ToArray(),
            aiReplicas=Tags("RemoteAIReplicaTag"),players=Tags("RemoteReplicaTag"),
            loot=Entries(Registry("Loot")).Select(e=>new {uid=Read(e,"LootUid"),key=Read(e,"PositionKey"),
                live=Read(e,"Inventory") is Inventory inv && inv,
                items=Read(e,"Inventory") is Inventory inventory && inventory?inventory.Select(i=>new {i.TypeID,i.StackCount,
                    tree=ItemTreeData.FromItem(i).entries.Select(t=>new {t.typeID,t.StackCount}).ToArray()}).ToArray():null}).ToArray(),
            drops=Entries(Registry("Drops")).Select(e=>new {id=Read(e,"DropId"),item=(Read(e,"Item") as Item)?.TypeID,
                count=(Read(e,"Item") as Item)?.StackCount}).ToArray(),
            physicalDrops=Resources.FindObjectsOfTypeAll(mod.GetType("EscapeFromDuckovCoopMod.NetDropTag",true)).Cast<Component>()
                .Where(c=>c && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded)
                .Select(c=>new {id=Read(c,"id"),active=c.gameObject.activeInHierarchy,scene=c.gameObject.scene.name,
                    parent=c.transform.parent?c.transform.parent.name:null,
                    item=c.GetComponent<DuckovItemAgent>()?.Item?.TypeID,count=c.GetComponent<DuckovItemAgent>()?.Item?.StackCount}).ToArray(),
            doors=Entries(Read(environment,"Doors")).Length,destructibles=Entries(Read(environment,"Destructibles")).Length,
            barrels=Entries(Read(environment,"ExplosiveOilBarrels")).Length,
            pendingBlends=Resources.FindObjectsOfTypeAll(mod.GetType("EscapeFromDuckovCoopMod.PendingMagicBlend",true)).Length,
            managedBytes=GC.GetTotalMemory(false),nativeBytes=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() };
    }
}
