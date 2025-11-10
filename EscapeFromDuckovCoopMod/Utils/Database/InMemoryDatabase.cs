using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Utils.Database;

/// <summary>
/// 高性能内存数据库，支持多索引查询和空间查询
/// </summary>
/// <typeparam name="T">实体类型</typeparam>
public class InMemoryDatabase<T> where T : class
{
    // 主存储：主键 -> 实体
    private readonly Dictionary<string, T> _primaryIndex = new();
    
    // 二级索引：索引名 -> (索引值 -> 实体列表)
    private readonly Dictionary<string, Dictionary<object, HashSet<T>>> _secondaryIndexes = new();
    
    // 空间索引：网格 -> 实体列表
    private readonly Dictionary<Vector2Int, HashSet<T>> _spatialIndex = new();
    
    // 索引配置
    private readonly Dictionary<string, Func<T, object>> _indexGetters = new();
    private Func<T, string> _primaryKeyGetter;
    private Func<T, Vector3> _positionGetter;
    private float _spatialCellSize = 10f;
    
    public int Count => _primaryIndex.Count;

    /// <summary>
    /// 配置主键
    /// </summary>
    public InMemoryDatabase<T> WithPrimaryKey(Func<T, string> keyGetter)
    {
        _primaryKeyGetter = keyGetter ?? throw new ArgumentNullException(nameof(keyGetter));
        return this;
    }

    /// <summary>
    /// 添加二级索引
    /// </summary>
    public InMemoryDatabase<T> WithIndex(string indexName, Func<T, object> valueGetter)
    {
        if (string.IsNullOrEmpty(indexName))
            throw new ArgumentException("Index name cannot be null or empty", nameof(indexName));
        
        _indexGetters[indexName] = valueGetter ?? throw new ArgumentNullException(nameof(valueGetter));
        _secondaryIndexes[indexName] = new Dictionary<object, HashSet<T>>();
        return this;
    }

    /// <summary>
    /// 配置空间索引（用于位置查询）
    /// </summary>
    public InMemoryDatabase<T> WithSpatialIndex(Func<T, Vector3> positionGetter, float cellSize = 10f)
    {
        _positionGetter = positionGetter ?? throw new ArgumentNullException(nameof(positionGetter));
        _spatialCellSize = cellSize;
        return this;
    }

    /// <summary>
    /// 插入实体
    /// </summary>
    public bool Insert(T entity)
    {
        if (entity == null) return false;
        if (_primaryKeyGetter == null)
            throw new InvalidOperationException("Primary key getter not configured");

        var key = _primaryKeyGetter(entity);
        if (string.IsNullOrEmpty(key)) return false;
        
        // 检查是否已存在
        if (_primaryIndex.ContainsKey(key))
            return false;

        // 添加到主索引
        _primaryIndex[key] = entity;

        // 添加到二级索引
        foreach (var kvp in _indexGetters)
        {
            var indexName = kvp.Key;
            var indexValue = kvp.Value(entity);
            
            if (indexValue != null)
            {
                if (!_secondaryIndexes[indexName].ContainsKey(indexValue))
                    _secondaryIndexes[indexName][indexValue] = new HashSet<T>();
                
                _secondaryIndexes[indexName][indexValue].Add(entity);
            }
        }

        // 添加到空间索引
        if (_positionGetter != null)
        {
            var cell = GetSpatialCell(_positionGetter(entity));
            if (!_spatialIndex.ContainsKey(cell))
                _spatialIndex[cell] = new HashSet<T>();
            _spatialIndex[cell].Add(entity);
        }

        return true;
    }

    /// <summary>
    /// 更新实体（先删除再插入）
    /// </summary>
    public bool Update(T entity)
    {
        if (entity == null) return false;
        
        var key = _primaryKeyGetter(entity);
        if (string.IsNullOrEmpty(key)) return false;

        Delete(key);
        return Insert(entity);
    }

    /// <summary>
    /// 删除实体
    /// </summary>
    public bool Delete(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (!_primaryIndex.TryGetValue(key, out var entity))
            return false;

        // 从主索引删除
        _primaryIndex.Remove(key);

        // 从二级索引删除
        foreach (var kvp in _indexGetters)
        {
            var indexName = kvp.Key;
            var indexValue = kvp.Value(entity);
            
            if (indexValue != null && _secondaryIndexes[indexName].TryGetValue(indexValue, out var set))
            {
                set.Remove(entity);
                if (set.Count == 0)
                    _secondaryIndexes[indexName].Remove(indexValue);
            }
        }

        // 从空间索引删除
        if (_positionGetter != null)
        {
            var cell = GetSpatialCell(_positionGetter(entity));
            if (_spatialIndex.TryGetValue(cell, out var set))
            {
                set.Remove(entity);
                if (set.Count == 0)
                    _spatialIndex.Remove(cell);
            }
        }

        return true;
    }

    /// <summary>
    /// 按主键查询（O(1)）
    /// </summary>
    public T FindByKey(string key)
    {
        return _primaryIndex.TryGetValue(key, out var entity) ? entity : null;
    }

    /// <summary>
    /// 按索引查询（O(1)）
    /// </summary>
    public IEnumerable<T> FindByIndex(string indexName, object value)
    {
        if (!_secondaryIndexes.TryGetValue(indexName, out var index))
            return Enumerable.Empty<T>();

        if (!index.TryGetValue(value, out var set))
            return Enumerable.Empty<T>();

        return set;
    }

    /// <summary>
    /// 空间范围查询（O(1) 网格查询）
    /// </summary>
    public IEnumerable<T> FindInRadius(Vector3 center, float radius)
    {
        if (_positionGetter == null)
            throw new InvalidOperationException("Spatial index not configured");

        var centerCell = GetSpatialCell(center);
        var cellRadius = Mathf.CeilToInt(radius / _spatialCellSize);
        var radiusSq = radius * radius;

        for (int x = -cellRadius; x <= cellRadius; x++)
        {
            for (int z = -cellRadius; z <= cellRadius; z++)
            {
                var cell = new Vector2Int(centerCell.x + x, centerCell.y + z);
                
                if (_spatialIndex.TryGetValue(cell, out var entities))
                {
                    foreach (var entity in entities)
                    {
                        var pos = _positionGetter(entity);
                        var distSq = (pos - center).sqrMagnitude;
                        
                        if (distSq <= radiusSq)
                            yield return entity;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 条件查询（支持复杂过滤）
    /// </summary>
    public IEnumerable<T> Where(Func<T, bool> predicate)
    {
        return _primaryIndex.Values.Where(predicate);
    }

    /// <summary>
    /// 获取所有实体
    /// </summary>
    public IEnumerable<T> GetAll()
    {
        return _primaryIndex.Values;
    }

    /// <summary>
    /// 清空数据库
    /// </summary>
    public void Clear()
    {
        _primaryIndex.Clear();
        foreach (var index in _secondaryIndexes.Values)
            index.Clear();
        _spatialIndex.Clear();
    }

    /// <summary>
    /// 批量插入
    /// </summary>
    public int BulkInsert(IEnumerable<T> entities)
    {
        int count = 0;
        foreach (var entity in entities)
        {
            if (Insert(entity))
                count++;
        }
        return count;
    }

    /// <summary>
    /// 批量删除
    /// </summary>
    public int BulkDelete(Func<T, bool> predicate)
    {
        var toDelete = _primaryIndex.Values.Where(predicate).ToList();
        int count = 0;
        
        foreach (var entity in toDelete)
        {
            var key = _primaryKeyGetter(entity);
            if (Delete(key))
                count++;
        }
        
        return count;
    }

    private Vector2Int GetSpatialCell(Vector3 position)
    {
        return new Vector2Int(
            Mathf.FloorToInt(position.x / _spatialCellSize),
            Mathf.FloorToInt(position.z / _spatialCellSize)
        );
    }

    #region JSON 导出

    /// <summary>
    /// 导出整库为 JSON 字符串
    /// </summary>
    public string ExportToJson(bool indented = true)
    {
        var formatting = indented 
            ? Newtonsoft.Json.Formatting.Indented 
            : Newtonsoft.Json.Formatting.None;
        
        return Newtonsoft.Json.JsonConvert.SerializeObject(
            _primaryIndex.Values,
            formatting
        );
    }

    /// <summary>
    /// 导出整库为 JSON 字符串（带统计信息）
    /// </summary>
    public string ExportToJsonWithStats(bool indented = true)
    {
        var data = new
        {
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            TotalCount = _primaryIndex.Count,
            IndexCount = _secondaryIndexes.Count,
            SpatialCellCount = _spatialIndex.Count,
            Data = _primaryIndex.Values
        };

        var formatting = indented 
            ? Newtonsoft.Json.Formatting.Indented 
            : Newtonsoft.Json.Formatting.None;
        
        return Newtonsoft.Json.JsonConvert.SerializeObject(data, formatting);
    }

    /// <summary>
    /// 导出符合条件的数据为 JSON
    /// </summary>
    public string ExportToJson(Func<T, bool> predicate, bool indented = true)
    {
        var filtered = _primaryIndex.Values.Where(predicate);
        
        var formatting = indented 
            ? Newtonsoft.Json.Formatting.Indented 
            : Newtonsoft.Json.Formatting.None;
        
        return Newtonsoft.Json.JsonConvert.SerializeObject(filtered, formatting);
    }

    #endregion
}
