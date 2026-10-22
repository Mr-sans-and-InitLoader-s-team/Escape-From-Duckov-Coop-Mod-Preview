using System.Reflection;
using System.Reflection.Emit;
using Duckov;
using Duckov.BlackMarkets;
using Duckov.Buildings;
using Duckov.Economy;
using Duckov.PerkTrees;
using Duckov.Quests;
using Duckov.Quests.Tasks;
using Saves;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(InteractableLootbox), "StartLoot")]
internal static class SharedHomeWarehouseReadyPatch
{
    private static bool Prefix(InteractableLootbox __instance, ref bool __result)
    {
        if (!SharedHome.Guest || SharedHomeSave.OverlayActive && SharedHomeLoadouts.Ready || !PlayerStorage.Instance ||
            __instance != PlayerStorage.Instance.InteractableLootBox) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(SavesSystem), nameof(SavesSystem.SetFile))]
internal static class SharedHomeChangeSlotPatch
{
    private static void Prefix()
    {
        if (SharedHome.Active) NetService.Instance.StopNetwork();
        CustomFace.ResetCache();
    }
}

[HarmonyPatch(typeof(Duckov.Achievements.AchievementManager), "Unlock")]
internal static class SharedHomeAchievementPatch
{
    private static bool Prefix() => !SharedHome.Guest;
}

[HarmonyPatch(typeof(SavesSystem), nameof(SavesSystem.SaveFile))]
internal static class SharedHomeSaveFilePatch
{
    private static bool Prefix() => !SharedHome.Guest && !SharedHomeSave.OverlayActive && !SharedHome.Reloading && !SharedHomeLoadouts.Restoring && !SharedHomeDisplays.Loading;
}

[HarmonyPatch]
internal static class SharedHomeCachedWritePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(ES3).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(m => m.Name == "StoreCachedFile");
    private static bool Prefix() => !SharedHome.Guest && !SharedHomeSave.OverlayActive && !SharedHome.Reloading && !SharedHomeLoadouts.Restoring && !SharedHomeDisplays.Loading;
}

[HarmonyPatch(typeof(SavesSystem), "CreateBackup", typeof(string))]
internal static class SharedHomeBackupPatch
{
    private static bool Prefix() => !SharedHome.Guest && !SharedHomeSave.OverlayActive && !SharedHome.Reloading && !SharedHomeLoadouts.Restoring && !SharedHomeDisplays.Loading;
}

[HarmonyPatch(typeof(QuestTask_KillCount), "Health_OnDead")]
internal static class SharedHomeQuestKillPatch
{
    private static bool Prefix(QuestTask_KillCount __instance, Health health) =>
        !SharedHome.Guest && AiQuestCredit.AllowNativeTask(__instance, health);
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(CharacterMainControl), nameof(CharacterMainControl.Team));
        var credit = AccessTools.Method(typeof(SharedHomeProgress), nameof(SharedHomeProgress.KillCreditTeam));
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(getter))
            {
                var replacement = new CodeInstruction(instruction) { opcode = OpCodes.Call, operand = credit };
                yield return replacement;
            }
            else yield return instruction;
        }
    }
}

[HarmonyPatch(typeof(EXPManager), nameof(EXPManager.AddExp))]
internal static class SharedHomeExpPatch
{
    private static bool Prefix(int amount, ref bool __result)
    {
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        SharedHome.Request("xp", new { amount });
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(EconomyManager), nameof(EconomyManager.Add))]
internal static class SharedHomeMoneyPatch
{
    private static bool Prefix(long amount, ref bool __result)
    {
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        SharedHome.Request("money", new { amount });
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Perk), nameof(Perk.SubmitItemsAndBeginUnlocking))]
internal static class SharedHomePerkBeginPatch
{
    private static bool Prefix(Perk __instance, ref bool __result)
    {
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        SharedHome.Request("perk", new { tree = __instance.Master.ID, perk = __instance.name, confirm = false });
        __result = false;
        return false;
    }
    private static void Postfix() => SharedHome.MarkDirty();
}

[HarmonyPatch(typeof(Perk), nameof(Perk.ConfirmUnlock))]
internal static class SharedHomePerkConfirmPatch
{
    private static bool Prefix(Perk __instance, ref bool __result)
    {
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        SharedHome.Request("perk", new { tree = __instance.Master.ID, perk = __instance.name, confirm = true });
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(QuestManager), nameof(QuestManager.ActivateQuest))]
internal static class SharedHomeQuestActivatePatch
{
    private static bool Prefix(int id)
    {
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        SharedHome.Request("activateQuest", new { quest = id });
        return false;
    }
}

[HarmonyPatch(typeof(Quest), nameof(Quest.TryComplete))]
internal static class SharedHomeQuestCompletePatch
{
    private static bool Prefix(Quest __instance, ref bool __result)
    {
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        SharedHome.Request("completeQuest", new { quest = __instance.ID });
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Reward), nameof(Reward.Claim))]
internal static class SharedHomeRewardPatch
{
    private static bool Prefix(Reward __instance)
    {
        if (!SharedHome.Guest) return true;
        if (!SharedHome.Applying)
            SharedHome.Request("reward", new { quest = __instance.Master.ID, reward = __instance.ID });
        return false;
    }
}

[HarmonyPatch(typeof(SubmitItems), nameof(SubmitItems.Interact))]
internal static class SharedHomeQuestSubmitPatch
{
    private static bool Prefix(SubmitItems __instance)
    {
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        SharedHome.Request("taskInteract", new { quest = __instance.Master.ID, task = __instance.ID });
        return false;
    }
}

[HarmonyPatch(typeof(QuestTask_SubmitMoney), nameof(QuestTask_SubmitMoney.Interact))]
internal static class SharedHomeQuestMoneyPatch
{
    private static bool Prefix(QuestTask_SubmitMoney __instance)
    {
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        SharedHome.Request("taskInteract", new { quest = __instance.Master.ID, task = __instance.ID });
        return false;
    }
}

[HarmonyPatch(typeof(BuildingManager), "BuyAndPlace")]
internal static class SharedHomeBuildPatch
{
    private static bool Prefix(string areaID, string id, Vector2Int coord, BuildingRotation rotation, ref BuildingBuyAndPlaceResults __result)
    {
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        SharedHome.Request("build", new { area = areaID, building = id, x = coord.x, y = coord.y, rotation = (int)rotation });
        __result = BuildingBuyAndPlaceResults.Succeed;
        return false;
    }
}

[HarmonyPatch(typeof(BuildingManager), "ReturnBuilding")]
internal static class SharedHomeReturnBuildingPatch
{
    private static bool Prefix(int guid, ref string areaID, ref UniTask<bool> __result)
    {
        if (!SharedHome.Guest || SharedHome.Applying)
        {
            // Native scoped lookup fills the out values but returns false. Validate
            // the requested area, then use the native successful GUID lookup.
            if (SharedHome.Active && areaID != null)
            {
                var requestedArea = areaID;
                if (!BuildingManager.Instance || !BuildingManager.Instance.Areas.Any(a => a.AreaID == requestedArea && a.Buildings.Any(b => b.GUID == guid)))
                {
                    __result = UniTask.FromResult(false);
                    return false;
                }
                areaID = null;
            }
            return true;
        }
        SharedHome.Request("returnBuilding", new { guid, area = areaID });
        __result = UniTask.FromResult(true);
        return false;
    }
}

[HarmonyPatch(typeof(BlackMarket), nameof(BlackMarket.PayAndRegenerate))]
internal static class SharedHomeBlackMarketRefreshPatch
{
    private static bool Prefix()
    {
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        SharedHome.Request("blackRefresh", new { });
        return false;
    }
    private static void Postfix() => SharedHome.MarkDirty();
}

[HarmonyPatch(typeof(BlackMarket), "FixedUpdate")]
internal static class SharedHomeBlackMarketTimerPatch
{
    private static bool Prefix() => !SharedHome.Guest;
}

[HarmonyPatch(typeof(BlackMarket), nameof(BlackMarket.Buy))]
internal static class SharedHomeBlackMarketBuyPatch
{
    private static bool Prefix(BlackMarket __instance, BlackMarket.DemandSupplyEntry entry, ref UniTask<bool> __result)
    {
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        SharedHome.Request("blackBuy", new { index = __instance.Supplies.IndexOf(entry), type = entry.ItemID, price = entry.TotalPrice });
        __result = UniTask.FromResult(true);
        return false;
    }
}

[HarmonyPatch(typeof(BlackMarket), nameof(BlackMarket.Sell))]
internal static class SharedHomeBlackMarketSellPatch
{
    private static bool Prefix(BlackMarket __instance, BlackMarket.DemandSupplyEntry entry, ref UniTask<bool> __result)
    {
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        SharedHome.Request("blackSell", new { index = __instance.Demands.IndexOf(entry), type = entry.ItemID, price = entry.TotalPrice });
        __result = UniTask.FromResult(true);
        return false;
    }
}

[HarmonyPatch(typeof(LevelManager), "SaveMainCharacter")]
internal static class SharedHomeSaveLoadoutPatch
{
    private static bool Prefix() => !SharedHomeLoadouts.Restoring;
    private static void Postfix() => SharedHomeLoadouts.Flush();
}
