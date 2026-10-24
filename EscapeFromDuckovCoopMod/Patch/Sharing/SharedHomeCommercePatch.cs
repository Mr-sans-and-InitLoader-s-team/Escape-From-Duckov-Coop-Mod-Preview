using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch]
internal static class SharedHomePurchaseDeliveryPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(StockShop), "BuyTask")
        .GetCustomAttribute<AsyncStateMachineAttribute>().StateMachineType.GetMethod("MoveNext",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var owner = __originalMethod.DeclaringType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(StockShop));
        var send = AccessTools.Method(typeof(ItemUtilities), nameof(ItemUtilities.SendToPlayerCharacterInventory));
        var notify = AccessTools.Method(typeof(NotificationText), nameof(NotificationText.Push));
        foreach (var instruction in instructions)
        {
            var replacement = instruction.Calls(send) ? nameof(SharedHomeShopPurchase.SendToBuyer) :
                instruction.Calls(notify) ? nameof(SharedHomeShopPurchase.Notify) : null;
            if (replacement != null)
            {
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.MoveLabelsFrom(instruction);
                load.MoveBlocksFrom(instruction);
                yield return load;
                yield return new CodeInstruction(OpCodes.Ldfld, owner);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SharedHomeShopPurchase), replacement));
            }
            else yield return instruction;
        }
    }
}

[HarmonyPatch(typeof(StockShop), "Sell")]
internal static class SharedHomeWarehouseSellPatch
{
    private static bool Prefix(StockShop __instance, Item target, ref UniTask __result)
    {
        if (!SharedHomeStorage.Hold(target?.InInventory)) return true;
        if (target.CanBeSold) SharedHomeStorage.RequestEdit(target, new { operation = "sell", merchant = __instance.MerchantID });
        __result = UniTask.CompletedTask;
        return false;
    }
}
