using Duckov.Quests.Tasks;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeQuestItems
{
    internal static void SubmitPersonal(SubmitItems task)
    {
        if (!SharedHomeLoadouts.Ready || !task.Master || task.IsFinished()) return;
        // Guest-owned items must be consumed on their owner; the warehouse stays authoritative.
        foreach (var item in ItemUtilities.FindAllBelongsToPlayer(item => item && item.TypeID == task.ItemTypeID)
                     .Where(item => !SharedHomeStorage.IsShared(item.InInventory)))
        {
            task.Submit(item);
            if (task.IsFinished()) break;
        }
        SharedHomeLoadouts.Flush();
        if (!task.IsFinished()) SharedHome.Request("taskInteract", new { quest = task.Master.ID, task = task.ID });
    }

    internal static void SubmitWarehouse(SubmitItems task)
    {
        if (!task.Master || task.IsFinished() || !SharedHomeStorage.CanTransfer) return;
        foreach (var item in PlayerStorage.Inventory.Where(item => item && item.TypeID == task.ItemTypeID).ToArray())
        {
            task.Submit(item);
            if (task.IsFinished()) break;
        }
    }
}
