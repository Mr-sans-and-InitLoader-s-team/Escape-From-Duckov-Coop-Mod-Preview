using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov;
using Duckov.Economy;
using Duckov.Buildings;
using Duckov.Quests;
using ItemStatsSystem;
using Duckov.Utilities;
using Duckov.Scenes;
using Duckvo.Beacons;
using Duckov.NoteIndexs;
using Duckov.MasterKeys;
using Duckov.PerkTrees;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Saves;

namespace CoopRuntimeTests;

internal static class RuntimeSharedHomeProbe
{
    private static bool _identityPatched;
    internal static void PrepareIdentity(Assembly mod)
    {
        if(_identityPatched)return;
        new Harmony("local.coop.runtime-tests.shared-identity").Patch(AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.SharedHomeLoadouts"),"LocalIdentity"),
            prefix:new HarmonyMethod(typeof(Runner),"TestSharedIdentity"));
        _identityPatched=true;
    }
    private static string _compatibilityCase;
    private static bool _compatibilityPatched;
    private static bool _holdStorage;
    private static bool TestEnsureStorage() => !_holdStorage;
    private static void TestManifest(object __result)
    {
        if (_compatibilityCase == null) return;
        var type = __result.GetType();
        var mods = (System.Collections.IList)type.GetField("Mods").GetValue(__result);
        var entryType = mods.GetType().GetGenericArguments()[0];
        var entry = Activator.CreateInstance(entryType, true);
        entryType.GetField("Name").SetValue(entry, "SharedValidationFixture");
        entryType.GetField("Version").SetValue(entry, _compatibilityCase == "version" ? "2" : "1");
        entryType.GetField("Hash").SetValue(entry, new string(_compatibilityCase == "hash" ? 'b' : 'a', 64));
        entryType.GetField("Unreadable").SetValue(entry, _compatibilityCase == "unreadable");
        if (_compatibilityCase != "missing") mods.Add(entry);
        if (_compatibilityCase == "extra")
        {
            var extra = Activator.CreateInstance(entryType, true);
            entryType.GetField("Name").SetValue(extra, "ExtraValidationFixture");
            entryType.GetField("Version").SetValue(extra, "1");
            entryType.GetField("Hash").SetValue(extra, "");
            mods.Add(extra);
        }
        if (_compatibilityCase == "game" || _compatibilityCase == "coop")
        {
            var field = type.GetField(_compatibilityCase == "game" ? "Game" : "Coop");
            field.SetValue(__result, (string)field.GetValue(__result) + "-test");
        }
    }
    private static bool TestBeginCompatibility(MethodBase __originalMethod)
    {
        if (_compatibilityCase != "timeout") return true;
        AccessTools.Field(__originalMethod.DeclaringType,"_clientDeadline").SetValue(null,UnityEngine.Time.unscaledTime+60f);
        return false;
    }
    internal static async UniTask<object> Run(Assembly mod, JObject command)
    {
        var type=mod.GetType("EscapeFromDuckovCoopMod.SharedHome");
        var compatibility = mod.GetType("EscapeFromDuckovCoopMod.SharedHomeCompatibility");
        if (!_compatibilityPatched)
        {
            var harmony = new Harmony("local.coop.runtime-tests.shared-validation");
            harmony.Patch(AccessTools.Method(compatibility,"Capture"), postfix: new HarmonyMethod(typeof(RuntimeSharedHomeProbe),nameof(TestManifest)));
            harmony.Patch(AccessTools.Method(compatibility,"BeginClient"), prefix: new HarmonyMethod(typeof(RuntimeSharedHomeProbe),nameof(TestBeginCompatibility)));
            harmony.Patch(AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.SharedHomeStorage"),"Ensure"),
                prefix: new HarmonyMethod(typeof(RuntimeSharedHomeProbe),nameof(TestEnsureStorage)));
            _compatibilityPatched = true;
        }
        if (command["compatibilityCase"] != null) _compatibilityCase = (string)command["compatibilityCase"];
        if (command["holdStorage"] != null) _holdStorage = (bool)command["holdStorage"];
        if (command["delayCapture"] != null) AccessTools.Field(type,"_nextUpdate").SetValue(null,UnityEngine.Time.unscaledTime+(float)command["delayCapture"]);
        if ((bool?)command["earlyStorageSnapshot"] == true)
        {
            var service=mod.GetType("EscapeFromDuckovCoopMod.NetService").GetField("Instance").GetValue(null);
            var manager=AccessTools.Field(service.GetType(),"netManager").GetValue(service);
            var loot=AccessTools.Field(mod.GetType("EscapeFromDuckovCoopMod.COOPManager"),"LootNet").GetValue(null);
            foreach(var peer in (System.Collections.IEnumerable)AccessTools.Property(manager.GetType(),"ConnectedPeerList").GetValue(manager))
                AccessTools.Method(loot.GetType(),"Server_SendLootboxState").Invoke(loot,new object[]{peer,PlayerStorage.Inventory});
        }
        if ((bool?)command["compatibilityNative"] == true) _compatibilityCase = null;
        if ((bool?)command["compatibilityClear"] == true)
        {
            var close = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(UnityEngine.FindObjectsInactive.Include,UnityEngine.FindObjectsSortMode.None)
                .FirstOrDefault(b => b.name == "CloseError");
            if (close) close.onClick.Invoke();
        }
        if ((bool?)command["compatibilityTimeout"] == true)
        {
            var waiting = (System.Collections.IDictionary)AccessTools.Field(compatibility,"Waiting").GetValue(null);
            foreach (var pending in waiting.Values) AccessTools.Field(pending.GetType(),"Deadline").SetValue(pending,0f);
        }
        if ((bool?)command["compatibilityState"] == true)
        {
            var manifest = AccessTools.Method(compatibility,"Capture").Invoke(null,null);
            return new { manifest=JObject.FromObject(manifest),
                ready=AccessTools.Property(compatibility,"ClientReady").GetValue(null),
                active=type.GetProperty("Active").GetValue(null),
                overlay=AccessTools.Property(mod.GetType("EscapeFromDuckovCoopMod.SharedHomeSave"),"OverlayActive").GetValue(null),
                details=AccessTools.Property(mod.GetType("EscapeFromDuckovCoopMod.ConnectionErrorDialog"),"LastDetails").GetValue(null) };
        }
        PrepareIdentity(mod);
        if(command["enabled"]!=null)type.GetMethod("SetEnabled").Invoke(null,new object[]{(bool)command["enabled"]});
        if((bool?)command["perkCandidate"]==true)
        {
            foreach(var tree in PerkTreeManager.Instance.perkTrees.Where(t=>t))
                foreach(var perk in tree.Perks.Where(p=>p && p.Unlocked))
                    foreach(var effect in perk.GetComponents<Duckov.PerkTrees.Behaviours.ModifyCharacterStatsBase>())
                        if(((System.Collections.IEnumerable)AccessTools.Field(effect.GetType(),"entries").GetValue(effect)).Cast<object>()
                            .Any(e=>(string)AccessTools.Field(e.GetType(),"key").GetValue(e)=="InventoryCapacity"))
                            return new{tree=tree.ID,perk=perk.name};
            return null;
        }
        if(command["perkAdjust"]!=null)
        {
            var tree=PerkTreeManager.GetPerkTree((string)command["tree"]);
            var perk=tree.Perks.Single(p=>p.name==(string)command["perkAdjust"]);
            var setter=AccessTools.PropertySetter(typeof(Perk),nameof(Perk.Unlocked));
            AccessTools.Method(type,"Apply").Invoke(null,new object[]{(Action)(()=>setter.Invoke(perk,new object[]{(bool)command["unlocked"]}))});
            type.GetMethod("MarkDirty").Invoke(null,null);
        }
        if((bool?)command["killFixture"]==true)
        {
            var prefab=GameplayDataSettings.QuestCollection.First(q=>q.ID>0 && q.Tasks.Any(t=>t is Duckov.Quests.Tasks.QuestTask_KillCount));
            var manager=QuestManager.Instance;
            foreach(var old in manager.ActiveQuests.Concat(manager.HistoryQuests).Where(q=>q.ID==prefab.ID).ToArray())
            {manager.ActiveQuests.Remove(old);manager.HistoryQuests.Remove(old);old.gameObject.SetActive(false);UnityEngine.Object.Destroy(old.gameObject);}
            ((List<int>)AccessTools.Field(typeof(QuestManager),"completedQuests").GetValue(manager)).Remove(prefab.ID);
            manager.ActivateQuest(prefab.ID);
            var task=manager.ActiveQuests.Single(q=>q.ID==prefab.ID).Tasks.OfType<Duckov.Quests.Tasks.QuestTask_KillCount>().First();
            foreach(var field in new[]{"withWeapon","requireBuff","requireHeadShot","withoutHeadShot"})AccessTools.Field(task.GetType(),field).SetValue(task,false);
            AccessTools.Field(task.GetType(),"requireEnemyType").SetValue(task,null);
            AccessTools.Field(task.GetType(),"requireSceneID").SetValue(task,"");
            AccessTools.Field(task.GetType(),"requireAmount").SetValue(task,100000);
            ((System.Collections.IList)AccessTools.Field(task.GetType(),"requireTags").GetValue(task)).Clear();
            task.SetupSaveData(0);
            type.GetMethod("MarkDirty").Invoke(null,null);
            return new{quest=prefab.ID,task=task.ID};
        }
        if((bool?)command["shopCatalog"]==true)
            return UnityEngine.Object.FindObjectsByType<StockShop>(UnityEngine.FindObjectsInactive.Include,UnityEngine.FindObjectsSortMode.None)
                .Select(shop=>new {shop.MerchantID,entries=shop.entries.Where(e=>e.Show && e.CurrentStock>0 && shop.GetItemInstanceDirect(e.ItemTypeID))
                    .Select(e=>new{id=e.ItemTypeID,stock=e.CurrentStock,price=shop.ConvertPrice(shop.GetItemInstanceDirect(e.ItemTypeID)),amount=shop.GetItemInstanceDirect(e.ItemTypeID).StackCount}).ToArray()}).ToArray();
        if(command["shopBuy"]!=null)
        {
            var shop=UnityEngine.Object.FindObjectsByType<StockShop>(UnityEngine.FindObjectsInactive.Include,UnityEngine.FindObjectsSortMode.None).First(s=>s.MerchantID==(string)command["shopBuy"]);
            await shop.Buy((int)command["id"],1);
        }
        if((bool?)command["craftCatalog"]==true)
            return CraftingFormulaCollection.Instance.Entries.Where(f=>CraftingManager.UnlockedFormulaIDs.Contains(f.id) && f.cost.Enough)
                .Select(f=>new {f.id,result=f.result.id,amount=f.result.amount,cost=f.cost}).ToArray();
        if(command["craft"]!=null)await CraftingManager.Instance.Craft((string)command["craft"]);
        if((bool?)command["displayCatalog"]==true)
            return UnityEngine.Object.FindObjectsByType<Showcase>(UnityEngine.FindObjectsInactive.Include,UnityEngine.FindObjectsSortMode.None)
                .Select(d=>new{d.ID,inventory=ItemStatsSystem.Data.ItemTreeData.FromItem(d.Item).entries.Select(e=>new{type=e.typeID,count=e.StackCount}).ToArray()}).ToArray();
        if(command["displaySeed"]!=null)
        {
            var display=UnityEngine.Object.FindObjectsByType<Showcase>(UnityEngine.FindObjectsInactive.Include,UnityEngine.FindObjectsSortMode.None).Single(d=>d.ID==(string)command["displaySeed"]);
            var item=await ItemAssetsCollection.InstantiateAsync((int)command["id"]);
            var inserted=display.Item.TryPlug(item,true) || display.Inventory && display.Inventory.AddAndMerge(item);
            if(!inserted)item.DestroyTree();
            return new{inserted};
        }
        if((bool?)command["removeNegativeQuests"]==true)
        {
            var manager=QuestManager.Instance;
            foreach(var old in manager.ActiveQuests.Concat(manager.HistoryQuests).Where(q=>q.ID<0).ToArray())
            {manager.ActiveQuests.Remove(old);manager.HistoryQuests.Remove(old);old.gameObject.SetActive(false);UnityEngine.Object.Destroy(old.gameObject);}
            ((List<int>)AccessTools.Field(typeof(QuestManager),"completedQuests").GetValue(manager)).RemoveAll(id=>id<0);
        }
        if(command["resetQuest"]!=null)
        {
            var id=(int)command["resetQuest"];
            var manager=QuestManager.Instance;
            foreach(var quest in manager.ActiveQuests.Concat(manager.HistoryQuests).Where(q=>q.ID==id).ToArray())
            {manager.ActiveQuests.Remove(quest);manager.HistoryQuests.Remove(quest);quest.gameObject.SetActive(false);UnityEngine.Object.Destroy(quest.gameObject);}
            ((List<int>)AccessTools.Field(typeof(QuestManager),"completedQuests").GetValue(manager)).Remove(id);
            manager.ActivateQuest(id);
        }
        if((bool?)command["personalLayout"]==true)
        {
            var area=BuildingManager.Instance.Areas.First(a=>a.Buildings.Count>0);
            area.Buildings.RemoveAt(area.Buildings.Count-1);
        }
        if(command["visited"]!=null)MultiSceneCore.SetVisited((string)command["visited"]);
        if(command["visitedFalse"]!=null)SavesSystem.Save("MultiSceneCore_Visited_"+(string)command["visitedFalse"],false);
        if(command["beacon"]!=null)BeaconManager.UnlockBeacon((string)command["beacon"],(int?)command["beaconIndex"]??0);
        if(command["formula"]!=null)CraftingManager.UnlockFormula((string)command["formula"]);
        if(command["note"]!=null)NoteIndex.SetNoteUnlocked((string)command["note"]);
        if(command["worldBool"]!=null)CommonVariables.SetBool((string)command["worldBool"],true);
        if(command["key"]!=null)AccessTools.Method(typeof(MasterKeysManager),"Activate").Invoke(null,new object[]{(int)command["key"]});
        if((bool?)command["unlockCatalog"]==true)return new {
            formulas=CraftingFormulaCollection.Instance.Entries.Where(f=>!f.unlockByDefault).Select(f=>f.id).ToArray(),
            notes=NoteIndex.Instance.Notes.Select(n=>n.key).ToArray(),keys=MasterKeysManager.AllPossibleKeys};
        if(command["xp"]!=null)EXPManager.AddExp((int)command["xp"]);
        if((bool?)command["save"]==true){SavesSystem.CollectSaveData();SavesSystem.SaveFile(false);}
        if(command["activate"]!=null)QuestManager.Instance.ActivateQuest((int)command["activate"]);
        if((bool?)command["questCatalog"]==true)return GameplayDataSettings.QuestCollection.Select(q=>new{id=q.ID,
            tasks=q.Tasks.Select(t=>new{id=t.ID,type=t.GetType().Name}).ToArray()}).ToArray();
        if(command["task"]!=null)
        {
            var quest=QuestManager.Instance.ActiveQuests.Single(q=>q.ID==(int)command["quest"]);
            var task=quest.Tasks.Single(t=>t.ID==(int)command["task"]);
            var data=task.GenerateSaveData();
            if(data is int i)task.SetupSaveData(i+(int)command["delta"]);
            else if(data is bool)task.SetupSaveData(true);
            AccessTools.Method(typeof(Duckov.Quests.Task),"ReportStatusChanged").Invoke(task,null);
        }
        if(command["complete"]!=null)
            QuestManager.Instance.ActiveQuests.Single(q=>q.ID==(int)command["complete"]).TryComplete();
        if(command["removeFixtureBuilding"]!=null)
        {
            foreach(var area in BuildingManager.Instance.Areas)
                foreach(var b in area.Buildings.Where(b=>b.ID==(string)command["removeFixtureBuilding"]).ToArray())
                    AccessTools.Method(typeof(BuildingManager),"DestroyBuilding").Invoke(null,new object[]{b.GUID,null});
        }
        if(command["build"]!=null)
        {
            AccessTools.Method(typeof(BuildingManager),"BuyAndPlace").Invoke(null,new object[]{(string)command["area"],
                (string)command["build"],new UnityEngine.Vector2Int((int)command["x"],(int)command["y"]),BuildingRotation.Zero});
        }
        if(command["return"]!=null)
        {
            var beforeReturn=AccessTools.Field(typeof(BuildingManager),"returningBuilding").GetValue(null);
            var result=await (UniTask<bool>)AccessTools.Method(typeof(BuildingManager),"ReturnBuilding").Invoke(null,
                new object[]{(int)command["return"],(string)command["area"]});
            return new{result,beforeReturn,afterReturn=AccessTools.Field(typeof(BuildingManager),"returningBuilding").GetValue(null)};
        }
        if((bool?)command["catalog"]==true)return BuildingDataCollection.Instance.Infos.Select(b=>new {id=b.id,
            b.maxAmount,dimensions=new[]{b.Dimensions.x,b.Dimensions.y},cost=b.cost,token=b.TokenAmount,
            allowed=AccessTools.Method(typeof(BuildingInfo),"RequirementsSatisfied").Invoke(b,null)}).ToArray();
        if(command["grantToken"]!=null)
            AccessTools.Method(typeof(BuildingManager),"AddToken").Invoke(BuildingManager.Instance,new object[]{(string)command["grantToken"],1});
        if((bool?)command["placement"]==true)
        {
            var valid=mod.GetType("EscapeFromDuckovCoopMod.SharedHomeBuildings").GetMethod("ValidPlacement",BindingFlags.NonPublic|BindingFlags.Static);
            foreach(var area in BuildingManager.Instance.Areas)
                for(var x=-25;x<10;x++)for(var y=-90;y<-40;y++)
                    if((bool)valid.Invoke(null,new object[]{area.AreaID,(string)command["building"],x,y,0}))return new{area=area.AreaID,x,y};
            return null;
        }
        if((bool?)command["flushLoadout"]==true)
            AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.SharedHomeLoadouts"),"Flush").Invoke(null,null);
        if((bool?)command["loadoutAudit"]==true)
        {
            return new[]{"client","guest3","guest4"}.Select(role=>{
                var key=(string)AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.SharedHomeLoadouts"),"Key").Invoke(null,new object[]{"private-test-"+role});
                var bytes=SavesSystem.Load<byte[]>(key);
                var file=bytes==null?null:new ES3File(bytes);
                var data=file?.Load<ItemStatsSystem.Data.ItemTreeData>("character");
                return new{role,bytes=bytes?.Length,inventory=data?.entries.Select(e=>new{type=e.typeID,count=e.StackCount}).ToArray()};
            }).ToArray();
        }
        if(command["bufferTake"]!=null)await PlayerStorage.TakeBufferItem((int)command["bufferTake"]);
        if(command["bufferPush"]!=null)
        {
            var item=CharacterMainControl.Main.CharacterItem.Inventory.First(i=>i && i.TypeID==(int)command["bufferPush"]);
            PlayerStorage.Push(item,true);
        }
        if(command["storageSeed"]!=null)
        {
            var item=await ItemAssetsCollection.InstantiateAsync((int)command["storageSeed"]);
            item.StackCount=(int?)command["count"]??1;
            if((bool?)command["bufferSeed"]==true)PlayerStorage.Push(item,true);
            else PlayerStorage.Inventory.AddAt(item,PlayerStorage.Inventory.GetFirstEmptyPosition());
        }
        if((bool?)command["openStorage"]==true)
            AccessTools.Method(typeof(InteractableLootbox),"StartLoot").Invoke(PlayerStorage.Instance.InteractableLootBox,null);
        if(command["takeStorage"]!=null)
        {
            var item=PlayerStorage.Inventory.GetItemAt((int)command["takeStorage"]);
            ItemUtilities.SendToPlayerCharacter(item);
        }
        if((bool?)command["claim"]==true)
        {
            var quest=QuestManager.Instance.HistoryQuests.Single(q=>q.ID==(int)command["quest"]);
            foreach(var reward in quest.Rewards)reward.Claim();
        }
        if(command["splitStorage"]!=null)
        {
            var item=await PlayerStorage.Inventory.GetItemAt((int)command["splitStorage"]).Split((int)command["count"]);
            if(item)ItemUtilities.SendToPlayerCharacter(item);
        }
        if((bool?)command["uiScreenshot"]==true)
        {
            var uiType=mod.GetType("EscapeFromDuckovCoopMod.MModUI");
            var ui=(UnityEngine.MonoBehaviour)AccessTools.Field(uiType,"Instance").GetValue(null);
            var components=AccessTools.Field(uiType,"_components").GetValue(ui);
            var panel=(UnityEngine.GameObject)AccessTools.Field(components.GetType(),"MainPanel").GetValue(components);
            AccessTools.Field(uiType,"showUI").SetValue(ui,true);
            ui.StartCoroutine((System.Collections.IEnumerator)AccessTools.Method(uiType,"AnimatePanel").Invoke(ui,new object[]{panel,true}));
            await UniTask.Delay(600);
            UnityEngine.Canvas.ForceUpdateCanvases();
            var card=panel.GetComponentsInChildren<UnityEngine.RectTransform>(true).Single(r=>r.name=="ModeCard");
            var button=card.GetComponentsInChildren<UnityEngine.RectTransform>(true).Single(r=>r.name=="ToggleMode");
            var bounds=UnityEngine.RectTransformUtility.CalculateRelativeRectTransformBounds(card,button);
            var path=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(UnityEngine.Application.persistentDataPath),
                command["screenshotName"]==null?"sharing-ui.png":System.IO.Path.GetFileName((string)command["screenshotName"]));
            if(System.IO.File.Exists(path))System.IO.File.Delete(path);
            var canvas=panel.GetComponentInParent<UnityEngine.Canvas>();
            var cameraObject=new UnityEngine.GameObject("RuntimeUICapture");
            var camera=cameraObject.AddComponent<UnityEngine.Camera>();
            camera.enabled=false;
            camera.clearFlags=UnityEngine.CameraClearFlags.SolidColor;
            camera.backgroundColor=new UnityEngine.Color(.12f,.16f,.2f,1);
            camera.cullingMask=1<<31;
            camera.transform.position=new UnityEngine.Vector3(0,0,-10000);
            var target=UnityEngine.RenderTexture.GetTemporary(1920,1080,24);
            var texture=new UnityEngine.Texture2D(1920,1080,UnityEngine.TextureFormat.RGBA32,false);
            var oldMode=canvas.renderMode;var oldCamera=canvas.worldCamera;var oldDistance=canvas.planeDistance;
            var oldTarget=UnityEngine.RenderTexture.active;
            var children=canvas.GetComponentsInChildren<UnityEngine.Transform>(true);
            var layers=children.Select(t=>t.gameObject.layer).ToArray();
            try
            {
                foreach(var child in children)child.gameObject.layer=31;
                camera.targetTexture=target;
                canvas.renderMode=UnityEngine.RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
                UnityEngine.Canvas.ForceUpdateCanvases();
                await UniTask.NextFrame();
                camera.Render();
                UnityEngine.RenderTexture.active=target;
                texture.ReadPixels(new UnityEngine.Rect(0,0,1920,1080),0,0);
                texture.Apply();
                System.IO.File.WriteAllBytes(path,UnityEngine.ImageConversion.EncodeToPNG(texture));
            }
            finally
            {
                canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;canvas.planeDistance=oldDistance;
                for(var i=0;i<children.Length;i++)if(children[i])children[i].gameObject.layer=layers[i];
                UnityEngine.RenderTexture.active=oldTarget;
                UnityEngine.RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(cameraObject);
            }
            return new{path,cardHeight=card.rect.height,buttonInside=card.rect.Contains(new UnityEngine.Vector2(bounds.center.x,bounds.min.y))};
        }
        if((bool?)command["tooltip"]==true)
        {
            var tooltip=UnityEngine.Object.FindObjectsByType<UnityEngine.MonoBehaviour>(UnityEngine.FindObjectsInactive.Include,UnityEngine.FindObjectsSortMode.None)
                .Single(c=>c.GetType().FullName=="EscapeFromDuckovCoopMod.SharedHomeTooltip");
            var data=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){position=new UnityEngine.Vector2(600,250)};
            tooltip.GetType().GetMethod("OnPointerEnter").Invoke(tooltip,new object[]{data});
            var text=UnityEngine.GameObject.Find("SharedHomeExplanation").GetComponentInChildren<TMPro.TextMeshProUGUI>().text;
            tooltip.GetType().GetMethod("OnPointerExit").Invoke(tooltip,new object[]{data});
            return new{text};
        }
        return new {enabled=type.GetProperty("Enabled").GetValue(null),active=type.GetProperty("Active").GetValue(null),
            loadoutReady=mod.GetType("EscapeFromDuckovCoopMod.SharedHomeLoadouts").GetProperty("Ready",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null),
            buildingReturnBusy=AccessTools.Field(typeof(BuildingManager),"returningBuilding").GetValue(null),
            defaultMode=mod.GetType("EscapeFromDuckovCoopMod.CoopGeneralSettings").GetField("PrivateSharedHome").GetValue(
                Activator.CreateInstance(mod.GetType("EscapeFromDuckovCoopMod.CoopGeneralSettings"))),
            unlocks=new {visited=command["probeScene"]==null ? false : MultiSceneCore.GetVisited((string)command["probeScene"]),
                beacon=command["probeScene"]==null ? false : BeaconManager.GetBeaconUnlocked((string)command["probeScene"],0),
                formulas=CraftingManager.UnlockedFormulaIDs.OrderBy(x=>x).ToArray(),
                notes=NoteIndex.Instance?NoteIndex.Instance.UnlockedNotes.OrderBy(x=>x).ToArray():Array.Empty<string>(),
                keys=MasterKeysManager.AllPossibleKeys.Where(MasterKeysManager.IsActive).ToArray(),
                worldBool=CommonVariables.GetBool("SharedHomeTest"),
                perks=!PerkTreeManager.Instance?Array.Empty<object>():PerkTreeManager.Instance.perkTrees.Where(t=>t).Select(t=>new {t.ID,perks=t.Perks.Where(p=>p).Select(p=>new{p.name,p.Unlocked,p.Unlocking}).ToArray()}).Cast<object>().ToArray()},
            guest=type.GetProperty("Guest").GetValue(null),xp=EXPManager.EXP,level=EXPManager.Level,money=EconomyManager.Money,
            overlay=mod.GetType("EscapeFromDuckovCoopMod.SharedHomeSave").GetProperty("OverlayActive",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null),
            quests=QuestManager.Instance.ActiveQuests.Select(q=>new {id=q.ID,complete=q.Complete,
                tasks=q.Tasks.Select(t=>new{id=t.ID,type=t.GetType().Name,data=t.GenerateSaveData(),finished=t.IsFinished()}).ToArray()}).ToArray(),
            history=QuestManager.Instance.HistoryQuests.Select(q=>q.ID).ToArray(),
            rewards=QuestManager.Instance.HistoryQuests.Where(q=>command["quest"]!=null && q.ID==(int)command["quest"])
                .SelectMany(q=>q.Rewards.Select(r=>new{id=r.ID,claimed=r.Claimed,type=r.GetType().Name})).ToArray(),
            buffer=PlayerStorage.IncomingItemBuffer.Select(d=>d.entries.Select(e=>new{type=e.typeID,count=e.StackCount}).ToArray()).ToArray(),
            context=AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.SharedHomeCharacterContext"),"Read").Invoke(
                AccessTools.Field(mod.GetType("EscapeFromDuckovCoopMod.SharedHomeLoadouts"),"SaveContext").GetValue(null),new object[]{LevelManager.Instance}),
            characterCapacity=CharacterMainControl.Main.InventoryCapacity,inventoryCapacity=CharacterMainControl.Main.CharacterItem.Inventory.Capacity,
            gear=CharacterMainControl.Main.CharacterItem.Inventory.Capacity<0?null:AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.ItemTool"),"MakeSnapshot").Invoke(null,new object[]{CharacterMainControl.Main.CharacterItem}),
            storage=!PlayerStorage.Inventory?null:new {capacity=PlayerStorage.Inventory.Capacity,items=Enumerable.Range(0,PlayerStorage.Inventory.Capacity)
                .Select(i=>new{slot=i,item=PlayerStorage.Inventory.GetItemAt(i)}).Where(e=>e.item)
                .Select(e=>new{e.slot,type=e.item.TypeID,count=e.item.StackCount}).ToArray()},
            inventory=ItemStatsSystem.Data.ItemTreeData.FromItem(CharacterMainControl.Main.CharacterItem).entries
                .Select(e=>new{type=e.typeID,count=e.StackCount}).ToArray(),
            buildings=!BuildingManager.Instance?Array.Empty<object>():BuildingManager.Instance.Areas.Select(a=>new {id=a.AreaID,buildings=a.Buildings.Select(b=>new{
                id=b.ID,guid=b.GUID,x=b.Coord.x,y=b.Coord.y,rotation=(int)b.Rotation}).ToArray()}).Cast<object>().ToArray(),
            ui=UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Toggle>(UnityEngine.FindObjectsInactive.Include,UnityEngine.FindObjectsSortMode.None)
                .Where(t=>t.name=="SharedHomeToggle").Select(t=>new{t.isOn,t.interactable,path=t.transform.parent.parent.name}).ToArray()};
    }
}
