using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Utils.Database;

/// <summary>
/// 游戏物品实体
/// </summary>
public class GameItemEntity
{
    public string SetId { get; set; }
    public GameObject GameObject { get; set; }
    public string ItemType { get; set; }
    public Vector3 Position { get; set; }
    public string SceneName { get; set; }
    public int OwnerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, object> CustomData { get; set; } = new();

    public GameItemEntity(GameObject go, string setId)
    {
        GameObject = go;
        SetId = setId;
        Position = go.transform.position;
        CreatedAt = DateTime.Now;
    }
}

/// <summary>
/// 游戏物品数据库（高性能查询）
/// </summary>
public class GameItemDatabase
{
    private readonly InMemoryDatabase<GameItemEntity> _db;
    
    public int Count => _db.Count;

    public GameItemDatabase(float spatialCellSize = 10f)
    {
        _db = new InMemoryDatabase<GameItemEntity>()
            .WithPrimaryKey(e => e.SetId)
            .WithIndex("ItemType", e => e.ItemType)
            .WithIndex("SceneName", e => e.SceneName)
            .WithIndex("OwnerId", e => e.OwnerId)
            .WithSpatialIndex(e => e.Position, spatialCellSize);
    }

    #region 基础操作

    /// <summary>
    /// 添加物品
    /// </summary>
    public bool AddItem(GameObject go, string setId, string itemType = null, int ownerId = -1)
    {
        if (go == null || string.IsNullOrEmpty(setId))
            return false;

        var entity = new GameItemEntity(go, setId)
        {
            ItemType = itemType ?? "Unknown",
            SceneName = go.scene.name,
            OwnerId = ownerId
        };

        return _db.Insert(entity);
    }

    /// <summary>
    /// 更新物品位置
    /// </summary>
    public bool UpdateItemPosition(string setId)
    {
        var entity = _db.FindByKey(setId);
        if (entity?.GameObject == null)
            return false;

        entity.Position = entity.GameObject.transform.position;
        return _db.Update(entity);
    }

    /// <summary>
    /// 删除物品
    /// </summary>
    public bool RemoveItem(string setId)
    {
        return _db.Delete(setId);
    }

    /// <summary>
    /// 清空数据库
    /// </summary>
    public void Clear()
    {
        _db.Clear();
    }

    #endregion

    #region 查询操作

    /// <summary>
    /// 按 SetId 查询（O(1)）
    /// </summary>
    public GameObject GetItemBySetId(string setId)
    {
        return _db.FindByKey(setId)?.GameObject;
    }

    /// <summary>
    /// 按类型查询（O(1)）
    /// </summary>
    public IEnumerable<GameObject> GetItemsByType(string itemType)
    {
        foreach (var entity in _db.FindByIndex("ItemType", itemType))
        {
            if (entity.GameObject != null)
                yield return entity.GameObject;
        }
    }

    /// <summary>
    /// 按场景查询（O(1)）
    /// </summary>
    public IEnumerable<GameObject> GetItemsByScene(string sceneName)
    {
        foreach (var entity in _db.FindByIndex("SceneName", sceneName))
        {
            if (entity.GameObject != null)
                yield return entity.GameObject;
        }
    }

    /// <summary>
    /// 按所有者查询（O(1)）
    /// </summary>
    public IEnumerable<GameObject> GetItemsByOwner(int ownerId)
    {
        foreach (var entity in _db.FindByIndex("OwnerId", ownerId))
        {
            if (entity.GameObject != null)
                yield return entity.GameObject;
        }
    }

    /// <summary>
    /// 空间范围查询（O(1) 网格查询）
    /// </summary>
    public IEnumerable<GameObject> GetItemsInRadius(Vector3 center, float radius)
    {
        foreach (var entity in _db.FindInRadius(center, radius))
        {
            if (entity.GameObject != null)
                yield return entity.GameObject;
        }
    }

    /// <summary>
    /// 复杂条件查询
    /// </summary>
    public IEnumerable<GameObject> FindItems(Func<GameItemEntity, bool> predicate)
    {
        foreach (var entity in _db.Where(predicate))
        {
            if (entity.GameObject != null)
                yield return entity.GameObject;
        }
    }

    /// <summary>
    /// 获取所有物品
    /// </summary>
    public IEnumerable<GameObject> GetAllItems()
    {
        foreach (var entity in _db.GetAll())
        {
            if (entity.GameObject != null)
                yield return entity.GameObject;
        }
    }

    #endregion

    #region 批量操作

    /// <summary>
    /// 批量添加物品
    /// </summary>
    public int BulkAddItems(IEnumerable<GameObject> gameObjects, Func<GameObject, string> setIdGetter, 
        Func<GameObject, string> typeGetter = null)
    {
        var entities = new List<GameItemEntity>();
        
        foreach (var go in gameObjects)
        {
            if (go == null) continue;
            
            var setId = setIdGetter?.Invoke(go);
            if (string.IsNullOrEmpty(setId)) continue;

            var entity = new GameItemEntity(go, setId)
            {
                ItemType = typeGetter?.Invoke(go) ?? "Unknown",
                SceneName = go.scene.name,
                OwnerId = -1
            };
            
            entities.Add(entity);
        }

        return _db.BulkInsert(entities);
    }

    /// <summary>
    /// 批量删除物品
    /// </summary>
    public int BulkRemoveItems(Func<GameItemEntity, bool> predicate)
    {
        return _db.BulkDelete(predicate);
    }

    /// <summary>
    /// 清理无效物品（GameObject 已销毁）
    /// </summary>
    public int CleanupInvalidItems()
    {
        return _db.BulkDelete(e => e.GameObject == null);
    }

    #endregion

    #region 统计信息

    /// <summary>
    /// 按类型统计数量
    /// </summary>
    public Dictionary<string, int> GetItemCountByType()
    {
        var counts = new Dictionary<string, int>();
        
        foreach (var entity in _db.GetAll())
        {
            var type = entity.ItemType ?? "Unknown";
            counts[type] = counts.GetValueOrDefault(type, 0) + 1;
        }
        
        return counts;
    }

    /// <summary>
    /// 按场景统计数量
    /// </summary>
    public Dictionary<string, int> GetItemCountByScene()
    {
        var counts = new Dictionary<string, int>();
        
        foreach (var entity in _db.GetAll())
        {
            var scene = entity.SceneName ?? "Unknown";
            counts[scene] = counts.GetValueOrDefault(scene, 0) + 1;
        }
        
        return counts;
    }

    #endregion

    #region 自定义数据

    /// <summary>
    /// 设置自定义数据
    /// </summary>
    public bool SetCustomData(string setId, string key, object value)
    {
        var entity = _db.FindByKey(setId);
        if (entity == null) return false;

        entity.CustomData[key] = value;
        return true;
    }

    /// <summary>
    /// 获取自定义数据
    /// </summary>
    public object GetCustomData(string setId, string key)
    {
        var entity = _db.FindByKey(setId);
        return entity?.CustomData.GetValueOrDefault(key);
    }

    #endregion

    #region JSON 导出

    /// <summary>
    /// 导出整库为 JSON（简化格式，只包含关键信息）
    /// </summary>
    public string ExportToJson(bool indented = true)
    {
        var exportData = new List<object>();
        
        foreach (var entity in _db.GetAll())
        {
            if (entity.GameObject == null) continue;
            
            exportData.Add(new
            {
                entity.SetId,
                entity.ItemType,
                entity.SceneName,
                entity.OwnerId,
                Position = new
                {
                    x = entity.Position.x,
                    y = entity.Position.y,
                    z = entity.Position.z
                },
                GameObjectName = entity.GameObject.name,
                Active = entity.GameObject.activeSelf,
                CreatedAt = entity.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                CustomData = entity.CustomData
            });
        }

        var formatting = indented 
            ? Newtonsoft.Json.Formatting.Indented 
            : Newtonsoft.Json.Formatting.None;
        
        return Newtonsoft.Json.JsonConvert.SerializeObject(exportData, formatting);
    }

    /// <summary>
    /// 导出整库为 JSON（带统计信息）
    /// </summary>
    public string ExportToJsonWithStats(bool indented = true)
    {
        var items = new List<object>();
        
        foreach (var entity in _db.GetAll())
        {
            if (entity.GameObject == null) continue;
            
            items.Add(new
            {
                entity.SetId,
                entity.ItemType,
                entity.SceneName,
                entity.OwnerId,
                Position = new
                {
                    x = entity.Position.x,
                    y = entity.Position.y,
                    z = entity.Position.z
                },
                GameObjectName = entity.GameObject.name,
                Active = entity.GameObject.activeSelf,
                CreatedAt = entity.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                CustomData = entity.CustomData
            });
        }

        var data = new
        {
            ExportTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            TotalCount = _db.Count,
            Statistics = new
            {
                ByType = GetItemCountByType(),
                ByScene = GetItemCountByScene()
            },
            Items = items
        };

        var formatting = indented 
            ? Newtonsoft.Json.Formatting.Indented 
            : Newtonsoft.Json.Formatting.None;
        
        return Newtonsoft.Json.JsonConvert.SerializeObject(data, formatting);
    }

    /// <summary>
    /// 按类型导出为 JSON
    /// </summary>
    public string ExportByTypeToJson(string itemType, bool indented = true)
    {
        var items = new List<object>();
        
        foreach (var entity in _db.FindByIndex("ItemType", itemType))
        {
            if (entity.GameObject == null) continue;
            
            items.Add(new
            {
                entity.SetId,
                entity.ItemType,
                entity.SceneName,
                entity.OwnerId,
                Position = new
                {
                    x = entity.Position.x,
                    y = entity.Position.y,
                    z = entity.Position.z
                },
                GameObjectName = entity.GameObject.name,
                Active = entity.GameObject.activeSelf,
                CreatedAt = entity.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        var formatting = indented 
            ? Newtonsoft.Json.Formatting.Indented 
            : Newtonsoft.Json.Formatting.None;
        
        return Newtonsoft.Json.JsonConvert.SerializeObject(items, formatting);
    }

    /// <summary>
    /// 按场景导出为 JSON
    /// </summary>
    public string ExportBySceneToJson(string sceneName, bool indented = true)
    {
        var items = new List<object>();
        
        foreach (var entity in _db.FindByIndex("SceneName", sceneName))
        {
            if (entity.GameObject == null) continue;
            
            items.Add(new
            {
                entity.SetId,
                entity.ItemType,
                entity.SceneName,
                entity.OwnerId,
                Position = new
                {
                    x = entity.Position.x,
                    y = entity.Position.y,
                    z = entity.Position.z
                },
                GameObjectName = entity.GameObject.name,
                Active = entity.GameObject.activeSelf,
                CreatedAt = entity.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        var formatting = indented 
            ? Newtonsoft.Json.Formatting.Indented 
            : Newtonsoft.Json.Formatting.None;
        
        return Newtonsoft.Json.JsonConvert.SerializeObject(items, formatting);
    }

    /// <summary>
    /// 按范围导出为 JSON
    /// </summary>
    public string ExportInRadiusToJson(Vector3 center, float radius, bool indented = true)
    {
        var items = new List<object>();
        
        foreach (var entity in _db.FindInRadius(center, radius))
        {
            if (entity.GameObject == null) continue;
            
            var distance = Vector3.Distance(entity.Position, center);
            
            items.Add(new
            {
                entity.SetId,
                entity.ItemType,
                entity.SceneName,
                entity.OwnerId,
                Position = new
                {
                    x = entity.Position.x,
                    y = entity.Position.y,
                    z = entity.Position.z
                },
                Distance = distance,
                GameObjectName = entity.GameObject.name,
                Active = entity.GameObject.activeSelf,
                CreatedAt = entity.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        var data = new
        {
            Center = new { x = center.x, y = center.y, z = center.z },
            Radius = radius,
            Count = items.Count,
            Items = items
        };

        var formatting = indented 
            ? Newtonsoft.Json.Formatting.Indented 
            : Newtonsoft.Json.Formatting.None;
        
        return Newtonsoft.Json.JsonConvert.SerializeObject(data, formatting);
    }

    /// <summary>
    /// 自定义条件导出为 JSON
    /// </summary>
    public string ExportToJson(Func<GameItemEntity, bool> predicate, bool indented = true)
    {
        var items = new List<object>();
        
        foreach (var entity in _db.Where(predicate))
        {
            if (entity.GameObject == null) continue;
            
            items.Add(new
            {
                entity.SetId,
                entity.ItemType,
                entity.SceneName,
                entity.OwnerId,
                Position = new
                {
                    x = entity.Position.x,
                    y = entity.Position.y,
                    z = entity.Position.z
                },
                GameObjectName = entity.GameObject.name,
                Active = entity.GameObject.activeSelf,
                CreatedAt = entity.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                CustomData = entity.CustomData
            });
        }

        var formatting = indented 
            ? Newtonsoft.Json.Formatting.Indented 
            : Newtonsoft.Json.Formatting.None;
        
        return Newtonsoft.Json.JsonConvert.SerializeObject(items, formatting);
    }

    #endregion
}
