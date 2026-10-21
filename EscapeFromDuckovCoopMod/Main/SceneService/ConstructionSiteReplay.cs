using System.Reflection;

namespace EscapeFromDuckovCoopMod;

internal static class ConstructionSiteReplay
{
    private static readonly FieldInfo WasBuilt = AccessTools.Field(typeof(ConstructionSite), "wasBuilt");
    private static readonly MethodInfo OnBuilt = AccessTools.Method(typeof(ConstructionSite), "OnBuilt");
    private static readonly FieldInfo OriginalCost = AccessTools.Field(typeof(ConstructionSite), "cost");

    private static ConstructionSite FindFreeSite(CostTaker taker)
    {
        var site = taker ? taker.GetComponentInParent<ConstructionSite>(true) : null;
        return site && ((Duckov.Economy.Cost)OriginalCost.GetValue(site)).IsFree ? site : null;
    }

    internal static bool IsFreeConstructionCost(CostTaker taker) => FindFreeSite(taker);

    internal static bool TryApply(CostTaker taker)
    {
        var site = FindFreeSite(taker);
        if (!site) return false;
        if (!(bool)WasBuilt.GetValue(site)) OnBuilt.Invoke(site, new object[] { taker });
        return true;
    }
}
