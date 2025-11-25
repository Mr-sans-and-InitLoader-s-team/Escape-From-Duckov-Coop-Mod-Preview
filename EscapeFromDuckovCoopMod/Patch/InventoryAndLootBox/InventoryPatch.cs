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

using System.Reflection;
using Duckov.UI;
using ItemStatsSystem;
using Object = UnityEngine.Object;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(Inventory), "AddAt")]
internal static class Patch_Inventory_AddAt_FromLoot
{
    private static bool Prefix(Inventory __instance, Item item, int atPosition, ref bool __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || m.IsServer) return true;
        if (COOPManager.LootNet._applyingLootState) return true;

        // 本地 OnDead 场景：让墓碑填充走原生逻辑，避免被 LootNet 拦截
        try
        {
            if (DeadLootSpawnContext.LocalOnDead)
                return true;
        }
        catch
        {
        }

        var srcInv = item ? item.InInventory : null;
        if (srcInv == null || srcInv == __instance) return true;

        // ★ 只拦公共容器来源
        if (LootboxDetectUtil.IsLootboxInventory(srcInv) && !LootboxDetectUtil.IsPrivateInventory(srcInv))
        {
            var srcPos = srcInv.GetIndex(item);

            // 进入保护区：标记“我现在是在 Loot.AddAt 流程里”
            LootUiGuards.InLootAddAtDepth++;
            try
            {
                COOPManager.LootNet.Client_SendLootTakeRequest(srcInv, srcPos, __instance, atPosition, null);
            }
            finally
            {
                LootUiGuards.InLootAddAtDepth--;
            }

            __result = true;
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddAt), typeof(Item), typeof(int))]
[HarmonyPriority(Priority.First)]
internal static class Patch_Inventory_AddAt_SlotToPrivate_Reroute
{
    private static bool Prefix(Inventory __instance, Item item, int atPosition, ref bool __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || m.IsServer) return true;

        // 只拦“落到私有库存”的情况（玩家背包/身上/宠物包）
        if (!LootboxDetectUtil.IsPrivateInventory(__instance)) return true;

        // 只拦“仍插在武器槽位里的附件”
        var slot = item ? item.PluggedIntoSlot : null;
        if (slot == null) return true;

        // 找到最外层主件（武器）
        var master = slot.Master;
        while (master && master.PluggedIntoSlot != null)
            master = master.PluggedIntoSlot.Master;

        // 源容器：优先 master.InInventory；拿不到时兜底用当前 LootView 的容器
        var srcLoot = master ? master.InInventory : null;
        if (!srcLoot)
            try
            {
                var lv = LootView.Instance;
                if (lv) srcLoot = lv.TargetInventory;
            }
            catch
            {
            }

        // 源容器必须是“公共容器”
        if (!srcLoot || !LootboxDetectUtil.IsLootboxInventory(srcLoot) || LootboxDetectUtil.IsPrivateInventory(srcLoot))
        {
            // 为了不触发原生的“父物体”报错，这里直接拦下，不执行原方法
            Debug.LogWarning($"[Coop] AddAt(private, slot->backpack) srcLoot not found; block local AddAt for '{item?.name}'");
            __result = false;
            return false;
        }

        Debug.Log($"[Coop] AddAt(private, slot->backpack) -> send UNPLUG(takeToBackpack), destPos={atPosition}");
        // 让主机先卸下，再由 TAKE_OK 驱动本地落到 atPosition
        COOPManager.LootNet.Client_RequestSlotUnplugToBackpack(srcLoot, master, slot.Key, __instance, atPosition);

        __result = true; // 本地视为已受理
        return false; // 阻止原生 AddAt（否则就会出现“仍有父物体”的报错）
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddAt), typeof(Item), typeof(int))]
internal static class Patch_Inventory_AddAt_BlockLocalInLoot
{
    private static bool Prefix(Inventory __instance, Item item, int atPosition, ref bool __result)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.IsClient) return true;

        // 本地死亡掉落的墓碑填充，放行原生 AddAt 避免吞掉战利品
        try
        {
            if (DeadLootSpawnContext.LocalOnDead)
                return true;
        }
        catch
        {
        }

        if (LootboxDetectUtil.IsPrivateInventory(__instance)) return true; // 私有库存放行

        if (!LootManager.IsCurrentLootInv(__instance)) return true;
        if (COOPManager.LootNet.ApplyingLootState) return true;

        LootUiGuards.InLootAddAtDepth++;
        try
        {
            var srcInv = item ? item.InInventory : null;

            // === A) 同容器内换位 / 挪动：改为 TAKE -> PUT 两段式 ===
            if (ReferenceEquals(srcInv, __instance))
            {
                // 同格就直接视为成功
                var srcPos = __instance.GetIndex(item);
                if (srcPos == atPosition)
                {
                    __result = true;
                    return false;
                }

                if (srcPos < 0)
                {
                    __result = false;
                    return false;
                }

                // 1) 发 TAKE（不带目的地）
                var tk = COOPManager.LootNet.Client_SendLootTakeRequest(__instance, srcPos, null, -1, null);

                // 2) 记录“待重排”，让 TAKE_OK 到达后立刻对同一容器发 PUT(atPosition)
                LootManager.Instance.NoteLootReorderPending(tk, __instance, atPosition);

                __result = true; // 告诉上层：已受理，别回退
                return false; // 不执行原 AddAt（避免本地改内容）
            }

            // B) 容器 -> 其它库存：保持你现有逻辑（仍旧发 TAKE 携带目的地）
            if (LootManager.IsCurrentLootInv(srcInv))
            {
                var srcPos = srcInv.GetIndex(item);
                if (srcPos < 0)
                {
                    __result = false;
                    return false;
                }

                COOPManager.LootNet.Client_SendLootTakeRequest(srcInv, srcPos, __instance, atPosition, null);
                __result = true;
                return false;
            }

            // C) slot -> loot（同容器）：拦截“从容器内武器插槽卸下到容器格子”
            if (__instance && LootboxDetectUtil.IsLootboxInventory(__instance) && !LootboxDetectUtil.IsPrivateInventory(__instance))
            {
                var slot = item ? item.PluggedIntoSlot : null;
                if (slot != null)
                {
                    // 找到这个槽位所属武器的“根主件”，以及它所在的容器
                    var master = slot.Master;
                    while (master && master.PluggedIntoSlot != null) master = master.PluggedIntoSlot.Master;
                    var masterLoot = master ? master.InInventory : null;

                    if (masterLoot == __instance) // 同一个容器：这是“拆附件放回容器”的场景
                    {
                        Debug.Log("[Coop] AddAt@Loot (slot->loot) -> send UNPLUG(takeToBackpack=false)");
                        try
                        {
                            LootUiGuards.InLootAddAtDepth++;
                        }
                        catch
                        {
                        }

                        try
                        {
                            // 走“旧负载+追加字段”的新重载：takeToBackpack=false
                            COOPManager.LootNet.Client_RequestLootSlotUnplug(__instance, slot.Master, slot.Key, false, 0);
                        }
                        finally
                        {
                            LootUiGuards.InLootAddAtDepth--;
                        }

                        __result = true; // 认为成功，等待主机的 LOOT_STATE 对齐UI
                        return false; // 阻断本地 AddAt，避免出现本地就先放进去
                    }
                }
            }


            // 其它来源 -> 容器：交给 PUT 拦截
            return true;
        }
        finally
        {
            LootUiGuards.InLootAddAtDepth--;
        }
    }
}

[HarmonyPatch(typeof(Inventory))]
internal static class Patch_Inventory_RemoveAt_BlockLocalInLoot
{
    // 用反射精确锁定 RemoveAt(int, out Item) 这个重载
    private static MethodBase TargetMethod()
    {
        var tInv = typeof(Inventory);
        var tItemByRef = typeof(Item).MakeByRefType();
        return AccessTools.Method(tInv, "RemoveAt", new[] { typeof(int), tItemByRef });
    }

    // 注意第二个参数是 out Item —— 用 ref 接住即可
    private static bool Prefix(Inventory __instance, int position, ref Item __1, ref bool __result)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.IsClient) return true;

        if (LootboxDetectUtil.IsPrivateInventory(__instance)) return true;

        var isLootInv = false;
        try
        {
            var lv = LootView.Instance;
            isLootInv = lv && __instance && ReferenceEquals(__instance, lv.TargetInventory);
        }
        catch
        {
        }

        if (!isLootInv) return true;
        if (LootboxDetectUtil.IsPrivateInventory(__instance)) return true; // ★ 放行私有库存
        if (COOPManager.LootNet.ApplyingLootState) return true;
        __1 = null;
        __result = false;
        return false;

        // 应用服务器快照期间允许 RemoveAt（UI 刷新），其余时间一律拦截
        if (COOPManager.LootNet.ApplyingLootState) return true;

        __1 = null;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), "NotifyContentChanged")]
public static class Patch_Inventory_NotifyContentChanged
{
    private static void Postfix(Inventory __instance, Item item)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || item == null) return;

        if (COOPManager.LootNet._applyingLootState) return;

        if (LootboxDetectUtil.IsLootboxInventory(__instance) && !LootboxDetectUtil.IsPrivateInventory(__instance))
            return;

        var net = COOPManager.ItemNet;
        if (net == null) return;

        net.HandleInventoryItemAdded(__instance, item);
    }
}

[HarmonyPatch(typeof(Inventory), "AddAt")]
internal static class Patch_Inventory_AddAt_LootPut
{
    private static bool Prefix(Inventory __instance, Item item, int atPosition, ref bool __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted) return true;

        // 只在客户端、且不是“应用服务器快照”阶段时干预
        if (!m.IsServer && !COOPManager.LootNet._applyingLootState)
        {
            // 本地 OnDead 的墓碑生成/填充：直接走原生 AddAt
            try
            {
                if (DeadLootSpawnContext.LocalOnDead)
                    return true;
            }
            catch
            {
            }

            var targetIsLoot = LootboxDetectUtil.IsLootboxInventory(__instance) && !LootboxDetectUtil.IsPrivateInventory(__instance);
            var srcInv = item ? item.InInventory : null;
            var srcIsLoot = LootboxDetectUtil.IsLootboxInventory(srcInv) && !LootboxDetectUtil.IsPrivateInventory(srcInv);

            // === A) 容器内换位 ===
            if (targetIsLoot && ReferenceEquals(srcInv, __instance))
            {
                var srcPos = __instance.GetIndex(item);
                if (srcPos == atPosition)
                {
                    __result = true;
                    return false;
                }

                var tk = COOPManager.LootNet.Client_SendLootTakeRequest(__instance, srcPos, null, -1, null);
                LootManager.Instance.NoteLootReorderPending(tk, __instance, atPosition);
                __result = true;
                return false;
            }

            // === B) 其它库存 -> 容器 ===
            if (targetIsLoot && srcInv && !ReferenceEquals(srcInv, __instance))
            {
                var srcPos = srcInv.GetIndex(item);
                if (srcPos >= 0)
                {
                    COOPManager.LootNet.Client_SendLootTakeRequest(srcInv, srcPos, __instance, atPosition, null);
                    __result = true;
                    return false;
                }
            }

            // === C) 容器 -> 其它库存（直接 PUT）===
            if (!targetIsLoot && srcIsLoot)
            {
                var srcPos = srcInv.GetIndex(item);
                if (srcPos >= 0)
                {
                    COOPManager.LootNet.Client_SendLootTakeRequest(srcInv, srcPos, __instance, atPosition, null);
                    __result = true;
                    return false;
                }
            }

            // === D) 直接往容器放（UI 上新建/拖入） ===
            var isLootInv = LootboxDetectUtil.IsLootboxInventory(__instance) && !LootboxDetectUtil.IsPrivateInventory(__instance);
            if (isLootInv)
            {
                COOPManager.LootNet.Client_SendLootPutRequest(__instance, item, atPosition);
                __result = false;
                return false;
            }
        }

        return true;
    }
}

[HarmonyPatch(typeof(Inventory), "AddItem")]
internal static class Patch_Inventory_AddItem_LootPut
{
    private static bool Prefix(Inventory __instance, Item item, ref bool __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted) return true;

        // 只在“真正的战利品容器初始化”时吞掉本地 Add
        if (!m.IsServer && m.ClientLootSetupActive)
        {
            var isLootInv = LootboxDetectUtil.IsLootboxInventory(__instance)
                            && !LootboxDetectUtil.IsPrivateInventory(__instance);
            if (isLootInv)
            {
                try
                {
                    if (item)
                    {
                        item.Detach();
                        Object.Destroy(item.gameObject);
                    }
                }
                catch
                {
                }

                __result = true;
                return false;
            }
        }

        if (!m.IsServer && !COOPManager.LootNet._applyingLootState)
        {
            var isLootInv = LootboxDetectUtil.IsLootboxInventory(__instance)
                            && !LootboxDetectUtil.IsPrivateInventory(__instance);
            if (isLootInv)
            {
                COOPManager.LootNet.Client_SendLootPutRequest(__instance, item, 0);
                __result = false;
                return false;
            }
        }

        return true;
    }
}

// === 主机：Inventory.AddAt 成功后广播 ===
[HarmonyPatch(typeof(Inventory), "AddAt")]
internal static class Patch_Inventory_AddAt_BroadcastOnServer
{
    private static void Postfix(Inventory __instance, Item item, int atPosition, bool __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || !m.IsServer) return;
        if (!__result || COOPManager.LootNet._serverApplyingLoot) return;
        if (!LootboxDetectUtil.IsLootboxInventory(__instance)) return;

        if (!LootboxDetectUtil.IsLootboxInventory(__instance) || LootboxDetectUtil.IsPrivateInventory(__instance)) return;

        COOPManager.LootNet.Server_SendLootboxState(null, __instance);
    }
}

// === 主机：Inventory.AddItem 成功后广播 ===
[HarmonyPatch(typeof(Inventory), "AddItem")]
internal static class Patch_Inventory_AddItem_BroadcastLootState
{
    private static void Postfix(Inventory __instance, Item item, bool __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || !m.IsServer) return;
        if (!__result || COOPManager.LootNet._serverApplyingLoot) return;

        if (!LootboxDetectUtil.IsLootboxInventory(__instance) || LootboxDetectUtil.IsPrivateInventory(__instance)) return;


        var dict = InteractableLootbox.Inventories;
        var isLootInv = dict != null && dict.ContainsValue(__instance);
        if (!isLootInv) return;

        COOPManager.LootNet.Server_SendLootboxState(null, __instance);
    }
}

// 主机：Inventory.RemoveAt 成功后广播 —— 修复“主机本地拿走，客户端不刷”的幽灵物品
[HarmonyPatch(typeof(Inventory))]
internal static class Patch_Inventory_RemoveAt_BroadcastOnServer
{
    // 精确锁定 RemoveAt(int, out Item) 这个重载
    private static MethodBase TargetMethod()
    {
        var tInv = typeof(Inventory);
        var tItemByRef = typeof(Item).MakeByRefType();
        return AccessTools.Method(tInv, "RemoveAt", new[] { typeof(int), tItemByRef });
    }

    // Postfix：当主机本地从“公共战利品容器”取出成功后，广播一次全量状态
    private static void Postfix(Inventory __instance, int position, Item __1, bool __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || !m.IsServer) return; // 仅主机
        if (!__result || COOPManager.LootNet._serverApplyingLoot) return; // 跳过失败/网络路径内部调用
        if (!LootboxDetectUtil.IsLootboxInventory(__instance)) return; // 只处理战利品容器
        if (LootboxDetectUtil.IsPrivateInventory(__instance)) return; // 跳过玩家仓库/宠物包等私有库存

        COOPManager.LootNet.Server_SendLootboxState(null, __instance); // 广播给所有客户端
    }
}

// 3) 有些路径会直接 Inventory.AddAt(...)（不走 AddAndMerge），同样要拦
[HarmonyPatch(typeof(Inventory), "AddAt")]
[HarmonyPriority(Priority.First)]
internal static class Patch_AddAt_SplitFirst
{
    private static bool Prefix(Inventory __instance, Item item, int atPosition, ref bool __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || m.IsServer) return true;

        if (__instance == null || item == null) return true;
        if (!LootboxDetectUtil.IsLootboxInventory(__instance) || LootboxDetectUtil.IsPrivateInventory(__instance))
            return true;

        if (!ModBehaviourF.map.TryGetValue(item.GetInstanceID(), out var p)) return true;
        if (!ReferenceEquals(p.inv, __instance)) return true;

        COOPManager.LootNet.Client_SendLootSplitRequest(__instance, p.srcPos, p.count, atPosition);

        try
        {
            if (item)
            {
                item.Detach();
                Object.Destroy(item.gameObject);
            }
        }
        catch
        {
        }

        ModBehaviourF.map.Remove(item.GetInstanceID());

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.Sort), new Type[] { })]
internal static class Patch_Inventory_Sort_BlockLocalInLoot
{
    private static bool Prefix(Inventory __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.IsClient) return true; // 主机/单机场景放行
        if (COOPManager.LootNet.ApplyingLootState) return true; // 应用服务器快照时放行（用于UI重建）
        if (!LootManager.IsCurrentLootInv(__instance)) return true; // 只拦当前战利品容器
        if (LootboxDetectUtil.IsPrivateInventory(__instance)) return true; // 私有库存放行

        // 这里可选：发一个“请求合并/整理”的网络指令给主机，让主机执行再广播；
        // 若不想加协议，先纯拦也行，至少不会再制造幽灵。
        return false; // 阻止原始 Sort()
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveAt))]
internal static class Patch_ServerBroadcast_OnRemoveAt
{
    private static void Postfix(Inventory __instance, int position, Item removedItem, bool __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || !m.IsServer) return;
        if (!__result || COOPManager.LootNet._serverApplyingLoot) return;
        if (!LootboxDetectUtil.IsLootboxInventory(__instance) || LootboxDetectUtil.IsPrivateInventory(__instance)) return;

        if (LootManager.Instance.Server_IsLootMuted(__instance)) return; // ★ 新增
        COOPManager.LootNet.Server_SendLootboxState(null, __instance);
    }
}

// Inventory.AddAt 主机本地往容器放入（含：从武器卸下再放回容器）
[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddAt))]
internal static class Patch_ServerBroadcast_OnAddAt
{
    // 死亡填充场景：在 AddAt 前给该容器加“静音窗口”，屏蔽本次及紧随其后的群发
    private static void Prefix(Inventory __instance)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || !m.IsServer) return;

        // 仅在 AI 死亡 OnDead 流程里触发（你项目里已有这个上下文标记）
        var deadOwner = DeadLootSpawnContext.InOnDead;
        if (deadOwner == null) return;

        // 如果是本地玩家死亡，放行原逻辑，避免静音窗口吞掉墓碑的物品填充/广播
        try
        {
            if (CharacterMainControl.Main == deadOwner) return;
        }
        catch
        {
        }

        if (!LootboxDetectUtil.IsLootboxInventory(__instance) || LootboxDetectUtil.IsPrivateInventory(__instance)) return;
        LootManager.Instance.Server_MuteLoot(__instance, 1.0f); // 1秒静音足够覆盖整次填充
    }

    private static void Postfix(Inventory __instance, Item item, int atPosition, bool __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || !m.IsServer) return;
        if (!__result || COOPManager.LootNet._serverApplyingLoot) return;
        if (!LootboxDetectUtil.IsLootboxInventory(__instance) || LootboxDetectUtil.IsPrivateInventory(__instance)) return;

        // ★ 新增：静音期内跳过群发（真正有人打开时仍会单播，应答不受影响）
        if (LootManager.Instance.Server_IsLootMuted(__instance)) return;

        COOPManager.LootNet.Server_SendLootboxState(null, __instance);
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddAt), typeof(Item), typeof(int))]
internal static class Patch_Inventory_AddAt_FlagUninspected_WhenApplyingLoot
{
    private static void Postfix(Inventory __instance, Item item)
    {
        ApplyUninspectedFlag(__instance, item);
    }

    private static void ApplyUninspectedFlag(Inventory inv, Item item)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || mod.IsServer) return;


        if (!COOPManager.LootNet.ApplyingLootState) return;

        if (LootboxDetectUtil.IsPrivateInventory(inv)) return;
        if (!(LootboxDetectUtil.IsLootboxInventory(inv) || LootManager.IsCurrentLootInv(inv))) return;

        try
        {
            var last = inv.GetLastItemPosition();
            var hasUninspected = false;
            for (var i = 0; i <= last; i++)
            {
                var it = inv.GetItemAt(i);
                if (it != null && !it.Inspected)
                {
                    hasUninspected = true;
                    break;
                }
            }

            inv.NeedInspection = hasUninspected;
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(Inventory), "AddItem", typeof(Item))]
internal static class Patch_Inventory_AddItem_FlagUninspected_WhenApplyingLoot
{
    private static void Postfix(Inventory __instance, Item item)
    {
        ApplyUninspectedFlag(__instance, item);
    }

    private static void ApplyUninspectedFlag(Inventory inv, Item item)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || mod.IsServer) return;

        if (!COOPManager.LootNet.ApplyingLootState) return;

        if (LootboxDetectUtil.IsPrivateInventory(inv)) return;
        if (!(LootboxDetectUtil.IsLootboxInventory(inv) || LootManager.IsCurrentLootInv(inv))) return;

        try
        {
            var last = inv.GetLastItemPosition();
            var hasUninspected = false;
            for (var i = 0; i <= last; i++)
            {
                var it = inv.GetItemAt(i);
                if (it != null && !it.Inspected)
                {
                    hasUninspected = true;
                    break;
                }
            }

            inv.NeedInspection = hasUninspected;
        }
        catch
        {
        }
    }
}