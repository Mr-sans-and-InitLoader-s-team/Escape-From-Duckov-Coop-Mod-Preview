// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025 Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
// YOU MUST NOT use this software for commercial purposes.
// YOU MUST NOT use this software to run a headless game server.
// YOU MUST include a conspicuous notice of attribution to
// Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Utils
{
    /// <summary>
    /// ✅ List 对象池 - 避免频繁创建临时列表
    /// </summary>
    public static class ListPool<T>
    {
        private static readonly Stack<List<T>> _pool = new Stack<List<T>>(16);
        private static readonly object _lock = new object();
        private const int MAX_POOL_SIZE = 32;
        private const int MAX_LIST_CAPACITY = 2048;

        public static List<T> Get(int capacity = 32)
        {
            lock (_lock)
            {
                if (_pool.Count > 0)
                {
                    var list = _pool.Pop();
                    list.Clear();
                    if (list.Capacity < capacity)
                    {
                        list.Capacity = capacity;
                    }
                    return list;
                }
            }
            return new List<T>(capacity);
        }

        public static void Return(List<T> list)
        {
            if (list == null || list.Capacity > MAX_LIST_CAPACITY) return;

            lock (_lock)
            {
                if (_pool.Count < MAX_POOL_SIZE)
                {
                    list.Clear();
                    _pool.Push(list);
                }
            }
        }
    }

    /// <summary>
    /// ✅ Dictionary 对象池
    /// </summary>
    public static class DictionaryPool<TKey, TValue>
    {
        private static readonly Stack<Dictionary<TKey, TValue>> _pool = new Stack<Dictionary<TKey, TValue>>(8);
        private static readonly object _lock = new object();
        private const int MAX_POOL_SIZE = 16;
        private const int MAX_DICT_SIZE = 512;

        public static Dictionary<TKey, TValue> Get(int capacity = 16)
        {
            lock (_lock)
            {
                if (_pool.Count > 0)
                {
                    var dict = _pool.Pop();
                    dict.Clear();
                    return dict;
                }
            }
            return new Dictionary<TKey, TValue>(capacity);
        }

        public static void Return(Dictionary<TKey, TValue> dict)
        {
            if (dict == null || dict.Count > MAX_DICT_SIZE) return;

            lock (_lock)
            {
                if (_pool.Count < MAX_POOL_SIZE)
                {
                    dict.Clear();
                    _pool.Push(dict);
                }
            }
        }
    }

    /// <summary>
    /// ✅ StringBuilder 对象池 - 字符串拼接优化
    /// </summary>
    public static class StringBuilderPool
    {
        private static readonly Stack<StringBuilder> _pool = new Stack<StringBuilder>(16);
        private static readonly object _lock = new object();
        private const int MAX_POOL_SIZE = 32;
        private const int MAX_CAPACITY = 2048;
        private const int DEFAULT_CAPACITY = 256;

        public static StringBuilder Get(int capacity = DEFAULT_CAPACITY)
        {
            lock (_lock)
            {
                if (_pool.Count > 0)
                {
                    var sb = _pool.Pop();
                    sb.Clear();
                    if (sb.Capacity < capacity)
                    {
                        sb.Capacity = capacity;
                    }
                    return sb;
                }
            }
            return new StringBuilder(capacity);
        }

        public static void Return(StringBuilder sb)
        {
            if (sb == null || sb.Capacity > MAX_CAPACITY) return;

            lock (_lock)
            {
                if (_pool.Count < MAX_POOL_SIZE)
                {
                    sb.Clear();
                    _pool.Push(sb);
                }
            }
        }

        /// <summary>
        /// 便捷方法：获取 → 构建 → 返回池
        /// </summary>
        public static string Build(Action<StringBuilder> buildAction)
        {
            var sb = Get();
            try
            {
                buildAction(sb);
                return sb.ToString();
            }
            finally
            {
                Return(sb);
            }
        }
    }

    /// <summary>
    /// ✅ 泛型对象池 - 通用对象池
    /// </summary>
    public class ObjectPool<T> where T : class, new()
    {
        private readonly Stack<T> _pool;
        private readonly int _maxSize;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onReturn;

        public ObjectPool(int maxSize = 32, Action<T> onGet = null, Action<T> onReturn = null)
        {
            _pool = new Stack<T>(maxSize);
            _maxSize = maxSize;
            _onGet = onGet;
            _onReturn = onReturn;
        }

        public T Get()
        {
            T obj;
            if (_pool.Count > 0)
            {
                obj = _pool.Pop();
            }
            else
            {
                obj = new T();
            }

            _onGet?.Invoke(obj);
            return obj;
        }

        public void Return(T obj)
        {
            if (obj == null) return;

            _onReturn?.Invoke(obj);

            if (_pool.Count < _maxSize)
            {
                _pool.Push(obj);
            }
        }

        public void Clear()
        {
            _pool.Clear();
        }

        public int Count => _pool.Count;
    }

    /// <summary>
    /// ✅ Unity GameObject 对象池
    /// </summary>
    public class GameObjectPool
    {
        private readonly Stack<GameObject> _pool = new Stack<GameObject>();
        private readonly GameObject _prefab;
        private readonly Transform _poolRoot;
        private readonly int _maxSize;
        private int _spawnedCount;

        public GameObjectPool(GameObject prefab, int initialSize = 10, int maxSize = 100)
        {
            _prefab = prefab;
            _maxSize = maxSize;

            // 创建池根节点
            _poolRoot = new GameObject($"[Pool] {prefab.name}").transform;
            UnityEngine.Object.DontDestroyOnLoad(_poolRoot.gameObject);

            // 预热对象池
            for (int i = 0; i < initialSize; i++)
            {
                var obj = UnityEngine.Object.Instantiate(prefab);
                obj.SetActive(false);
                obj.transform.SetParent(_poolRoot);
                _pool.Push(obj);
            }
        }

        public GameObject Spawn(Vector3 position = default, Quaternion rotation = default, Transform parent = null)
        {
            GameObject obj;

            if (_pool.Count > 0)
            {
                obj = _pool.Pop();
                obj.SetActive(true);
            }
            else
            {
                obj = UnityEngine.Object.Instantiate(_prefab);
                _spawnedCount++;
            }

            obj.transform.SetParent(parent);
            obj.transform.SetPositionAndRotation(position, rotation);
            return obj;
        }

        public void Despawn(GameObject obj)
        {
            if (obj == null) return;

            obj.SetActive(false);
            obj.transform.SetParent(_poolRoot);

            if (_pool.Count < _maxSize)
            {
                _pool.Push(obj);
            }
            else
            {
                UnityEngine.Object.Destroy(obj);
                _spawnedCount--;
            }
        }

        public void Clear()
        {
            while (_pool.Count > 0)
            {
                var obj = _pool.Pop();
                if (obj != null)
                {
                    UnityEngine.Object.Destroy(obj);
                }
            }
            _spawnedCount = 0;
        }

        public int PooledCount => _pool.Count;
        public int TotalSpawned => _spawnedCount;
    }

    /// <summary>
    /// ✅ 对象池管理器 - 统一管理所有对象池
    /// </summary>
    public class ObjectPoolManager : MonoBehaviour
    {
        public static ObjectPoolManager Instance { get; private set; }

        private readonly Dictionary<string, GameObjectPool> _gameObjectPools = new Dictionary<string, GameObjectPool>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// 注册 GameObject 对象池
        /// </summary>
        public void RegisterPool(string poolName, GameObject prefab, int initialSize = 10, int maxSize = 100)
        {
            if (_gameObjectPools.ContainsKey(poolName))
            {
                Debug.LogWarning($"[ObjectPoolManager] 对象池 '{poolName}' 已存在");
                return;
            }

            var pool = new GameObjectPool(prefab, initialSize, maxSize);
            _gameObjectPools[poolName] = pool;
            Debug.Log($"[ObjectPoolManager] 注册对象池: {poolName} (初始: {initialSize}, 最大: {maxSize})");
        }

        /// <summary>
        /// 从对象池获取 GameObject
        /// </summary>
        public GameObject Spawn(string poolName, Vector3 position = default, Quaternion rotation = default, Transform parent = null)
        {
            if (_gameObjectPools.TryGetValue(poolName, out var pool))
            {
                return pool.Spawn(position, rotation, parent);
            }

            Debug.LogError($"[ObjectPoolManager] 对象池 '{poolName}' 不存在");
            return null;
        }

        /// <summary>
        /// 归还 GameObject 到对象池
        /// </summary>
        public void Despawn(string poolName, GameObject obj)
        {
            if (_gameObjectPools.TryGetValue(poolName, out var pool))
            {
                pool.Despawn(obj);
            }
            else
            {
                Debug.LogWarning($"[ObjectPoolManager] 对象池 '{poolName}' 不存在，直接销毁对象");
                if (obj != null) Destroy(obj);
            }
        }

        /// <summary>
        /// 清空指定对象池
        /// </summary>
        public void ClearPool(string poolName)
        {
            if (_gameObjectPools.TryGetValue(poolName, out var pool))
            {
                pool.Clear();
                Debug.Log($"[ObjectPoolManager] 清空对象池: {poolName}");
            }
        }

        /// <summary>
        /// 清空所有对象池
        /// </summary>
        public void ClearAllPools()
        {
            foreach (var pool in _gameObjectPools.Values)
            {
                pool.Clear();
            }
            Debug.Log("[ObjectPoolManager] 清空所有对象池");
        }

        /// <summary>
        /// 获取对象池统计信息
        /// </summary>
        public void LogPoolStats()
        {
            var sb = StringBuilderPool.Get();
            sb.AppendLine("=== Object Pool Stats ===");

            foreach (var kvp in _gameObjectPools)
            {
                var poolName = kvp.Key;
                var pool = kvp.Value;
                sb.AppendLine($"{poolName}: Pooled={pool.PooledCount}, Total={pool.TotalSpawned}");
            }

            Debug.Log(sb.ToString());
            StringBuilderPool.Return(sb);
        }

        private void OnDestroy()
        {
            ClearAllPools();
        }
    }
}

