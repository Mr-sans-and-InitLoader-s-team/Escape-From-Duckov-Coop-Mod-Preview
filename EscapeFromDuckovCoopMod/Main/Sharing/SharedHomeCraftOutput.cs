using System.Reflection;
using Duckov.Economy;
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeCraftOutput
{
    internal static readonly MethodInfo NativeReturn = AccessTools.Method(typeof(Cost), "Return");

    internal static UniTask Return(ref Cost cost, bool directToBuffer, bool toPlayerInventory, int amountFactor, List<Item> generatedItemsBuffer)
    {
        // Route before the native merge can consume the generated item into the host's backpack.
        if (SharedHome.Active && NetService.Instance.IsServer) toPlayerInventory = false;
        return (UniTask)NativeReturn.Invoke(cost, new object[] { directToBuffer, toPlayerInventory, amountFactor, generatedItemsBuffer });
    }
}
