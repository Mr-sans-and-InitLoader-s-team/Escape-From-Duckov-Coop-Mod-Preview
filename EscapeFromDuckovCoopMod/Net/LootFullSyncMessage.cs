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

using LiteNetLib;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

/// <summary>
/// 战利品箱全量同步消息
/// 在SCENE_GATE_RELEASE后一次性同步所有战利品箱
/// </summary>
public static class LootFullSyncMessage
{
    /// <summary>
    /// 战利品箱数据结构
    /// </summary>
    [System.Serializable]
    public class LootBoxData
    {
        public string type = "lootFullSync";
        public LootBoxInfo[] lootBoxes;
        public string timestamp;
    }

    /// <summary>
    /// 单个战利品箱信息
    /// </summary>
    [System.Serializable]
    public class LootBoxInfo
    {
        public int lootUid;              // 战利品箱唯一ID
        public int aiId;                 // 关联的AI ID（如果是AI掉落）
        public Vector3Serializable position;        // 位置
        public Vector3Serializable rotation;        // 旋转（欧拉角）
        public int capacity;             // 容量
        public LootItemInfo[] items;     // 物品列表
    }

    /// <summary>
    /// 战利品箱中的物品信息
    /// </summary>
    [System.Serializable]
    public class LootItemInfo
    {
        public int position;             // 在容器中的位置
        public int typeId;               // 物品类型ID
        public int stack;                // 堆叠数量
        public float durability;         // 耐久度
        public float durabilityLoss;     // 耐久度损失
        public bool inspected;           // 是否已检查
        // 注意：附件和容器内容暂不支持，需要更复杂的序列化
    }

    /// <summary>
    /// 可序列化的Vector3
    /// </summary>
    [System.Serializable]
    public class Vector3Serializable
    {
        public float x, y, z;

        public Vector3Serializable(Vector3 v)
        {
            x = v.x;
            y = v.y;
            z = v.z;
        }

        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }
    }

    /// <summary>
    /// 主机：在SCENE_GATE_RELEASE后收集并发送所有战利品箱数据
    /// </summary>
    public static void Host_SendLootFullSync(NetPeer peer)
    {
        var service = NetService.Instance;
        if (service == null || !service.IsServer)
        {
            Debug.LogWarning("[LootFullSync] 只能在主机端调用");
            return;
        }

        try
        {
            // 收集所有战利品箱数据
            var lootBoxes = CollectAllLootBoxes();

            var data = new LootBoxData
            {
                lootBoxes = lootBoxes,
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };

            // 发送JSON消息
            JsonMessage.SendToPeer(peer, data, DeliveryMethod.ReliableOrdered);

            Debug.Log($"[LootFullSync] 发送战利品箱全量同步: {lootBoxes.Length} 个箱子 → {peer.EndPoint}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[LootFullSync] 发送失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// 主机：广播给所有客户端
    /// </summary>
    public static void Host_BroadcastLootFullSync()
    {
        var service = NetService.Instance;
        if (service == null || !service.IsServer)
        {
            Debug.LogWarning("[LootFullSync] 只能在主机端调用");
            return;
        }

        var netManager = service.netManager;
        if (netManager == null || netManager.ConnectedPeerList.Count == 0)
        {
            Debug.Log("[LootFullSync] 没有连接的客户端，跳过广播");
            return;
        }

        try
        {
            // 收集所有战利品箱数据
            var lootBoxes = CollectAllLootBoxes();

            var data = new LootBoxData
            {
                lootBoxes = lootBoxes,
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };

            // 广播给所有客户端
            JsonMessage.BroadcastToAllClients(data, DeliveryMethod.ReliableOrdered);

            Debug.Log($"[LootFullSync] 广播战利品箱全量同步: {lootBoxes.Length} 个箱子 → {netManager.ConnectedPeerList.Count} 个客户端");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[LootFullSync] 广播失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// 收集场景中所有战利品箱的数据
    /// </summary>
    private static LootBoxInfo[] CollectAllLootBoxes()
    {
        var lootBoxList = new List<LootBoxInfo>();

        // 查找场景中所有的InteractableLootbox
        var allLootBoxes = UnityEngine.Object.FindObjectsOfType<InteractableLootbox>();

        Debug.Log($"[LootFullSync] 找到 {allLootBoxes.Length} 个战利品箱");

        foreach (var lootBox in allLootBoxes)
        {
            if (lootBox == null)
                continue;

            try
            {
                var inventory = lootBox.Inventory;
                if (inventory == null)
                {
                    Debug.LogWarning($"[LootFullSync] 战利品箱 {lootBox.name} 没有Inventory");
                    continue;
                }

                // 获取lootUid（从_srvLootByUid反查）
                int lootUid = -1;
                var lootManager = LootManager.Instance;
                if (lootManager != null && lootManager._srvLootByUid != null)
                {
                    foreach (var kv in lootManager._srvLootByUid)
                    {
                        if (kv.Value == inventory)
                        {
                            lootUid = kv.Key;
                            break;
                        }
                    }
                }

                // 获取aiId（暂时设为0，AI关联信息待实现）
                int aiId = 0;

                // 收集物品数据
                var items = CollectLootBoxItems(inventory);

                var boxInfo = new LootBoxInfo
                {
                    lootUid = lootUid,
                    aiId = aiId,
                    position = new Vector3Serializable(lootBox.transform.position),
                    rotation = new Vector3Serializable(lootBox.transform.rotation.eulerAngles),
                    capacity = inventory.Capacity,
                    items = items
                };

                lootBoxList.Add(boxInfo);

                Debug.Log($"[LootFullSync] 收集战利品箱: lootUid={lootUid}, aiId={aiId}, pos={lootBox.transform.position}, items={items.Length}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LootFullSync] 收集战利品箱失败: {ex.Message}");
            }
        }

        return lootBoxList.ToArray();
    }

    /// <summary>
    /// 收集战利品箱中的所有物品
    /// </summary>
    private static LootItemInfo[] CollectLootBoxItems(Inventory inventory)
    {
        var itemList = new List<LootItemInfo>();

        if (inventory == null)
            return itemList.ToArray();

        // 获取容器内容
        var items = ItemTool.TryGetInventoryItems(inventory);
        if (items == null || items.Count == 0)
            return itemList.ToArray();

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item == null)
                continue;

            try
            {
                var itemInfo = new LootItemInfo
                {
                    position = i,
                    typeId = item.TypeID,
                    stack = item.StackCount,
                    durability = item.Durability,
                    durabilityLoss = item.DurabilityLoss,
                    inspected = item.Inspected
                };

                itemList.Add(itemInfo);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LootFullSync] 收集物品失败 (位置{i}): {ex.Message}");
            }
        }

        return itemList.ToArray();
    }

    /// <summary>
    /// 客户端：接收并应用战利品箱全量同步数据
    /// </summary>
    public static void Client_OnLootFullSync(string json)
    {
        var service = NetService.Instance;
        if (service == null)
        {
            Debug.LogWarning("[LootFullSync] NetService未初始化");
            return;
        }

        if (service.IsServer)
        {
            Debug.LogWarning("[LootFullSync] 主机不应该接收此消息");
            return;
        }

        try
        {
            var data = JsonUtility.FromJson<LootBoxData>(json);
            if (data == null || data.lootBoxes == null)
            {
                Debug.LogError("[LootFullSync] 解析数据失败");
                return;
            }

            Debug.Log($"[LootFullSync] 收到战利品箱全量同步: {data.lootBoxes.Length} 个箱子, 时间={data.timestamp}");

            // 应用数据
            ApplyLootBoxes(data.lootBoxes);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[LootFullSync] 处理失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// 应用战利品箱数据
    /// </summary>
    private static void ApplyLootBoxes(LootBoxInfo[] lootBoxes)
    {
        int successCount = 0;
        int failCount = 0;

        foreach (var boxInfo in lootBoxes)
        {
            try
            {
                ApplySingleLootBox(boxInfo);
                successCount++;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LootFullSync] 应用战利品箱失败 (lootUid={boxInfo.lootUid}): {ex.Message}");
                failCount++;
            }
        }

        Debug.Log($"[LootFullSync] 应用完成: 成功={successCount}, 失败={failCount}");
    }

    /// <summary>
    /// 应用单个战利品箱数据
    /// </summary>
    private static void ApplySingleLootBox(LootBoxInfo boxInfo)
    {
        // 1. 查找或创建战利品箱
        var lootBox = FindOrCreateLootBox(boxInfo);
        if (lootBox == null)
        {
            Debug.LogWarning($"[LootFullSync] 无法创建战利品箱: lootUid={boxInfo.lootUid}");
            return;
        }

        var inventory = lootBox.Inventory;
        if (inventory == null)
        {
            Debug.LogWarning($"[LootFullSync] 战利品箱没有Inventory: lootUid={boxInfo.lootUid}");
            return;
        }

        // 2. 设置容量
        try
        {
            inventory.SetCapacity(boxInfo.capacity);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[LootFullSync] 设置容量失败: {ex.Message}");
        }

        // 3. 清空现有物品
        var existingItems = ItemTool.TryGetInventoryItems(inventory);
        if (existingItems != null)
        {
            foreach (var item in existingItems.ToList())
            {
                if (item != null)
                {
                    try
                    {
                        UnityEngine.Object.Destroy(item.gameObject);
                    }
                    catch { }
                }
            }
            existingItems.Clear();
        }

        // 4. 添加物品
        foreach (var itemInfo in boxInfo.items)
        {
            try
            {
                // 创建ItemSnapshot
                var snapshot = new LootNet.ItemSnapshot
                {
                    typeId = itemInfo.typeId,
                    stack = itemInfo.stack,
                    durability = itemInfo.durability,
                    durabilityLoss = itemInfo.durabilityLoss,
                    inspected = itemInfo.inspected,
                    slots = null,
                    inventory = null
                };

                // 使用ItemTool创建物品实例
                var item = ItemTool.BuildItemFromSnapshot(snapshot);
                if (item == null)
                {
                    Debug.LogWarning($"[LootFullSync] 无法创建物品: typeId={itemInfo.typeId}");
                    continue;
                }

                // 添加到容器
                bool added = ItemTool.TryAddToInventory(inventory, item);
                if (!added)
                {
                    Debug.LogWarning($"[LootFullSync] 无法添加物品到容器: typeId={itemInfo.typeId}, pos={itemInfo.position}");
                    UnityEngine.Object.Destroy(item.gameObject);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LootFullSync] 添加物品失败 (位置{itemInfo.position}): {ex.Message}");
            }
        }

        // 6. 注册到LootManager
        var lootManager = LootManager.Instance;
        if (lootManager != null && boxInfo.lootUid >= 0)
        {
            lootManager._cliLootByUid[boxInfo.lootUid] = inventory;
        }

        Debug.Log($"[LootFullSync] 应用战利品箱: lootUid={boxInfo.lootUid}, items={boxInfo.items.Length}");
    }

    /// <summary>
    /// 查找或创建战利品箱
    /// </summary>
    private static InteractableLootbox FindOrCreateLootBox(LootBoxInfo boxInfo)
    {
        var position = boxInfo.position.ToVector3();
        var rotation = Quaternion.Euler(boxInfo.rotation.ToVector3());

        // 1. 尝试查找现有的战利品箱（在附近）
        var allLootBoxes = UnityEngine.Object.FindObjectsOfType<InteractableLootbox>();
        foreach (var existing in allLootBoxes)
        {
            if (Vector3.Distance(existing.transform.position, position) < 0.5f)
            {
                Debug.Log($"[LootFullSync] 找到现有战利品箱: lootUid={boxInfo.lootUid}, pos={position}");
                return existing;
            }
        }

        // 2. 创建新的战利品箱
        if (boxInfo.aiId > 0)
        {
            // 如果是AI掉落，使用DeadLootBox创建
            var deadLootBox = DeadLootBox.Instance;
            if (deadLootBox != null)
            {
                deadLootBox.SpawnDeadLootboxAt(boxInfo.aiId, boxInfo.lootUid, position, rotation);

                // 再次查找刚创建的箱子
                allLootBoxes = UnityEngine.Object.FindObjectsOfType<InteractableLootbox>();
                foreach (var existing in allLootBoxes)
                {
                    if (Vector3.Distance(existing.transform.position, position) < 0.5f)
                    {
                        Debug.Log($"[LootFullSync] 创建AI掉落箱: lootUid={boxInfo.lootUid}, aiId={boxInfo.aiId}");
                        return existing;
                    }
                }
            }
        }

        Debug.LogWarning($"[LootFullSync] 无法创建战利品箱: lootUid={boxInfo.lootUid}");
        return null;
    }
}
