using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Utils.Database;

/// <summary>
/// 数据库使用示例和性能测试
/// </summary>
public class DatabaseUsageExample : MonoBehaviour
{
    private GameItemDatabase _itemDb;

    void Start()
    {
        // 初始化数据库（10米网格大小）
        _itemDb = new GameItemDatabase(spatialCellSize: 10f);
    }

    #region 基础使用示例

    /// <summary>
    /// 示例1：添加物品
    /// </summary>
    void Example_AddItem()
    {
        var lootBox = new GameObject("LootBox_001");
        lootBox.transform.position = new Vector3(10, 0, 20);
        
        // 添加到数据库
        _itemDb.AddItem(
            go: lootBox,
            setId: "loot_001",
            itemType: "LootBox",
            ownerId: 1
        );
    }

    /// <summary>
    /// 示例2：快速查询（O(1)）
    /// </summary>
    void Example_FastQuery()
    {
        // 按 SetId 查询
        var item = _itemDb.GetItemBySetId("loot_001");
        
        // 按类型查询
        var allLootBoxes = _itemDb.GetItemsByType("LootBox");
        
        // 按所有者查询
        var playerItems = _itemDb.GetItemsByOwner(1);
        
        // 空间查询（50米范围内）
        var nearbyItems = _itemDb.GetItemsInRadius(transform.position, 50f);
    }

    /// <summary>
    /// 示例3：复杂条件查询
    /// </summary>
    void Example_ComplexQuery()
    {
        // 查询特定场景中，特定类型的物品
        var items = _itemDb.FindItems(e => 
            e.SceneName == "MainScene" && 
            e.ItemType == "LootBox" &&
            e.OwnerId == -1
        );
        
        // 查询最近创建的物品
        var recentItems = _itemDb.FindItems(e => 
            (DateTime.Now - e.CreatedAt).TotalSeconds < 60
        );
    }

    /// <summary>
    /// 示例4：批量操作
    /// </summary>
    void Example_BulkOperations()
    {
        // 批量添加
        var allLootBoxes = GameObject.FindGameObjectsWithTag("LootBox");
        int added = _itemDb.BulkAddItems(
            allLootBoxes,
            setIdGetter: go => go.GetComponent<ItemComponent>()?.SetId,
            typeGetter: go => "LootBox"
        );
        
        UnityEngine.Debug.Log($"批量添加了 {added} 个物品");
        
        // 批量删除（删除特定场景的物品）
        int removed = _itemDb.BulkRemoveItems(e => e.SceneName == "OldScene");
        UnityEngine.Debug.Log($"批量删除了 {removed} 个物品");
        
        // 清理无效物品
        int cleaned = _itemDb.CleanupInvalidItems();
        UnityEngine.Debug.Log($"清理了 {cleaned} 个无效物品");
    }

    /// <summary>
    /// 示例5：自定义数据
    /// </summary>
    void Example_CustomData()
    {
        // 存储自定义数据
        _itemDb.SetCustomData("loot_001", "LastSyncTime", DateTime.Now);
        _itemDb.SetCustomData("loot_001", "IsSynced", true);
        
        // 读取自定义数据
        var lastSync = _itemDb.GetCustomData("loot_001", "LastSyncTime");
        var isSynced = _itemDb.GetCustomData("loot_001", "IsSynced");
    }

    #endregion

    #region 实际应用场景

    /// <summary>
    /// 场景1：同步附近玩家的物品
    /// </summary>
    IEnumerator SyncNearbyItems(Vector3 playerPosition)
    {
        // 只同步50米内的物品
        var nearbyItems = _itemDb.GetItemsInRadius(playerPosition, 50f);
        
        int count = 0;
        foreach (var item in nearbyItems)
        {
            // 同步物品到客户端
            SyncItemToClient(item);
            
            // 每10个物品暂停一帧，避免卡顿
            if (++count % 10 == 0)
                yield return null;
        }
    }

    /// <summary>
    /// 场景2：清理远离玩家的物品
    /// </summary>
    void CleanupDistantItems(Vector3 playerPosition, float maxDistance)
    {
        int removed = _itemDb.BulkRemoveItems(e =>
        {
            var distance = Vector3.Distance(e.Position, playerPosition);
            return distance > maxDistance;
        });
        
        UnityEngine.Debug.Log($"清理了 {removed} 个远距离物品");
    }

    /// <summary>
    /// 场景3：按类型统计物品
    /// </summary>
    void ShowItemStatistics()
    {
        var countByType = _itemDb.GetItemCountByType();
        
        UnityEngine.Debug.Log("=== 物品统计 ===");
        foreach (var kvp in countByType)
        {
            UnityEngine.Debug.Log($"{kvp.Key}: {kvp.Value} 个");
        }
        UnityEngine.Debug.Log($"总计: {_itemDb.Count} 个");
    }

    #endregion

    #region 性能测试

    /// <summary>
    /// 性能测试：对比传统遍历 vs 数据库查询
    /// </summary>
    void PerformanceTest()
    {
        const int itemCount = 10000;
        
        // 准备测试数据
        var testItems = new GameObject[itemCount];
        for (int i = 0; i < itemCount; i++)
        {
            var go = new GameObject($"Item_{i}");
            go.transform.position = new Vector3(
                UnityEngine.Random.Range(-500f, 500f),
                0,
                UnityEngine.Random.Range(-500f, 500f)
            );
            testItems[i] = go;
            
            _itemDb.AddItem(go, $"item_{i}", "TestItem");
        }

        var sw = new Stopwatch();
        var testPosition = Vector3.zero;

        // 测试1：传统遍历查找附近物品
        sw.Restart();
        int count1 = 0;
        foreach (var item in testItems)
        {
            if (Vector3.Distance(item.transform.position, testPosition) <= 50f)
                count1++;
        }
        sw.Stop();
        UnityEngine.Debug.Log($"传统遍历: {sw.ElapsedMilliseconds}ms, 找到 {count1} 个");

        // 测试2：数据库空间查询
        sw.Restart();
        int count2 = 0;
        foreach (var item in _itemDb.GetItemsInRadius(testPosition, 50f))
        {
            count2++;
        }
        sw.Stop();
        UnityEngine.Debug.Log($"数据库查询: {sw.ElapsedMilliseconds}ms, 找到 {count2} 个");

        // 测试3：按 SetId 查询
        sw.Restart();
        var found = _itemDb.GetItemBySetId("item_5000");
        sw.Stop();
        UnityEngine.Debug.Log($"按ID查询: {sw.ElapsedTicks} ticks (纳秒级)");

        // 清理
        foreach (var item in testItems)
            Destroy(item);
        _itemDb.Clear();
    }

    #endregion

    #region JSON 导出示例

    /// <summary>
    /// 示例6：JSON 导出
    /// </summary>
    void Example_JsonExport()
    {
        // 导出整库
        var json = _itemDb.ExportToJson(indented: true);
        UnityEngine.Debug.Log("整库 JSON:\n" + json);
        
        // 导出带统计信息
        var jsonWithStats = _itemDb.ExportToJsonWithStats(indented: true);
        UnityEngine.Debug.Log("带统计信息的 JSON:\n" + jsonWithStats);
        
        // 按类型导出
        var lootBoxJson = _itemDb.ExportByTypeToJson("LootBox", indented: true);
        UnityEngine.Debug.Log("LootBox JSON:\n" + lootBoxJson);
        
        // 按场景导出
        var sceneJson = _itemDb.ExportBySceneToJson("MainScene", indented: true);
        UnityEngine.Debug.Log("MainScene JSON:\n" + sceneJson);
        
        // 按范围导出
        var radiusJson = _itemDb.ExportInRadiusToJson(transform.position, 50f, indented: true);
        UnityEngine.Debug.Log("50米范围内 JSON:\n" + radiusJson);
        
        // 自定义条件导出
        var customJson = _itemDb.ExportToJson(
            e => e.ItemType == "Weapon" && e.OwnerId > 0,
            indented: true
        );
        UnityEngine.Debug.Log("自定义条件 JSON:\n" + customJson);
    }

    /// <summary>
    /// 示例7：保存 JSON 到文件
    /// </summary>
    void Example_SaveJsonToFile()
    {
        var json = _itemDb.ExportToJsonWithStats(indented: true);
        var filePath = System.IO.Path.Combine(
            UnityEngine.Application.persistentDataPath,
            $"item_database_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        );
        
        System.IO.File.WriteAllText(filePath, json);
        UnityEngine.Debug.Log($"数据库已导出到: {filePath}");
    }

    #endregion

    // 模拟同步方法
    private void SyncItemToClient(GameObject item)
    {
        // 实际的同步逻辑
    }
}

// 假设的物品组件
public class ItemComponent : MonoBehaviour
{
    public string SetId;
}
