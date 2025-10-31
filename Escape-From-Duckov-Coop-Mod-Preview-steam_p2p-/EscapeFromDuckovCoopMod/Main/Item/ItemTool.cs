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

using ItemStatsSystem;
using static EscapeFromDuckovCoopMod.LootNet;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

public static class ItemTool
{
    public static uint nextDropId = 1;

    public static uint nextLocalDropToken = 1; // 客户端本地 token（用来忽略自己 echo 回来的 SPAWN）

    public static readonly Dictionary<uint, Item> serverDroppedItems = COOPManager.ItemHandle.serverDroppedItems; // 主机记录
    public static readonly Dictionary<uint, Item> clientDroppedItems = COOPManager.ItemHandle.clientDroppedItems; // 客户端记录（可用于拾取等后续）

    public static bool _serverApplyingLoot; // 主机：处理客户端请求时抑制 Postfix 二次广播

    public static void AddNetDropTag(GameObject go, uint id)
    {
        if (!go) return;
        var tag = go.GetComponent<NetDropTag>() ?? go.AddComponent<NetDropTag>();
        tag.id = id;
    }

    public static void AddNetDropTag(Item item, uint id)
    {
        try
        {
            var ag = item?.ActiveAgent;
            if (ag && ag.gameObject) AddNetDropTag(ag.gameObject, id);
        }
        catch
        {
        }
    }

    // 取容器内列表（反射兜底）
    public static List<Item> TryGetInventoryItems(Inventory inv)
    {
        if (inv == null) return null;

        var list = inv.Content;
        return list;
    }

    // 向容器添加（反射兜底）
    public static bool TryAddToInventory(Inventory inv, Item child)
    {
        if (inv == null || child == null) return false;
        try
        {
            // 统一走“合并 + 放入”，内部会在需要时 Detach
            return inv.AddAndMerge(child);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ITEM] Inventory.Add* 失败: {e.Message}");
            try
            {
                child.Detach();
                return inv.AddItem(child);
            }
            catch
            {
            }
        }

        return false;
    }

    public static uint AllocateDropId()
    {
        var id = nextDropId++;
        while (serverDroppedItems.ContainsKey(id))
            id = nextDropId++;
        return id;
    }

    public static async UniTaskVoid Server_DoSplitAsync(
        Inventory inv, int srcPos, int count, int prefer)
    {
        _serverApplyingLoot = true;
        try
        {
            var srcItem = inv.GetItemAt(srcPos);
            if (!srcItem) return;

            // 1) 主机执行真正的拆分（源堆 -count）
            var newItem = await srcItem.Split(count);
            if (!newItem) return;

            // 2) 优先按 prefer 落到空格；没有空位才允许合并
            var dst = prefer;
            if (dst < 0 || inv.GetItemAt(dst)) dst = inv.GetFirstEmptyPosition(srcPos + 1);
            if (dst < 0) dst = inv.GetFirstEmptyPosition();

            var ok = false;
            if (dst >= 0) ok = inv.AddAt(newItem, dst); // 不合并
            if (!ok) ok = inv.AddAndMerge(newItem, srcPos + 1); // 兜底

            if (!ok)
            {
                try
                {
                    Object.Destroy(newItem.gameObject);
                }
                catch
                {
                }

                if (srcItem) srcItem.StackCount = srcItem.StackCount + count;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LOOT][SPLIT] exception: {ex}");
        }
        finally
        {
            _serverApplyingLoot = false;
            COOPManager.LootNet.Server_SendLootboxState(null, inv);
        }
    }


    public static ItemSnapshot MakeSnapshot(Item item)
    {
        ItemSnapshot s;
        s.typeId = item.TypeID;
        s.stack = item.StackCount;
        s.durability = item.Durability;
        s.durabilityLoss = item.DurabilityLoss;
        s.inspected = item.Inspected;
        s.slots = new List<(string, ItemSnapshot)>();
        s.inventory = new List<ItemSnapshot>();

        var slots = item.Slots;
        if (slots != null && slots.list != null)
            foreach (var slot in slots.list)
                if (slot != null && slot.Content != null)
                    s.slots.Add((slot.Key ?? string.Empty, MakeSnapshot(slot.Content)));

        var invItems = TryGetInventoryItems(item.Inventory);
        if (invItems != null)
            foreach (var child in invItems)
                if (child != null)
                    s.inventory.Add(MakeSnapshot(child));

        return s;
    }

    //写物品快照！！
    public static void WriteItemSnapshot(NetDataWriter w, Item item)
    {
        w.Put(item.TypeID);
        w.Put(item.StackCount);
        w.Put(item.Durability);
        w.Put(item.DurabilityLoss);
        w.Put(item.Inspected);

        // Slots：只写“有内容”的槽
        var slots = item.Slots;
        if (slots != null && slots.list != null)
        {
            var filled = 0;
            foreach (var s in slots.list)
                if (s != null && s.Content != null)
                    filled++;
            w.Put((ushort)filled);
            foreach (var s in slots.list)
            {
                if (s == null || s.Content == null) continue;
                w.Put(s.Key ?? string.Empty);
                WriteItemSnapshot(w, s.Content);
            }
        }
        else
        {
            w.Put((ushort)0);
        }

        // Inventory：**只写非空**，不写任何占位
        var invItems = TryGetInventoryItems(item.Inventory);
        if (invItems != null)
        {
            var valid = new List<Item>(invItems.Count);
            foreach (var c in invItems)
                if (c != null)
                    valid.Add(c);

            w.Put((ushort)valid.Count);
            foreach (var child in valid)
                WriteItemSnapshot(w, child);
        }
        else
        {
            w.Put((ushort)0);
        }
    }

    // 读快照
    public static ItemSnapshot ReadItemSnapshot(NetPacketReader r)
    {
        ItemSnapshot s;
        s.typeId = r.GetInt();
        s.stack = r.GetInt();
        s.durability = r.GetFloat();
        s.durabilityLoss = r.GetFloat();
        s.inspected = r.GetBool();
        s.slots = new List<(string, ItemSnapshot)>();
        s.inventory = new List<ItemSnapshot>();

        int slotsCount = r.GetUShort();
        for (var i = 0; i < slotsCount; i++)
        {
            var key = r.GetString();
            var child = ReadItemSnapshot(r);
            s.slots.Add((key, child));
        }

        int invCount = r.GetUShort();
        for (var i = 0; i < invCount; i++)
        {
            var child = ReadItemSnapshot(r);
            s.inventory.Add(child);
        }

        return s;
    }

    // 用快照构建实例（递归）
    public static Item BuildItemFromSnapshot(ItemSnapshot s)
    {
        Item item = null;
        try
        {
            item = COOPManager.GetItemAsync(s.typeId).Result;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ITEM] 实例化失败 typeId={s.typeId}, err={e}");
            return null;
        }

        if (item == null) return null;
        ApplySnapshotToItem(item, s);
        return item;
    }


    // 把快照写回到 item（递归挂接）
    public static void ApplySnapshotToItem(Item item, ItemSnapshot s)
    {
        try
        {
            // 仅可堆叠才设置数量，避免“不可堆叠，无法设置数量”
            if (item.Stackable)
            {
                var target = s.stack;
                if (target < 1) target = 1;
                try
                {
                    target = Mathf.Clamp(target, 1, item.MaxStackCount);
                }
                catch
                {
                }

                item.StackCount = target;
            }

            item.Durability = s.durability;
            item.DurabilityLoss = s.durabilityLoss;
            item.Inspected = s.inspected;

            // Slots
            if (s.slots != null && s.slots.Count > 0 && item.Slots != null)
                foreach (var (key, childSnap) in s.slots)
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    var slot = item.Slots.GetSlot(key);
                    if (slot == null)
                    {
                        Debug.LogWarning($"[ITEM] 找不到槽位 key={key} on {item.DisplayName}");
                        continue;
                    }

                    var child = BuildItemFromSnapshot(childSnap);
                    if (child == null) continue;
                    if (!slot.Plug(child, out _))
                        TryAddToInventory(item.Inventory, child);
                }

            // 容器内容
            if (s.inventory != null && s.inventory.Count > 0)
                foreach (var childSnap in s.inventory)
                {
                    var child = BuildItemFromSnapshot(childSnap);
                    if (child == null) continue;
                    TryAddToInventory(item.Inventory, child);
                }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ITEM] ApplySnapshot 出错: {e}");
        }
    }
}