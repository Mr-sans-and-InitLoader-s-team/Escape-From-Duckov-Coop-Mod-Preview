using Duckov;
using Duckov.BlackMarkets;
using Duckov.Buildings;
using Duckov.Economy;
using Duckov.PerkTrees;
using Duckov.Quests;
using Newtonsoft.Json.Linq;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeActions
{
    internal static void Execute(string action, JObject args, NetPeer sender)
    {
        if (args == null || !SharedHome.Active || !NetService.Instance.IsServer) return;
        switch (action)
        {
            case "storageTake": SharedHomeStorage.Take(args, sender); break;
            case "storageTaken": SharedHomeStorage.FinishTake(args, sender); break;
            case "storageEdit": SharedHomeStorageOperations.Execute(args, sender); break;
            case "storageSort": PlayerStorage.Inventory?.Sort(); break;
            case "storageBufferTake": SharedHomeStorage.TakeBuffer((int)args["index"], (string)args["expected"]).Forget(); break;
            case "storageBufferPut": SharedHomeStorage.PutBuffer(args, sender).Forget(); break;
            case "worldFlag": SharedHomeWorld.SetFlag((string)args["key"], (bool)args["value"]); break;
            case "count": SavesCounter.AddCount((string)args["key"]); break;
            case "craft": SharedHomeCommerce.Craft((string)args["id"]).Forget(); break;
            case "shopBuy": SharedHomeCommerce.Buy((string)args["merchant"], (int)args["type"], (int)args["amount"]).Forget(); break;
            case "unlock": SharedHomeUnlocks.Execute(args); break;
            case "xp":
                var amount = (int)args["amount"];
                if (amount > 0) EXPManager.AddExp(amount);
                break;
            case "money": EconomyManager.Add((long)args["amount"]); break;
            case "perk":
                var tree = PerkTreeManager.GetPerkTree((string)args["tree"]);
                var perk = tree?.Perks.FirstOrDefault(p => p && p.name == (string)args["perk"]);
                if (perk)
                {
                    if ((bool?)args["confirm"] == true) perk.ConfirmUnlock();
                    else if (!perk.Unlocking) perk.SubmitItemsAndBeginUnlocking();
                }
                break;
            case "activateQuest":
                var id = (int)args["quest"];
                var manager = QuestManager.Instance;
                var prefab = (Quest)AccessTools.Method(typeof(QuestManager), "GetQuestPrefab").Invoke(manager, new object[] { id });
                if (prefab && prefab.MeetsPrerequisit() && !manager.ActiveQuests.Concat(manager.HistoryQuests).Any(q => q.ID == id))
                    manager.ActivateQuest(id);
                break;
            case "completeQuest":
                var completed = FindQuest(args);
                if (completed && completed.TryComplete())
                    foreach (var reward in completed.Rewards.Where(r => r.AutoClaim)) reward.Claim();
                break;
            case "reward":
                var quest = FindQuest(args);
                quest?.Rewards.FirstOrDefault(r => r.ID == (int)args["reward"])?.Claim();
                break;
            case "task":
                var task = FindQuest(args)?.Tasks.FirstOrDefault(t => t.ID == (int)args["task"]);
                if (!task || task.IsFinished()) break;
                var current = task.GenerateSaveData();
                if (current is int count)
                {
                    var delta = (int)args["delta"];
                    if (delta <= 0) break;
                    task.SetupSaveData((int)Math.Min(int.MaxValue, (long)count + delta));
                }
                else if (current is bool)
                {
                    if (task is Duckov.Quests.Tasks.QuestTask_CheckSaveData check)
                        SharedHomeWorld.SetFlag(check.SaveDataKey, (bool)args["value"]);
                    else task.SetupSaveData((bool)args["value"]);
                }
                else break;
                AccessTools.Method(typeof(Duckov.Quests.Task), "ReportStatusChanged").Invoke(task, null);
                break;
            case "taskInteract":
                FindQuest(args)?.Tasks.FirstOrDefault(t => t.ID == (int)args["task"])?.Interact();
                break;
            case "build":
                var area = (string)args["area"];
                var building = (string)args["building"];
                var x = (int)args["x"]; var y = (int)args["y"]; var rotation = (int)args["rotation"];
                if (SharedHomeBuildings.ValidPlacement(area, building, x, y, rotation))
                    AccessTools.Method(typeof(BuildingManager), "BuyAndPlace").Invoke(null,
                        new object[] { area, building, new Vector2Int(x, y), (BuildingRotation)rotation });
                break;
            case "returnBuilding":
                ReturnBuilding((int)args["guid"], (string)args["area"]).Forget();
                break;
            case "blackRefresh": BlackMarket.Instance?.PayAndRegenerate(); break;
            case "blackBuy":
            case "blackSell":
                var market = BlackMarket.Instance;
                if (!market) break;
                var entries = action == "blackBuy" ? market.Supplies : market.Demands;
                var index = (int)args["index"];
                if (index >= 0 && index < entries.Count && entries[index].ItemID == (int)args["type"] && entries[index].TotalPrice == (int)args["price"])
                    Trade(market, entries[index], action == "blackBuy").Forget();
                break;
        }
    }
    private static Quest FindQuest(JObject args) => QuestManager.Instance?.ActiveQuests.Concat(QuestManager.Instance.HistoryQuests)
        .FirstOrDefault(q => q && q.ID == (int)args["quest"]);
    private static async UniTaskVoid ReturnBuilding(int guid, string area)
    {
        if (!BuildingManager.Instance || !BuildingManager.Instance.Areas.Any(a => a.AreaID == area && a.Buildings.Any(b => b.GUID == guid))) return;
        await (UniTask<bool>)AccessTools.Method(typeof(BuildingManager), "ReturnBuilding").Invoke(null, new object[] { guid, area });
        SharedHome.MarkDirty();
    }
    private static async UniTaskVoid Trade(BlackMarket market, BlackMarket.DemandSupplyEntry entry, bool buy)
    {
        if (buy) await market.Buy(entry); else await market.Sell(entry);
        SharedHome.MarkDirty();
    }
}
