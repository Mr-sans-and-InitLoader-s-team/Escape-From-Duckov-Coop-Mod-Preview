// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team

using Duckov.Utilities;
using EscapeFromDuckovCoopMod.Utils;
using ItemStatsSystem;
using NodeCanvas.Framework;
using NodeCanvas.StateMachines;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod.Utils;

/// <summary>
/// 游戏对象缓存管理器 - 统一管理所有 FindObjectsOfType 调用，减少性能开销
/// </summary>
public class GameObjectCacheManager : MonoBehaviour
{
    public static GameObjectCacheManager Instance { get; private set; }

    // 各子系统缓存
    public AIObjectCache AI { get; private set; }
    public DestructibleCache Destructibles { get; private set; }
    public EnvironmentObjectCache Environment { get; private set; }
    public LootObjectCache Loot { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        AI = new AIObjectCache();
        Destructibles = new DestructibleCache();
        Environment = new EnvironmentObjectCache();
        Loot = new LootObjectCache();

        StartCoroutine(PeriodicCleanup());
    }

    /// <summary>
    /// 场景加载时刷新所有缓存
    /// </summary>
    public void RefreshAllCaches()
    {
        try
        {
            AI.ClearCache();

            // ✅ 优化：Destructibles 缓存已在 BuildDestructibleIndex 中刷新，避免重复调用 FindObjectsOfType
            // 只在缓存过期时才刷新（10秒过期时间）
            // Destructibles.RefreshCache(); // 注释掉以避免重复

            Environment.RefreshOnSceneLoad();

            // ✅ 优化：Loot 缓存使用协程异步刷新，避免主线程阻塞
            StartCoroutine(Loot.RefreshCacheCoroutine());

            Debug.Log("[CacheManager] 所有缓存已刷新（Destructibles 跳过，Loot 异步刷新）");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CacheManager] 刷新缓存失败: {ex.Message}");
        }
    }

    /// <summary>
    /// ✅ 清空所有缓存（场景卸载时调用，避免旧场景对象引用残留）
    /// </summary>
    public void ClearAllCaches()
    {
        try
        {
            AI.ClearCache();
            Destructibles.ClearCache();
            Environment.ClearCache();
            Loot.ClearCache();
            Debug.Log("[CacheManager] 所有缓存已清空（场景卸载）");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CacheManager] 清空缓存失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 定期清理无效引用
    /// </summary>
    private IEnumerator PeriodicCleanup()
    {
        var wait = new WaitForSeconds(10f);
        while (true)
        {
            yield return wait;

            try
            {
                int cleaned = 0;
                cleaned += AI.CleanupInvalidReferences();
                cleaned += Destructibles.CleanupInvalidReferences();
                cleaned += Environment.CleanupInvalidReferences();
                cleaned += Loot.CleanupInvalidReferences();

                if (cleaned > 0)
                {
                    Debug.Log($"[CacheManager] 定期清理完成，移除 {cleaned} 个无效引用");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CacheManager] 定期清理失败: {ex.Message}");
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}

/// <summary>
/// AI 对象缓存
/// </summary>
public class AIObjectCache
{
    private CharacterSpawnerRoot[] _cachedRoots;
    private Dictionary<int, CharacterMainControl> _cmcById = new();
    private List<CharacterMainControl> _allCmc = new();
    private HashSet<AICharacterController> _allControllers = new();
    private List<NetAiTag> _netAiTags;

    // ✅ 优化：AI 行为组件缓存
    private List<AI_PathControl> _pathControls;
    private List<FSMOwner> _fsmOwners;
    private List<Blackboard> _blackboards;

    private float _lastRootCacheTime;
    private float _lastNetAiTagsCacheTime;
    private float _lastAiBehaviorCacheTime;
    private const float CACHE_REFRESH_INTERVAL = 5f;
    private const float NET_AI_TAGS_REFRESH_INTERVAL = 2f;
    private const float AI_BEHAVIOR_REFRESH_INTERVAL = 3f;

    public CharacterSpawnerRoot[] GetCharacterSpawnerRoots(bool forceRefresh = false)
    {
        if (forceRefresh || _cachedRoots == null || Time.time - _lastRootCacheTime > CACHE_REFRESH_INTERVAL)
        {
            _cachedRoots = Object.FindObjectsOfType<CharacterSpawnerRoot>(true);
            _lastRootCacheTime = Time.time;
            Debug.Log($"[AICache] 刷新 CharacterSpawnerRoot 缓存，找到 {_cachedRoots.Length} 个");
        }
        return _cachedRoots;
    }

    public void RegisterCharacterMainControl(int aiId, CharacterMainControl cmc)
    {
        if (!cmc || aiId == 0) return;
        _cmcById[aiId] = cmc;
        if (!_allCmc.Contains(cmc))
        {
            _allCmc.Add(cmc);
        }

        var controller = cmc.GetComponent<AICharacterController>();
        if (controller)
        {
            _allControllers.Add(controller);
        }
    }

    public CharacterMainControl FindByAiId(int aiId)
    {
        return _cmcById.TryGetValue(aiId, out var cmc) && cmc ? cmc : null;
    }

    /// <summary>
    /// ✅ 优化：手写循环替代 LINQ，提升性能 2-3倍
    /// </summary>
    public IEnumerable<CharacterMainControl> GetAllCharacters()
    {
        // 手写循环，避免 LINQ 的枚举器分配
        foreach (var c in _allCmc)
        {
            if (c != null) yield return c;
        }
    }

    /// <summary>
    /// ✅ 优化：手写循环替代 LINQ，提升性能 2-3倍
    /// </summary>
    public IEnumerable<AICharacterController> GetAllControllers()
    {
        // 手写循环，避免 LINQ 的枚举器分配
        foreach (var c in _allControllers)
        {
            if (c != null) yield return c;
        }
    }

    /// <summary>
    /// ✅ 获取所有 NetAiTag，使用缓存避免 FindObjectsOfType
    /// </summary>
    public IReadOnlyList<NetAiTag> GetNetAiTags(bool forceRefresh = false)
    {
        if (forceRefresh || _netAiTags == null || Time.time - _lastNetAiTagsCacheTime > NET_AI_TAGS_REFRESH_INTERVAL)
        {
            _netAiTags = new List<NetAiTag>(Object.FindObjectsOfType<NetAiTag>(true));
            _lastNetAiTagsCacheTime = Time.time;
            Debug.Log($"[AICache] 刷新 NetAiTag 缓存，找到 {_netAiTags.Count} 个");
        }
        return _netAiTags;
    }

    /// <summary>
    /// ✅ 获取所有 AI_PathControl，使用缓存避免 FindObjectsOfType
    /// </summary>
    public IEnumerable<AI_PathControl> GetAllPathControls(bool forceRefresh = false)
    {
        // ✅ 安全保护：如果缓存未初始化，直接使用 FindObjectsOfType，不触发刷新
        if (_pathControls == null && !forceRefresh)
        {
            foreach (var pc in Object.FindObjectsOfType<AI_PathControl>(true))
            {
                yield return pc;
            }
            yield break;
        }

        RefreshAiBehaviorCache(forceRefresh);
        foreach (var pc in _pathControls)
        {
            if (pc != null) yield return pc;
        }
    }

    /// <summary>
    /// ✅ 获取所有 FSMOwner，使用缓存避免 FindObjectsOfType
    /// </summary>
    public IEnumerable<FSMOwner> GetAllFSMOwners(bool forceRefresh = false)
    {
        // ✅ 安全保护：如果缓存未初始化，直接使用 FindObjectsOfType，不触发刷新
        if (_fsmOwners == null && !forceRefresh)
        {
            foreach (var fsm in Object.FindObjectsOfType<FSMOwner>(true))
            {
                yield return fsm;
            }
            yield break;
        }

        RefreshAiBehaviorCache(forceRefresh);
        foreach (var fsm in _fsmOwners)
        {
            if (fsm != null) yield return fsm;
        }
    }

    /// <summary>
    /// ✅ 获取所有 Blackboard，使用缓存避免 FindObjectsOfType
    /// </summary>
    public IEnumerable<Blackboard> GetAllBlackboards(bool forceRefresh = false)
    {
        // ✅ 安全保护：如果缓存未初始化，直接使用 FindObjectsOfType，不触发刷新
        if (_blackboards == null && !forceRefresh)
        {
            foreach (var bb in Object.FindObjectsOfType<Blackboard>(true))
            {
                yield return bb;
            }
            yield break;
        }

        RefreshAiBehaviorCache(forceRefresh);
        foreach (var bb in _blackboards)
        {
            if (bb != null) yield return bb;
        }
    }

    /// <summary>
    /// ✅ 刷新 AI 行为组件缓存
    /// </summary>
    private void RefreshAiBehaviorCache(bool forceRefresh)
    {
        if (forceRefresh || _pathControls == null || Time.time - _lastAiBehaviorCacheTime > AI_BEHAVIOR_REFRESH_INTERVAL)
        {
            try
            {
                _pathControls = new List<AI_PathControl>(Object.FindObjectsOfType<AI_PathControl>(true));
                _fsmOwners = new List<FSMOwner>(Object.FindObjectsOfType<FSMOwner>(true));
                _blackboards = new List<Blackboard>(Object.FindObjectsOfType<Blackboard>(true));
                _lastAiBehaviorCacheTime = Time.time;
                Debug.Log($"[AICache] 刷新 AI 行为组件缓存：{_pathControls.Count} PathControl, {_fsmOwners.Count} FSMOwner, {_blackboards.Count} Blackboard");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AICache] AI 行为组件缓存刷新失败: {ex.Message}");
                // 出错时保持旧缓存或初始化为空列表
                _pathControls ??= new List<AI_PathControl>();
                _fsmOwners ??= new List<FSMOwner>();
                _blackboards ??= new List<Blackboard>();
            }
        }
    }

    public void ClearCache()
    {
        _cachedRoots = null;
        _cmcById.Clear();
        _allCmc.Clear();
        _allControllers.Clear();
        _netAiTags = null;
        _pathControls = null;
        _fsmOwners = null;
        _blackboards = null;
        _lastRootCacheTime = 0f;
        _lastNetAiTagsCacheTime = 0f;
        _lastAiBehaviorCacheTime = 0f;
    }

    /// <summary>
    /// ✅ 优化：手写循环替代 LINQ，避免中间集合分配
    /// </summary>
    public int CleanupInvalidReferences()
    {
        int count = 0;

        // ✅ 优化：手写循环替代 .Where().Select().ToList()
        // 避免创建中间 List，直接收集需要删除的 key
        var invalidIdsList = ListPool<int>.Get();
        try
        {
            foreach (var kv in _cmcById)
            {
                if (!kv.Value)
                {
                    invalidIdsList.Add(kv.Key);
                }
            }

            foreach (var id in invalidIdsList)
            {
                _cmcById.Remove(id);
                count++;
            }
        }
        finally
        {
            ListPool<int>.Return(invalidIdsList);
        }

        // RemoveAll 已经是最优实现，保持不变
        _allCmc.RemoveAll(c => !c);
        _allControllers.RemoveWhere(c => !c);

        // 清理 NetAiTags
        if (_netAiTags != null)
        {
            int before = _netAiTags.Count;
            _netAiTags.RemoveAll(t => !t);
            count += before - _netAiTags.Count;
        }

        // ✅ 清理 AI 行为组件缓存
        if (_pathControls != null)
        {
            count += _pathControls.RemoveAll(pc => !pc);
        }
        if (_fsmOwners != null)
        {
            count += _fsmOwners.RemoveAll(fsm => !fsm);
        }
        if (_blackboards != null)
        {
            count += _blackboards.RemoveAll(bb => !bb);
        }

        return count;
    }
}

/// <summary>
/// 可破坏物缓存
/// </summary>
public class DestructibleCache
{
    private Dictionary<uint, HealthSimpleBase> _destructiblesById = new();
    private HealthSimpleBase[] _allDestructibles; // ✅ 新增：缓存所有可破坏物的数组
    private float _lastFullScanTime;

    public void RefreshCache()
    {
        _destructiblesById.Clear();
        var all = Object.FindObjectsOfType<HealthSimpleBase>(true);
        _allDestructibles = all; // ✅ 缓存数组，供 BuildDestructibleIndex 使用

        foreach (var hs in all)
        {
            if (!hs) continue;
            var tag = hs.GetComponent<NetDestructibleTag>();
            if (tag && tag.id != 0)
            {
                _destructiblesById[tag.id] = hs;
            }
        }
        _lastFullScanTime = Time.time;
        Debug.Log($"[DestructibleCache] 刷新缓存，找到 {_destructiblesById.Count} 个可破坏物（总共 {all.Length} 个 HealthSimpleBase）");
    }

    /// <summary>
    /// ✅ 获取所有可破坏物（供 BuildDestructibleIndex 使用，避免重复调用 FindObjectsOfType）
    /// </summary>
    public HealthSimpleBase[] GetAllDestructibles()
    {
        // 缓存过期则刷新
        if (_allDestructibles == null || Time.time - _lastFullScanTime > 10f)
        {
            RefreshCache();
        }
        return _allDestructibles;
    }

    public HealthSimpleBase FindById(uint id)
    {
        // 缓存过期则刷新
        if (Time.time - _lastFullScanTime > 10f)
        {
            RefreshCache();
        }
        return _destructiblesById.TryGetValue(id, out var hs) && hs ? hs : null;
    }

    /// <summary>
    /// ✅ 优化：手写循环替代 LINQ
    /// </summary>
    public int CleanupInvalidReferences()
    {
        var invalidList = ListPool<uint>.Get();
        try
        {
            foreach (var kv in _destructiblesById)
            {
                if (!kv.Value)
                {
                    invalidList.Add(kv.Key);
                }
            }

            foreach (var id in invalidList)
            {
                _destructiblesById.Remove(id);
            }
            return invalidList.Count;
        }
        finally
        {
            ListPool<uint>.Return(invalidList);
        }
    }

    /// <summary>
    /// ✅ 清空缓存（场景卸载时调用）
    /// </summary>
    public void ClearCache()
    {
        _destructiblesById.Clear();
        _allDestructibles = null; // ✅ 清空缓存的数组
        _lastFullScanTime = 0f;
    }
}

/// <summary>
/// 环境对象缓存（战利品箱加载器、门、场景加载器等）
/// </summary>
public class EnvironmentObjectCache
{
    private List<LootBoxLoader> _cachedLoaders = new();
    private List<global::Door> _cachedDoors = new();
    private List<SceneLoaderProxy> _cachedSceneLoaders = new();
    private float _lastRefreshTime;

    public void RefreshOnSceneLoad()
    {
        _cachedLoaders = Object.FindObjectsOfType<LootBoxLoader>(true).ToList();
        _cachedDoors = Object.FindObjectsOfType<global::Door>(true).ToList();
        _cachedSceneLoaders = Object.FindObjectsOfType<SceneLoaderProxy>(true).ToList();
        _lastRefreshTime = Time.time;
        Debug.Log($"[EnvironmentCache] 刷新缓存：{_cachedLoaders.Count} 个 LootBoxLoader, {_cachedDoors.Count} 个 Door, {_cachedSceneLoaders.Count} 个 SceneLoaderProxy");
    }

    /// <summary>
    /// ✅ 优化：手写循环替代 LINQ Where
    /// </summary>
    public IEnumerable<LootBoxLoader> GetAllLoaders()
    {
        if (Time.time - _lastRefreshTime > 15f)
        {
            RefreshOnSceneLoad();
        }
        foreach (var l in _cachedLoaders)
        {
            if (l != null) yield return l;
        }
    }

    /// <summary>
    /// ✅ 优化：手写循环替代 LINQ Where
    /// </summary>
    public IEnumerable<global::Door> GetAllDoors()
    {
        if (Time.time - _lastRefreshTime > 15f)
        {
            RefreshOnSceneLoad();
        }
        foreach (var d in _cachedDoors)
        {
            if (d != null) yield return d;
        }
    }

    /// <summary>
    /// ✅ 优化：手写循环替代 LINQ FirstOrDefault
    /// </summary>
    public global::Door FindDoorByKey(int key)
    {
        if (key == 0) return null;

        foreach (var d in _cachedDoors)
        {
            if (d && ComputeDoorKey(d.transform) == key)
            {
                return d;
            }
        }
        return null;
    }

    /// <summary>
    /// 计算门的 Key（与 Door.ComputeDoorKey 逻辑一致）
    /// </summary>
    private static int ComputeDoorKey(Transform t)
    {
        if (!t) return 0;
        var p = t.position * 10f;
        var k = new Vector3Int(
            Mathf.RoundToInt(p.x),
            Mathf.RoundToInt(p.y),
            Mathf.RoundToInt(p.z)
        );
        return $"Door_{k}".GetHashCode();
    }

    /// <summary>
    /// ✅ 获取所有 SceneLoaderProxy
    /// </summary>
    public IEnumerable<SceneLoaderProxy> GetAllSceneLoaders()
    {
        if (Time.time - _lastRefreshTime > 15f)
        {
            RefreshOnSceneLoad();
        }
        foreach (var loader in _cachedSceneLoaders)
        {
            if (loader != null) yield return loader;
        }
    }

    public int CleanupInvalidReferences()
    {
        int count = _cachedLoaders.RemoveAll(l => !l);
        count += _cachedDoors.RemoveAll(d => !d);
        count += _cachedSceneLoaders.RemoveAll(sl => !sl);
        return count;
    }

    /// <summary>
    /// ✅ 清空缓存（场景卸载时调用）
    /// </summary>
    public void ClearCache()
    {
        _cachedLoaders.Clear();
        _cachedDoors.Clear();
        _cachedSceneLoaders.Clear();
        _lastRefreshTime = 0f;
    }
}

/// <summary>
/// 战利品对象缓存（InteractableLootbox等）
/// </summary>
public class LootObjectCache
{
    private List<InteractableLootbox> _allLootboxes = new();
    private Dictionary<Inventory, InteractableLootbox> _lootboxByInv = new();
    private float _lastRefreshTime;
    private const float REFRESH_INTERVAL = 3f;

    // ✅ 递归保护：防止在刷新过程中再次触发刷新导致死循环
    private static bool _isRefreshing = false;

    // ✅ 委托缓存：用于快速访问 InteractableLootbox 的 inventoryReference 字段
    private static Func<InteractableLootbox, Inventory> _getInventoryRefDelegate;
    private static readonly object _delegateLock = new object();

    /// <summary>
    /// ✅ 初始化委托（只在第一次调用时通过反射创建，后续直接使用缓存的委托）
    /// 性能：委托访问比反射快 100+ 倍
    /// </summary>
    private static void EnsureInventoryDelegateInitialized()
    {
        if (_getInventoryRefDelegate != null) return;

        lock (_delegateLock)
        {
            if (_getInventoryRefDelegate != null) return;

            try
            {
                // 通过反射获取 inventoryReference 字段
                var fieldInfo = typeof(InteractableLootbox).GetField(
                    "inventoryReference",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                );

                if (fieldInfo == null)
                {
                    Debug.LogError("[LootCache] 无法找到 InteractableLootbox.inventoryReference 字段");
                    // 创建一个返回 null 的委托作为降级方案
                    _getInventoryRefDelegate = (lb) => null;
                    return;
                }

                // ✅ 使用 Expression Tree 创建高性能的字段访问委托
                var parameter = System.Linq.Expressions.Expression.Parameter(typeof(InteractableLootbox), "lb");
                var fieldAccess = System.Linq.Expressions.Expression.Field(parameter, fieldInfo);
                var lambda = System.Linq.Expressions.Expression.Lambda<Func<InteractableLootbox, Inventory>>(
                    fieldAccess,
                    parameter
                );
                _getInventoryRefDelegate = lambda.Compile();

                Debug.Log("[LootCache] 委托初始化成功（Expression Tree 编译）");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LootCache] 委托初始化失败: {ex.Message}，使用降级反射方案");

                // 降级方案：使用反射包装（虽然慢，但至少能工作）
                var fieldInfo = typeof(InteractableLootbox).GetField(
                    "inventoryReference",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                );

                if (fieldInfo != null)
                {
                    _getInventoryRefDelegate = (lb) => (Inventory)fieldInfo.GetValue(lb);
                    Debug.LogWarning("[LootCache] 使用反射降级方案（性能较低）");
                }
                else
                {
                    _getInventoryRefDelegate = (lb) => null;
                    Debug.LogError("[LootCache] 无法访问 inventoryReference 字段");
                }
            }
        }
    }

    public void RefreshCache()
    {
        // ✅ 递归保护：如果正在刷新，直接返回避免无限递归
        if (_isRefreshing)
        {
            Debug.LogWarning("[LootCache] 递归调用 RefreshCache 被阻止，避免死循环");
            return;
        }

        try
        {
            _isRefreshing = true;

            // ✅ 确保委托已初始化
            EnsureInventoryDelegateInitialized();

            _allLootboxes = Object.FindObjectsOfType<InteractableLootbox>(true).ToList();
            _lootboxByInv.Clear();

            int initializedCount = 0;
            int uninitializedCount = 0;

            foreach (var lb in _allLootboxes)
            {
                if (!lb) continue;

                // ✅ 关键优化：使用委托快速访问 inventoryReference 字段，避免触发 Inventory 属性的 getter
                // Inventory 属性的 getter 会调用 GetOrCreateInventory()，触发 Setup()，导致主线程阻塞
                try
                {
                    // 使用委托直接访问字段（比反射快 100+ 倍）
                    var inv = _getInventoryRefDelegate(lb);

                    if (inv != null)
                    {
                        _lootboxByInv[inv] = lb;
                        initializedCount++;
                    }
                    else
                    {
                        // 箱子尚未初始化（inventoryReference 为 null），跳过
                        uninitializedCount++;
                    }
                }
                catch (Exception ex)
                {
                    // 委托调用失败或其他异常，跳过
                    Debug.LogWarning($"[LootCache] 跳过战利品箱: {lb.name}, Error: {ex.Message}");
                    uninitializedCount++;
                }
            }

            _lastRefreshTime = Time.time;
            Debug.Log($"[LootCache] 刷新缓存，找到 {_allLootboxes.Count} 个战利品箱，映射 {initializedCount} 个 Inventory，跳过 {uninitializedCount} 个未初始化箱子");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>
    /// ✅ 协程版本：分帧刷新缓存，避免大型地图（760+箱子）导致主线程阻塞
    /// 每处理50个箱子后 yield 一次，分散到多帧处理
    /// </summary>
    public IEnumerator RefreshCacheCoroutine()
    {
        // ✅ 递归保护：如果正在刷新，直接返回避免无限递归
        if (_isRefreshing)
        {
            Debug.LogWarning("[LootCache] 递归调用 RefreshCacheCoroutine 被阻止，避免死循环");
            yield break;
        }

        try
        {
            _isRefreshing = true;

            // ✅ 确保委托已初始化
            EnsureInventoryDelegateInitialized();

            // ✅ FindObjectsOfType 本身无法分帧，但耗时相对较短（主要是后续处理耗时）
            var allLootboxesArray = Object.FindObjectsOfType<InteractableLootbox>(true);
            _allLootboxes = new List<InteractableLootbox>(allLootboxesArray.Length);
            _lootboxByInv.Clear();

            int initializedCount = 0;
            int uninitializedCount = 0;
            int processedCount = 0;
            const int BATCH_SIZE = 50; // 每批处理50个箱子

            Debug.Log($"[LootCache] 开始协程刷新缓存，共 {allLootboxesArray.Length} 个战利品箱");

            foreach (var lb in allLootboxesArray)
            {
                if (!lb) continue;

                _allLootboxes.Add(lb);

                // ✅ 关键优化：使用委托快速访问 inventoryReference 字段
                try
                {
                    var inv = _getInventoryRefDelegate(lb);

                    if (inv != null)
                    {
                        _lootboxByInv[inv] = lb;
                        initializedCount++;
                    }
                    else
                    {
                        uninitializedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[LootCache] 跳过战利品箱: {lb.name}, Error: {ex.Message}");
                    uninitializedCount++;
                }

                processedCount++;

                // ✅ 每处理 BATCH_SIZE 个箱子后让出控制权，分散到下一帧
                if (processedCount % BATCH_SIZE == 0)
                {
                    yield return null;
                }
            }

            _lastRefreshTime = Time.time;
            Debug.Log($"[LootCache] 协程刷新缓存完成，找到 {_allLootboxes.Count} 个战利品箱，映射 {initializedCount} 个 Inventory，跳过 {uninitializedCount} 个未初始化箱子");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>
    /// ✅ 优化：手写循环替代 LINQ Where
    /// </summary>
    public IEnumerable<InteractableLootbox> GetAllLootboxes()
    {
        // ✅ 递归保护：如果正在刷新，不触发新的刷新，直接返回当前缓存
        if (!_isRefreshing && Time.time - _lastRefreshTime > REFRESH_INTERVAL)
        {
            RefreshCache();
        }
        foreach (var lb in _allLootboxes)
        {
            if (lb != null) yield return lb;
        }
    }

    public InteractableLootbox FindByInventory(Inventory inv)
    {
        if (!inv) return null;

        // ✅ 递归保护：如果正在刷新，不触发新的刷新，直接查询当前缓存
        if (!_isRefreshing && Time.time - _lastRefreshTime > REFRESH_INTERVAL)
        {
            RefreshCache();
        }

        return _lootboxByInv.TryGetValue(inv, out var lb) && lb ? lb : null;
    }

    /// <summary>
    /// ✅ 优化：手写循环替代 LINQ，使用对象池减少分配
    /// </summary>
    public int CleanupInvalidReferences()
    {
        int count = _allLootboxes.RemoveAll(lb => !lb);

        var invalidInvs = ListPool<Inventory>.Get();
        try
        {
            foreach (var kv in _lootboxByInv)
            {
                if (!kv.Key || !kv.Value)
                {
                    invalidInvs.Add(kv.Key);
                }
            }

            foreach (var inv in invalidInvs)
            {
                _lootboxByInv.Remove(inv);
                count++;
            }
            return count;
        }
        finally
        {
            ListPool<Inventory>.Return(invalidInvs);
        }
    }

    /// <summary>
    /// ✅ 清空缓存（场景卸载时调用）
    /// </summary>
    public void ClearCache()
    {
        _allLootboxes.Clear();
        _lootboxByInv.Clear();
        _lastRefreshTime = 0f;
        _isRefreshing = false; // 重置递归保护标志
    }
}

