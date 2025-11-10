using System;
using System.Diagnostics;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Utils.Database;

/// <summary>
/// 数据库性能测试（Mod 启动时自动执行）
/// </summary>
public class DatabasePerformanceTest
{
    private const int TEST_ITEM_COUNT = 10000;
    
    /// <summary>
    /// 执行完整性能测试
    /// </summary>
    public static void RunFullTest()
    {
        UnityEngine.Debug.Log("========================================");
        UnityEngine.Debug.Log("数据库性能测试开始");
        UnityEngine.Debug.Log($"测试数据量: {TEST_ITEM_COUNT} 条");
        UnityEngine.Debug.Log("========================================");

        var db = new GameItemDatabase(spatialCellSize: 10f);
        var testItems = new GameObject[TEST_ITEM_COUNT];
        var sw = new Stopwatch();

        // 测试1：写入性能
        UnityEngine.Debug.Log("\n[测试1] 写入性能测试");
        sw.Restart();
        for (int i = 0; i < TEST_ITEM_COUNT; i++)
        {
            var go = new GameObject($"TestItem_{i}");
            go.transform.position = new Vector3(
                UnityEngine.Random.Range(-500f, 500f),
                UnityEngine.Random.Range(-10f, 10f),
                UnityEngine.Random.Range(-500f, 500f)
            );
            testItems[i] = go;
            
            db.AddItem(
                go: go,
                setId: $"item_{i}",
                itemType: GetRandomItemType(),
                ownerId: UnityEngine.Random.Range(1, 11)
            );
        }
        sw.Stop();
        UnityEngine.Debug.Log($"✓ 写入 {TEST_ITEM_COUNT} 条数据耗时: {sw.ElapsedMilliseconds}ms");
        UnityEngine.Debug.Log($"  平均每条: {(float)sw.ElapsedMilliseconds / TEST_ITEM_COUNT:F3}ms");

        // 测试2：按主键查询
        UnityEngine.Debug.Log("\n[测试2] 主键查询性能测试");
        sw.Restart();
        for (int i = 0; i < 1000; i++)
        {
            var item = db.GetItemBySetId($"item_{UnityEngine.Random.Range(0, TEST_ITEM_COUNT)}");
        }
        sw.Stop();
        UnityEngine.Debug.Log($"✓ 查询 1000 次耗时: {sw.ElapsedMilliseconds}ms");
        UnityEngine.Debug.Log($"  平均每次: {(float)sw.ElapsedMilliseconds / 1000:F3}ms");

        // 测试3：按类型查询
        UnityEngine.Debug.Log("\n[测试3] 类型索引查询性能测试");
        sw.Restart();
        var lootBoxes = db.GetItemsByType("LootBox");
        int lootBoxCount = 0;
        foreach (var item in lootBoxes)
        {
            lootBoxCount++;
        }
        sw.Stop();
        UnityEngine.Debug.Log($"✓ 查询类型 'LootBox' 耗时: {sw.ElapsedMilliseconds}ms");
        UnityEngine.Debug.Log($"  找到 {lootBoxCount} 个物品");

        // 测试4：空间范围查询
        UnityEngine.Debug.Log("\n[测试4] 空间范围查询性能测试");
        var testPosition = Vector3.zero;
        sw.Restart();
        var nearbyItems = db.GetItemsInRadius(testPosition, 50f);
        int nearbyCount = 0;
        foreach (var item in nearbyItems)
        {
            nearbyCount++;
        }
        sw.Stop();
        UnityEngine.Debug.Log($"✓ 查询50米范围内物品耗时: {sw.ElapsedMilliseconds}ms");
        UnityEngine.Debug.Log($"  找到 {nearbyCount} 个物品");

        // 测试5：对比传统遍历
        UnityEngine.Debug.Log("\n[测试5] 对比传统遍历性能");
        sw.Restart();
        int traditionalCount = 0;
        foreach (var item in testItems)
        {
            if (item != null && Vector3.Distance(item.transform.position, testPosition) <= 50f)
                traditionalCount++;
        }
        sw.Stop();
        var traditionalTime = sw.ElapsedMilliseconds;
        UnityEngine.Debug.Log($"✓ 传统遍历耗时: {traditionalTime}ms");
        UnityEngine.Debug.Log($"  找到 {traditionalCount} 个物品");

        // 测试6：读取所有数据
        UnityEngine.Debug.Log("\n[测试6] 读取所有数据性能测试");
        sw.Restart();
        int totalCount = 0;
        foreach (var item in db.GetAllItems())
        {
            totalCount++;
        }
        sw.Stop();
        UnityEngine.Debug.Log($"✓ 读取所有 {totalCount} 条数据耗时: {sw.ElapsedMilliseconds}ms");

        // 测试7：复杂条件查询
        UnityEngine.Debug.Log("\n[测试7] 复杂条件查询性能测试");
        sw.Restart();
        var complexResults = db.FindItems(e => 
            e.ItemType == "Weapon" && 
            e.OwnerId > 5 &&
            e.Position.y > 0
        );
        int complexCount = 0;
        foreach (var item in complexResults)
        {
            complexCount++;
        }
        sw.Stop();
        UnityEngine.Debug.Log($"✓ 复杂条件查询耗时: {sw.ElapsedMilliseconds}ms");
        UnityEngine.Debug.Log($"  找到 {complexCount} 个物品");

        // 测试8：批量删除
        UnityEngine.Debug.Log("\n[测试8] 批量删除性能测试");
        sw.Restart();
        int deletedCount = db.BulkRemoveItems(e => e.OwnerId == 1);
        sw.Stop();
        UnityEngine.Debug.Log($"✓ 批量删除耗时: {sw.ElapsedMilliseconds}ms");
        UnityEngine.Debug.Log($"  删除了 {deletedCount} 个物品");
        UnityEngine.Debug.Log($"  剩余 {db.Count} 个物品");

        // 统计信息
        UnityEngine.Debug.Log("\n[统计信息]");
        var countByType = db.GetItemCountByType();
        foreach (var kvp in countByType)
        {
            UnityEngine.Debug.Log($"  {kvp.Key}: {kvp.Value} 个");
        }

        // 测试9：JSON 导出性能
        UnityEngine.Debug.Log("\n[测试9] JSON 导出性能测试");
        
        // 简单导出
        sw.Restart();
        var json = db.ExportToJson(indented: false);
        sw.Stop();
        UnityEngine.Debug.Log($"✓ 导出 {db.Count} 条数据为 JSON 耗时: {sw.ElapsedMilliseconds}ms");
        UnityEngine.Debug.Log($"  JSON 大小: {json.Length / 1024.0:F2} KB");
        
        // 带统计信息导出
        sw.Restart();
        var jsonWithStats = db.ExportToJsonWithStats(indented: true);
        sw.Stop();
        UnityEngine.Debug.Log($"✓ 导出带统计信息的 JSON 耗时: {sw.ElapsedMilliseconds}ms");
        UnityEngine.Debug.Log($"  JSON 大小: {jsonWithStats.Length / 1024.0:F2} KB");
        
        // 按类型导出
        sw.Restart();
        var jsonByType = db.ExportByTypeToJson("LootBox", indented: false);
        sw.Stop();
        UnityEngine.Debug.Log($"✓ 按类型导出 JSON 耗时: {sw.ElapsedMilliseconds}ms");
        
        // 按范围导出
        sw.Restart();
        var jsonInRadius = db.ExportInRadiusToJson(Vector3.zero, 50f, indented: false);
        sw.Stop();
        UnityEngine.Debug.Log($"✓ 按范围导出 JSON 耗时: {sw.ElapsedMilliseconds}ms");

        // 输出 JSON 示例（前500字符）
        UnityEngine.Debug.Log("\n[JSON 示例]");
        var preview = jsonWithStats.Length > 500 
            ? jsonWithStats.Substring(0, 500) + "..." 
            : jsonWithStats;
        UnityEngine.Debug.Log(preview);

        // 清理测试数据
        UnityEngine.Debug.Log("\n[清理] 销毁测试对象...");
        foreach (var item in testItems)
        {
            if (item != null)
                UnityEngine.Object.Destroy(item);
        }
        db.Clear();

        UnityEngine.Debug.Log("\n========================================");
        UnityEngine.Debug.Log("数据库性能测试完成");
        UnityEngine.Debug.Log("========================================");
    }

    private static string GetRandomItemType()
    {
        var types = new[] { "LootBox", "Weapon", "Ammo", "Medical", "Food", "Tool" };
        return types[UnityEngine.Random.Range(0, types.Length)];
    }
}
