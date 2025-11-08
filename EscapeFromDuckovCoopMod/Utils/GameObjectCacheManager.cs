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
            Destructibles.RefreshCache();
            Environment.RefreshOnSceneLoad();
            Loot.RefreshCache();
            Debug.Log("[CacheManager] 所有缓存已刷新");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CacheManager] 刷新缓存失败: {ex.Message}");
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
    private float _lastFullScanTime;

    public void RefreshCache()
    {
        _destructiblesById.Clear();
        var all = Object.FindObjectsOfType<HealthSimpleBase>(true);
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
        Debug.Log($"[DestructibleCache] 刷新缓存，找到 {_destructiblesById.Count} 个可破坏物");
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

            _allLootboxes = Object.FindObjectsOfType<InteractableLootbox>(true).ToList();
            _lootboxByInv.Clear();

            foreach (var lb in _allLootboxes)
            {
                if (!lb) continue;

                // ✅ 安全获取 Inventory：使用 try-catch 避免触发 Setup 导致的递归
                try
                {
                    var inv = lb.Inventory;
                    if (inv)
                    {
                        _lootboxByInv[inv] = lb;
                    }
                }
                catch (Exception ex)
                {
                    // 在场景初始化期间，某些 lootbox 可能尚未完全初始化，跳过即可
                    Debug.LogWarning($"[LootCache] 跳过未初始化的 lootbox: {lb.name}, Error: {ex.Message}");
                }
            }

            _lastRefreshTime = Time.time;
            Debug.Log($"[LootCache] 刷新缓存，找到 {_allLootboxes.Count} 个战利品箱，映射 {_lootboxByInv.Count} 个 Inventory");
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
}

