namespace EscapeFromDuckovCoopMod;

internal static class CorpseInspection
{
    internal static void Initialize(InteractableLootbox box, bool tomb, string ownerId)
    {
        var inv = box.Inventory;
        if (!inv) return;
        var ownBody = tomb && NetService.Instance?.IsSelfId(ownerId) == true;
        var needInspection = !ownBody && (tomb || (LevelConfig.Instance
            ? LevelConfig.Instance.NeedInspectDeadLootBox : box.needInspect));
        box.needInspect = needInspection;
        inv.NeedInspection = needInspection;
        inv.hasBeenInspectedInLootBox = !needInspection;
        foreach (var item in inv)
        {
            if (!item) continue;
            item.Inspected = !needInspection;
            item.Inspecting = false;
        }
    }
}
