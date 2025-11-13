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

using System.Collections;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(InteractableLootbox), "StartLoot")]
internal static class Patch_Lootbox_StartLoot_RequestState
{
    private static void Postfix(InteractableLootbox __instance, ref bool __result)
    {
        if (!__result) return;

        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || m.IsServer) return;

        var inv = __instance ? __instance.Inventory : null;
        if (inv == null) return;

        inv.Loading = true; // 先挂起 UI
        COOPManager.LootNet.Client_RequestLootState(inv); // 请求快照
        LootManager.Instance.KickLootTimeout(inv); // 每次开箱都拉起 1.5s 兜底，避免二次打开卡死
    }
}

[HarmonyPatch(typeof(InteractableLootbox), "OnInteractStop")]
internal static class Patch_Lootbox_OnInteractStop_DisableFogWhenAllInspected
{
    private static void Postfix(InteractableLootbox __instance)
    {
        var inv = __instance?.Inventory;
        if (inv == null) return;

        try
        {
            inv.NeedInspection = false;
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(InteractableLootbox), "get_Inventory")]
internal static class Patch_Lootbox_GetInventory_Safe
{
    // 已存在：异常/空返回时强制创建
    private static Exception Finalizer(InteractableLootbox __instance, ref Inventory __result, Exception __exception)
    {
        try
        {
            if (__instance != null && (__exception != null || __result == null))
            {
                var mCreate = AccessTools.Method(typeof(InteractableLootbox), "GetOrCreateInventory", new[] { typeof(InteractableLootbox) });
                if (mCreate != null)
                {
                    var inv = (Inventory)mCreate.Invoke(null, new object[] { __instance });
                    if (inv != null)
                    {
                        __result = inv;
                        return null; // 吞掉异常
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static void Postfix(InteractableLootbox __instance, ref Inventory __result)
    {
        if (__result != null) return;

        // 用 LevelManager.LootBoxInventories 做二次兜底
        try
        {
            var key = ModBehaviourF.Instance != null
                ? LootManager.Instance.ComputeLootKey(__instance.transform)
                : __instance.GetHashCode();

            // 看 InteractableLootbox.Inventories
            var dict1 = InteractableLootbox.Inventories;
            if (dict1 != null && dict1.TryGetValue(key, out var inv1) && inv1)
            {
                __result = inv1;
                return;
            }

            // 再看 LevelManager.LootBoxInventories
            var lm = LevelManager.Instance;
            var dict2 = lm != null ? LevelManager.LootBoxInventories : null;
            if (dict2 != null && dict2.TryGetValue(key, out var inv2) && inv2)
            {
                __result = inv2;

                // 顺便把 InteractableLootbox.Inventories 也对齐一次
                try
                {
                    if (dict1 != null) dict1[key] = inv2;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(InteractableLootbox), "get_Inventory")]
internal static class Patch_Lootbox_GetInventory_Register
{
    private static void Postfix(InteractableLootbox __instance, ref Inventory __result)
    {
        try
        {
            if (!__result) return;

            var key = ModBehaviourF.Instance != null
                ? LootManager.Instance.ComputeLootKey(__instance.transform)
                : __instance.GetHashCode();

            var dictA = InteractableLootbox.Inventories;
            if (dictA != null) dictA[key] = __result;

            var lm = LevelManager.Instance;
            var dictB = lm != null ? LevelManager.LootBoxInventories : null;
            if (dictB != null) dictB[key] = __result;
        }
        catch
        {
        }
    }
}

// 阻断：客户端在“死亡路径”里不要本地创建（避免双生）
[HarmonyPatch(typeof(InteractableLootbox), "CreateFromItem")]
internal static class Patch_Lootbox_CreateFromItem_BlockClient
{
    private static bool Prefix()
    {
        var mod = ModBehaviourF.Instance;
        if (mod != null && mod.networkStarted && !mod.IsServer && DeadLootSpawnContext.InOnDead != null)
            return false; // 客户端处于OnDead路径→禁止本地创建
        return true;
    }
}

// ★ 修复：在CreateFromItem之前，确保AI的装备槽能被正确读取
[HarmonyPatch(typeof(InteractableLootbox), "CreateFromItem")]
[HarmonyPriority(Priority.VeryHigh)]
internal static class Patch_Lootbox_CreateFromItem_EnsureAISlots
{
    private static void Prefix(Item item)
    {
        var mod = ModBehaviourF.Instance;
        var dead = DeadLootSpawnContext.InOnDead;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;
        if (dead == null || item == null) return;

        try
        {
            // 确保characterItem的Slots引用正确指向EquipmentController的槽位
            var equipmentController = dead.EquipmentController;
            if (equipmentController == null) return;

            // 获取characterItem的Slots
            var itemSlots = item.Slots;
            if (itemSlots == null || itemSlots.list == null) return;

            var slotNames = new[] { "armorSlot", "helmatSlot", "faceMaskSlot", "backpackSlot", "headsetSlot" };

            Debug.Log($"[AI-LOOT] 开始检查AI装备槽，characterItem={item.DisplayName}, Slots.list.Count={itemSlots.list.Count}");

            // 遍历装备控制器的槽位，确保它们在characterItem.Slots中
            foreach (var slotName in slotNames)
            {
                try
                {
                    var slotField = Traverse.Create(equipmentController).Field<Slot>(slotName);
                    if (slotField?.Value == null) continue;

                    var equipSlot = slotField.Value;
                    var content = equipSlot.Content;

                    if (content != null)
                    {
                        // 检查这个槽位是否已经在characterItem.Slots中
                        bool found = false;
                        foreach (var slot in itemSlots.list)
                        {
                            if (slot != null && ReferenceEquals(slot, equipSlot))
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            // 添加到Slots列表中
                            itemSlots.list.Add(equipSlot);
                            Debug.Log($"[AI-LOOT] 添加装备槽到Slots: {slotName}, 内容: {content.DisplayName}");
                        }
                        else
                        {
                            Debug.Log($"[AI-LOOT] 装备槽已存在: {slotName}, 内容: {content.DisplayName}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AI-LOOT] 处理装备槽 {slotName} 时出错: {ex.Message}");
                }
            }

            Debug.Log($"[AI-LOOT] 装备槽检查完成，最终Slots.list.Count={itemSlots.list.Count}");

            // 同时检查Inventory中的物品
            var itemInv = item.Inventory;
            if (itemInv != null)
            {
                Debug.Log($"[AI-LOOT] characterItem.Inventory物品数量: {itemInv.Content.Count}");
                for (int i = 0; i < itemInv.Content.Count; i++)
                {
                    var invItem = itemInv.GetItemAt(i);
                    if (invItem != null)
                    {
                        Debug.Log($"[AI-LOOT] Inventory物品 {i}: {invItem.DisplayName}");
                    }
                }
            }
            else
            {
                Debug.LogWarning("[AI-LOOT] characterItem.Inventory is null!");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[AI-LOOT] EnsureAISlots failed: {e}");
        }
    }
}

// 广播：服务端在 CreateFromItem 返回实例的这一刻立即广播 spawn + state
[HarmonyPatch(typeof(InteractableLootbox), "CreateFromItem")]
internal static class Patch_Lootbox_CreateFromItem_DeferredSpawn
{
    private static void Postfix(InteractableLootbox __result)
    {
        var mod = ModBehaviourF.Instance;
        var dead = DeadLootSpawnContext.InOnDead;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;
        if (dead == null || !__result) return;

        mod.StartCoroutine(DeferredSpawn(__result, dead));
    }

    private static IEnumerator DeferredSpawn(InteractableLootbox box, CharacterMainControl who)
    {
        yield return null;
        var mod = ModBehaviourF.Instance;
        if (mod && mod.IsServer && box) DeadLootBox.Instance.Server_OnDeadLootboxSpawned(box, who);
    }
}

[HarmonyPatch(typeof(InteractableLootbox), "CreateFromItem")]
internal static class Patch_Lootbox_CreateFromItem_Register
{
    private static void Postfix(InteractableLootbox __result)
    {
        try
        {
            return;
            //之前排查击杀卡顿ret了的，优化好了一点之后就忘记放行了好像也不会影响正常的东西，能用就先别管：）

            if (!__result) return;
            var inv = __result.Inventory;
            if (!inv) return;

            var key = ModBehaviourF.Instance != null
                ? LootManager.Instance.ComputeLootKey(__result.transform)
                : __result.GetHashCode(); // 兜底

            var dict = InteractableLootbox.Inventories;
            if (dict != null) dict[key] = inv;
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(InteractableLootbox), "CreateFromItem")]
[HarmonyPriority(Priority.High)]
internal static class Patch_Lootbox_CreateFromItem_DeferOnServerFromOnDead
{
    // 防止我们在协程里再次调用 CreateFromItem 时又被自己拦截
    [ThreadStatic] private static bool _bypassDefer;

    private static bool Prefix(
        Item item,
        Vector3 position,
        Quaternion rotation,
        bool moveToMainScene,
        InteractableLootbox prefab,
        bool filterDontDropOnDead,
        ref InteractableLootbox __result)
    {
        var mod = ModBehaviourF.Instance;
        var dead = DeadLootSpawnContext.InOnDead;

        // 仅在：联机 + 服务端 + 正处于 OnDead 路径 时延帧，其余情况不动
        if (_bypassDefer || mod == null || !mod.networkStarted || !mod.IsServer || dead == null)
            return true;

        mod.StartCoroutine(DeferOneFrame(
            item, position, rotation, moveToMainScene, prefab, filterDontDropOnDead, dead
        ));

        // 原调用方（OnDead）不依赖立即返回值，先置空并跳过本帧
        __result = null;
        return false;
    }

    private static IEnumerator DeferOneFrame(
        Item item,
        Vector3 position,
        Quaternion rotation,
        bool moveToMainScene,
        InteractableLootbox prefab,
        bool filterDontDropOnDead,
        CharacterMainControl deadOwner)
    {
        yield return null;

        var old = DeadLootSpawnContext.InOnDead;
        DeadLootSpawnContext.InOnDead = deadOwner;

        _bypassDefer = true;
        try
        {
            InteractableLootbox.CreateFromItem(
                item, position, rotation, moveToMainScene, prefab, filterDontDropOnDead
            );
        }
        finally
        {
            _bypassDefer = false;
            DeadLootSpawnContext.InOnDead = old;
        }
    }
}

[HarmonyPatch(typeof(InteractableLootbox), "StartLoot")]
internal static class Patch_Lootbox_StartLoot_RequestState_AndPrime
{
    private static void Postfix(InteractableLootbox __instance)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || m.IsServer) return;

        var inv = __instance ? __instance.Inventory : null;
        if (!inv) return;

        try
        {
            inv.Loading = true;
        }
        catch
        {
        }

        COOPManager.LootNet.Client_RequestLootState(inv);
        LootManager.Instance.KickLootTimeout(inv);

        if (!LootboxDetectUtil.IsPrivateInventory(inv) && LootboxDetectUtil.IsLootboxInventory(inv))
        {
            try
            {
                inv.NeedInspection = false;
            }
            catch
            {
            }

            try
            {
                __instance.needInspect = false;
            }
            catch
            {
            }
        }
    }
}