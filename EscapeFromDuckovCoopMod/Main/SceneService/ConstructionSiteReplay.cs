using System.Reflection;

namespace EscapeFromDuckovCoopMod;

internal static class ConstructionSiteReplay
{
    private static readonly FieldInfo WasBuilt = AccessTools.Field(typeof(ConstructionSite), "wasBuilt");
    private static readonly MethodInfo OnBuilt = AccessTools.Method(typeof(ConstructionSite), "OnBuilt");
    private static readonly FieldInfo OriginalCost = AccessTools.Field(typeof(ConstructionSite), "cost");
    private static readonly FieldInfo CostTakerField = AccessTools.Field(typeof(ConstructionSite), "costTaker");
    private static readonly FieldInfo PaidEvent = AccessTools.Field(typeof(CostTaker), "onPayed");

    private static ConstructionSite FindSite(CostTaker taker)
    {
        var site = taker ? taker.GetComponentInParent<ConstructionSite>(true) : null;
        if (site && (CostTaker)CostTakerField.GetValue(site) == taker) return site;
        return taker ? (PaidEvent.GetValue(taker) as Action<CostTaker>)?.GetInvocationList()
            .Select(callback => callback.Target).OfType<ConstructionSite>()
            .FirstOrDefault(owner => owner && (CostTaker)CostTakerField.GetValue(owner) == taker) : null;
    }

    internal static bool IsFreeConstructionCost(CostTaker taker)
    {
        var site = FindSite(taker);
        return site && ((Duckov.Economy.Cost)OriginalCost.GetValue(site)).IsFree;
    }

    internal static bool IsSharedConstructionCost(CostTaker taker) => SharedHome.Active && FindSite(taker);

    internal static bool TryApply(CostTaker taker)
    {
        var site = FindSite(taker);
        if (!site || !SharedHome.Active && !((Duckov.Economy.Cost)OriginalCost.GetValue(site)).IsFree) return false;
        if (!(bool)WasBuilt.GetValue(site)) OnBuilt.Invoke(site, new object[] { taker });
        return true;
    }
}
