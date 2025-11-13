// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspicuous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine.EventSystems;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(LootView), "OnLootTargetItemDoubleClicked")]
[HarmonyPriority(Priority.First)]
internal static class Patch_LootView_OnLootTargetItemDoubleClicked_EquipDirectly
{
    // ⚠ 第二个参数类型必须是 Duckov.UI.InventoryEntry（不是 InventoryDisplayEntry）
    private static bool Prefix(LootView __instance, InventoryDisplay display, InventoryEntry entry, PointerEventData data)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || mod.IsServer) return true; // 主机/单机走原逻辑

        var item = entry?.Item;
        if (item == null) return false;

        var lootInv = __instance?.TargetInventory;
        if (lootInv == null) return true;

        // 只拦“公共战利品容器”，仓库/宠物包不拦
        if (!ReferenceEquals(item.InInventory, lootInv)) return true;
        if (!LootboxDetectUtil.IsLootboxInventory(lootInv)) return true;

        // 容器中的索引
        int pos;
        try
        {
            pos = lootInv.GetIndex(item);
        }
        catch
        {
            return true;
        }

        if (pos < 0) return true;

        // 选择一个可穿戴且为空的槽（武器位优先）
        var destSlot = PickEquipSlot(item);

        // 发 TAKE（带目标槽）；回包后由 Mod.cs 的 _cliPendingTake[token].slot → slot.Plug(item)
        if (destSlot != null)
            COOPManager.LootNet.Client_SendLootTakeRequest(lootInv, pos, null, -1, destSlot);
        else
            COOPManager.LootNet.Client_SendLootTakeRequest(lootInv, pos);

        data?.Use(); // 吃掉这次双击
        return false; // 阻断原方法，避免回落到“塞背包”
    }

    private static Slot PickEquipSlot(Item item)
    {
        var cmc = CharacterMainControl.Main;
        var charItem = cmc ? cmc.CharacterItem : null;
        var slots = charItem ? charItem.Slots : null;
        if (slots == null) return null;

        // 武器位优先
        try
        {
            var s = cmc.PrimWeaponSlot();
            if (s != null && s.Content == null && s.CanPlug(item)) return s;
        }
        catch
        {
        }

        try
        {
            var s = cmc.SecWeaponSlot();
            if (s != null && s.Content == null && s.CanPlug(item)) return s;
        }
        catch
        {
        }

        try
        {
            var s = cmc.MeleeWeaponSlot();
            if (s != null && s.Content == null && s.CanPlug(item)) return s;
        }
        catch
        {
        }

        // 其余槽
        foreach (var s in slots)
        {
            if (s == null || s.Content != null) continue;
            try
            {
                if (s.CanPlug(item)) return s;
            }
            catch
            {
            }
        }

        return null;
    }
}

// 允许 LootView.RegisterEvents 总是执行；只做异常兜底，避免首次打开因 open==false 而错过注册
[HarmonyPatch(typeof(LootView), "RegisterEvents")]
internal static class Patch_LootView_RegisterEvents_Safe
{
    // 不要 Prefix 拦截，让原方法总是运行
    private static Exception Finalizer(LootView __instance, Exception __exception)
    {
        if (__exception != null)
        {
            Debug.LogWarning("[LOOT][UI] RegisterEvents threw and was swallowed: " + __exception);
            return null; // 吞掉异常，保持 UI 可用
        }

        return null;
    }
}

// 翻页：未打开时直接吞掉
[HarmonyPatch(typeof(LootView), "OnPreviousPage")]
internal static class Patch_LootView_OnPreviousPage_OnlyWhenOpen
{
    private static bool Prefix(LootView __instance)
    {
        var isOpen = false;
        try
        {
            var tr = Traverse.Create(__instance);
            try
            {
                isOpen = tr.Property<bool>("open").Value;
            }
            catch
            {
                isOpen = tr.Field<bool>("open").Value;
            }
        }
        catch
        {
        }

        return isOpen; // 未打开==false -> 不进原方法
    }
}

[HarmonyPatch(typeof(LootView), "OnNextPage")]
internal static class Patch_LootView_OnNextPage_OnlyWhenOpen
{
    private static bool Prefix(LootView __instance)
    {
        var isOpen = false;
        try
        {
            var tr = Traverse.Create(__instance);
            try
            {
                isOpen = tr.Property<bool>("open").Value;
            }
            catch
            {
                isOpen = tr.Field<bool>("open").Value;
            }
        }
        catch
        {
        }

        return isOpen;
    }
}

[HarmonyPatch(typeof(LootView), "get_TargetInventory")]
internal static class Patch_LootView_GetTargetInventory_Safe
{
    private static Exception Finalizer(LootView __instance,
        ref Inventory __result,
        Exception __exception)
    {
        if (__exception != null)
        {
            __result = null; // 直接当“未就绪/无容器”处理
            return null; // 吞掉异常
        }

        return null;
    }
}

// 让“是否需要搜索”对所有公共容器生效（世界箱 + 尸体箱等）可能的修复:)
[HarmonyPatch(typeof(LootView), nameof(LootView.HasInventoryEverBeenLooted))]
internal static class Patch_LootView_HasInventoryEverBeenLooted_NeedAware_AllLoot
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(ref bool __result, Inventory inventory)
    {
        if (!inventory) return true;

        if (!LootboxDetectUtil.IsLootboxInventory(inventory)) return true;
        if (LootboxDetectUtil.IsPrivateInventory(inventory)) return true;

        __result = true; // 公共容器一律视为“已搜过”
        return false;
    }
}

[HarmonyPatch(typeof(LootView), "OnStartLoot")]
internal static class Patch_LootView_OnStartLoot_PrimeSearchGate_Robust
{
    private static void Postfix(LootView __instance, InteractableLootbox lootbox)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || mod.IsServer) return;

        var inv = __instance.TargetInventory;
        if (!inv || lootbox == null) return;

        if (LootboxDetectUtil.IsPrivateInventory(inv)) return;

        TrySetNeedInspection(inv, false);
        TrySetLootboxNeedInspect(lootbox, false);
    }

    private static void TrySetNeedInspection(Inventory inv, bool v)
    {
        try
        {
            inv.NeedInspection = v;
        }
        catch
        {
        }
    }

    private static void TrySetLootboxNeedInspect(InteractableLootbox box, bool v)
    {
        if (box == null) return;
        try
        {
            var t = box.GetType();
            var f = AccessTools.Field(t, "needInspect");
            if (f != null)
            {
                f.SetValue(box, v);
                return;
            }

            var p = AccessTools.Property(t, "needInspect");
            if (p != null && p.CanWrite) p.SetValue(box, v, null);
        }
        catch
        {
        }
    }
}