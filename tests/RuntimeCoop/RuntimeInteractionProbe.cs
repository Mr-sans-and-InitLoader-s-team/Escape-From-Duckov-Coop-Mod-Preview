using System.Collections;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.Utilities;
using HarmonyLib;
using ItemStatsSystem;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;
using Object=UnityEngine.Object;

namespace CoopRuntimeTests;

internal static class RuntimeInteractionProbe
{
    private static readonly List<object> PutCalls=new();
    private static bool _putTrace;
    private static void TracePut(Item item,int preferPos)=>PutCalls.Add(new {type=item?item.TypeID:0,count=item?item.StackCount:0,preferPos,trace=Environment.StackTrace});
    private static object Read(object value,string field)=>value==null?null:
        AccessTools.Field(value.GetType(),field)?.GetValue(value) ?? AccessTools.Property(value.GetType(),field)?.GetValue(value);
    private static string Path(Transform t)=>t.parent?Path(t.parent)+"/"+t.name:t.name;
    private static object Events(UnityEventBase e)=>Enumerable.Range(0,e.GetPersistentEventCount()).Select(i=>new {
        method=e.GetPersistentMethodName(i),type=e.GetPersistentTarget(i)?.GetType().Name,
        path=e.GetPersistentTarget(i) is Component c?Path(c.transform):e.GetPersistentTarget(i)?.name}).ToArray();
    internal static object Catalog()=>Object.FindObjectsByType<InteractableBase>(FindObjectsInactive.Include,FindObjectsSortMode.None)
        .Where(i=>i.gameObject.scene.IsValid()).Select(i=>new {instance=i.GetInstanceID(),key=AccessTools.Method(typeof(InteractableBase),"GetKey").Invoke(i,null),
            i.name,path=Path(i.transform),scene=i.gameObject.scene.name,active=i.gameObject.activeInHierarchy,i.enabled,
            i.interactableGroup,i.requireItem,i.requireItemId,i.disableOnFinish,i.finishWhenTimeOut,time=i.InteractTime,
            label=i._overrideInteractNameKey,position=new[]{i.transform.position.x,i.transform.position.y,i.transform.position.z},
            start=Events(i.OnInteractStartEvent),finish=Events(i.OnInteractFinishedEvent),timeout=Events(i.OnInteractTimeoutEvent),required=Events(i.OnRequiredItemUsedEvent)}).ToArray();
    private static IEnumerable<object> Entries(Assembly mod)=>(IEnumerable<object>)((IEnumerable)Read(mod.GetType("EscapeFromDuckovCoopMod.CoopSyncDatabase").GetProperty("Loot").GetValue(null),"Entries")).Cast<object>();
    internal static object Loot(Assembly mod,int? uid,int? boxInstance=null)=>Entries(mod).Where(e=>(!uid.HasValue || (int)Read(e,"LootUid")==uid) &&
        (!boxInstance.HasValue || Read(e,"Lootbox") is InteractableLootbox b && b && b.GetInstanceID()==boxInstance) && Read(e,"Inventory") is Inventory inv && inv).Select(e=>{
        var inv=(Inventory)Read(e,"Inventory");var box=(InteractableLootbox)Read(e,"Lootbox");
        return new {uid=Read(e,"LootUid"),key=Read(e,"PositionKey"),inventory=inv.GetInstanceID(),box=box?box.GetInstanceID():0,
            path=box?Path(box.transform):null,active=box && box.gameObject.activeInHierarchy,enabled=box && box.enabled,
            boxNeedInspect=box?(bool?)box.needInspect:null,inv.NeedInspection,inv.hasBeenInspectedInLootBox,inv.Loading,inv.Capacity,
            interacting=box && box.Interacting,lootState=Read(box,"lootState")?.ToString(),
            position=box?new[]{box.transform.position.x,box.transform.position.y,box.transform.position.z}:null,
            items=inv.Select(i=>new {instance=i.GetInstanceID(),type=i.TypeID,count=i.StackCount,max=i.MaxStackCount,i.Inspected,i.Inspecting}).ToArray()};}).ToArray();
    internal static object Construction(JObject command)=>Object.FindObjectsByType<ConstructionSite>(FindObjectsInactive.Include,FindObjectsSortMode.None)
        .Where(s=>Path(s.transform).Contains((string)command["path"]??"SingleDirectionDoor")).Select(s=>{
            if((bool?)command["reset"]==true){AccessTools.Field(typeof(ConstructionSite),"wasBuilt").SetValue(s,false);s.RefreshGameObjects();}
            var taker=(CostTaker)Read(s,"costTaker");
            return new {instance=s.GetInstanceID(),path=Path(s.transform),built=Read(s,"wasBuilt"),costTaker=taker.GetInstanceID(),
                cost=taker.Cost,active=taker.gameObject.activeInHierarchy,enough=taker.Cost.Enough};}).ToArray();
    internal static object Act(Assembly mod,JObject command)
    {
        if((bool?)command["close"]==true){Duckov.UI.LootView.Instance?.Close();CharacterMainControl.Main.interactAction.StopAction();return "closed";}
        if((bool?)command["pickAll"]==true){AccessTools.Method(typeof(Duckov.UI.LootView),"OnPickAllButtonClicked").Invoke(Duckov.UI.LootView.Instance,null);return "native_pick_all";}
        InteractableBase target;
        if(command["uid"]!=null)
            target=(InteractableLootbox)Read(Entries(mod).Single(e=>(int)Read(e,"LootUid")== (int)command["uid"]),"Lootbox");
        else target=Object.FindObjectsByType<InteractableBase>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(i=>i.GetInstanceID()==(int)command["instance"]);
        if((bool?)command["stop"]==true){target.StopInteract();return "stopped";}
        if((bool?)command["finish"]==true)target.FinishInteract(CharacterMainControl.Main);
        else CharacterMainControl.Main.Interact(target);
        return new {instance=target.GetInstanceID(),target.Interacting,allowed=target.CheckInteractable(),
            lootOpen=Duckov.UI.LootView.Instance && Duckov.UI.LootView.Instance.open};
    }

    internal static async UniTask<object> Put(Assembly mod,JObject command)
    {
        if(!_putTrace)
        {
            new Harmony("local.coop.search-insert-trace").Patch(AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.LootNet"),"Client_SendLootPutRequest"),
                prefix:new HarmonyMethod(typeof(RuntimeInteractionProbe),nameof(TracePut)));
            _putTrace=true;
        }
        PutCalls.Clear();
        var entry=Entries(mod).Single(e=>command["instance"]!=null
            ? Read(e,"Lootbox") is InteractableLootbox b && b && b.GetInstanceID()==(int)command["instance"]
            : (int)Read(e,"LootUid")== (int)command["uid"]);
        var inv=(Inventory)Read(entry,"Inventory");
        var box=(InteractableLootbox)Read(entry,"Lootbox");
        if((bool?)command["holdSearch"]==true)
        {
            AccessTools.Field(typeof(InteractableLootbox),"inspectTime").SetValue(box,120f);
            AccessTools.Field(typeof(InteractableLootbox),"inspectTimer").SetValue(box,0f);
        }
        if(command["type"]==null)return Loot(mod,null,box.GetInstanceID());
        if((bool?)command["gap"]==true)
        {
            var net=AccessTools.Field(mod.GetType("EscapeFromDuckovCoopMod.COOPManager"),"LootNet").GetValue(null);
            ((IDictionary)Read(net,"_cliVersions"))[inv]=-1000;
        }
        var item=await ItemAssetsCollection.InstantiateAsync((int)command["type"]);
        item.Inspected=true;
        item.StackCount=(int?)command["count"]==-1?item.MaxStackCount-1:(int?)command["count"]??1;
        CharacterMainControl.Main.CharacterItem.Inventory.AddItem(item);
        item.Detach();
        var method=(string)command["method"]??"add";
        var slot=(int?)command["slot"]??Enumerable.Range(0,inv.Capacity).First(i=>inv.GetItemAt(i)==null);
        bool result=method=="at"?inv.AddAt(item,slot):method=="merge"?inv.AddAndMerge(item):inv.AddItem(item);
        await UniTask.Delay(600);
        return new {result,method,calls=PutCalls.ToArray(),items=Loot(mod,null,box.GetInstanceID())};
    }
}
