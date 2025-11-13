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

using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine.SceneManagement;
using static EscapeFromDuckovCoopMod.LootNet;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

public static class LootUiGuards
{
    [ThreadStatic] public static int InLootAddAtDepth;
    [ThreadStatic] public static int BlockNextSendToInventory;
    public static bool InLootAddAt => InLootAddAtDepth > 0;
}

internal static class LootSearchWorldGate
{
    private static readonly Dictionary<Inventory, bool> _world = new();

    private static MemberInfo _miNeedInspection;

    public static void EnsureWorldFlag(Inventory inv)
    {
        if (inv) _world[inv] = true; // 只缓存 true避免一次误判把容器永久当“非世界”
    }

    public static bool IsWorldLootByInventory(Inventory inv)
    {
        if (!inv) return false;
        if (_world.TryGetValue(inv, out var yes) && yes) return true;

        // 动态匹配（不缓存 false）
        // ✅ 优化：使用缓存管理器，避免 FindObjectsOfType
        try
        {
            IEnumerable<InteractableLootbox> boxes = Utils.GameObjectCacheManager.Instance != null
                ? Utils.GameObjectCacheManager.Instance.Loot.GetAllLootboxes()
                : Object.FindObjectsOfType<InteractableLootbox>(true);

            foreach (var b in boxes)
            {
                if (!b) continue;
                if (b.Inventory == inv)
                {
                    var isWorld = b.GetComponent<LootBoxLoader>() != null;
                    if (isWorld) _world[inv] = true;
                    return isWorld;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    internal static bool GetNeedInspection(Inventory inv)
    {
        if (inv == null) return false;
        try
        {
            var m = FindNeedInspectionMember(inv.GetType());
            if (m is FieldInfo fi) return (bool)(fi.GetValue(inv) ?? false);
            if (m is PropertyInfo pi) return (bool)(pi.GetValue(inv) ?? false);
        }
        catch
        {
        }

        return false;
    }

    private static MemberInfo FindNeedInspectionMember(Type t)
    {
        if (_miNeedInspection != null) return _miNeedInspection;
        _miNeedInspection = (MemberInfo)t.GetField("NeedInspection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?? t.GetProperty("NeedInspection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return _miNeedInspection;
    }

    internal static void TrySetNeedInspection(Inventory inv, bool v)
    {
        if (!inv) return;
        inv.NeedInspection = v;
    }


    internal static void ForceTopLevelUninspected(Inventory inv)
    {
        if (inv == null) return;
        try
        {
            foreach (var it in inv)
            {
                if (!it) continue;
                try
                {
                    it.Inspected = false;
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

internal static class WorldLootPrime
{
    public static void PrimeIfClient(InteractableLootbox lb)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || mod.IsServer) return;
        if (!lb) return;

        var inv = lb.Inventory;
        if (!inv) return;

        // 把它标记成“世界容器”（只缓存 true，避免误判成 false）
        LootSearchWorldGate.EnsureWorldFlag(inv);

        try
        {
            lb.needInspect = false;
        }
        catch
        {
        }

        try
        {
            inv.NeedInspection = false;
        }
        catch
        {
        }

        // 直接标记为已检视，确保客户端没有迷雾
        try
        {
            foreach (var it in inv)
            {
                if (!it) continue;
                try
                {
                    it.Inspected = true;
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

internal static class DeadLootSpawnContext
{
    [ThreadStatic] public static CharacterMainControl InOnDead;
}

public static class LootboxDetectUtil
{
    public static bool IsPrivateInventory(Inventory inv)
    {
        if (inv == null) return false;
        if (ReferenceEquals(inv, PlayerStorage.Inventory)) return true; // 仓库
        if (ReferenceEquals(inv, PetProxy.PetInventory)) return true; // 宠物包
        return false;
    }

    // ✅ 性能优化：缓存墓碑/临时箱子的 Inventory，避免重复的 GetComponent 调用
    private static readonly HashSet<Inventory> _tombInventories = new HashSet<Inventory>();
    private static readonly HashSet<Inventory> _validLootboxInventories = new HashSet<Inventory>();
    private static float _lastCacheClearTime = 0f;
    private static readonly object _cacheLock = new object();

    // ✅ 性能统计
    private static int _totalChecks = 0;
    private static int _cacheHits = 0;
    private static int _tombDetections = 0;

    /// <summary>
    /// ✅ 关键修复：区分普通箱子和墓碑，使用缓存避免重复检查
    /// 普通箱子：Inventory 在独立的 GameObject 上（通过 GetOrCreateInventory 创建）
    /// 墓碑：Inventory 直接在墓碑 GameObject 上（通过 CreateLocalInventory 创建）
    /// </summary>
    public static bool IsLootboxInventory(Inventory inv)
    {
        _totalChecks++;

        if (inv == null) return false;

        // 排除私有库存（仓库/宠物包）
        if (IsPrivateInventory(inv)) return false;

        // ✅ 性能优化：定期清理缓存（每30秒），避免内存泄漏
        if (Time.time - _lastCacheClearTime > 30f)
        {
            lock (_cacheLock)
            {
                if (Time.time - _lastCacheClearTime > 30f) // 双重检查
                {
                    // 输出性能统计
                    if (_totalChecks > 0)
                    {
                        float cacheHitRate = (_cacheHits / (float)_totalChecks) * 100f;
                        Debug.Log($"[LootManager] 性能统计 - 总检查: {_totalChecks}, 缓存命中: {_cacheHits} ({cacheHitRate:F1}%), 墓碑检测: {_tombDetections}");
                    }

                    _tombInventories.Clear();
                    _validLootboxInventories.Clear();
                    _lastCacheClearTime = Time.time;

                    // 重置统计
                    _totalChecks = 0;
                    _cacheHits = 0;
                    _tombDetections = 0;
                }
            }
        }

        // ✅ 性能优化：优先检查缓存
        lock (_cacheLock)
        {
            if (_tombInventories.Contains(inv))
            {
                _cacheHits++;
                return false; // 已知是墓碑，直接返回
            }
            if (_validLootboxInventories.Contains(inv))
            {
                _cacheHits++;
                return true; // 已知是有效箱子，直接返回
            }
        }

        // ✅ 关键：检查 Inventory 是否在独立的 GameObject 上
        // 墓碑的 Inventory 直接挂在墓碑 GameObject 上，可以通过这个特征识别
        // ★ 修复：需要区分玩家墓碑和AI战利品盒子
        try
        {
            var lootbox = inv.GetComponent<InteractableLootbox>();
            if (lootbox != null)
            {
                // ✅ Inventory 和 InteractableLootbox 在同一个 GameObject 上
                // 可能是墓碑或AI战利品盒子，需要进一步判断

                // ★ 通过预制体名称区分：玩家墓碑包含"Tomb"，AI战利品盒子包含"EnemyDie"或"Die"
                var objName = inv.gameObject.name;
                bool isTomb = objName.Contains("Tomb") || objName.Contains("墓碑");
                bool isAILoot = objName.Contains("EnemyDie") || objName.Contains("Enemy") || objName.Contains("AI");

                if (isTomb && !isAILoot)
                {
                    // 确认是玩家墓碑，排除
                    lock (_cacheLock)
                    {
                        bool isNewTomb = _tombInventories.Add(inv);
                        if (isNewTomb)
                        {
                            _tombDetections++;
                            Debug.Log($"[LootManager] [{System.DateTime.Now:HH:mm:ss.fff}] 排除玩家墓碑 Inventory: {objName}（首次检测，总计 {_tombDetections} 个墓碑）");
                        }
                    }
                    return false;
                }
                else if (isAILoot)
                {
                    // ★ 这是AI战利品盒子，不应该被排除
                    Debug.Log($"[LootManager] 识别为AI战利品盒子: {objName}，允许同步");
                    lock (_cacheLock)
                    {
                        _validLootboxInventories.Add(inv);
                    }
                    return true;
                }
                // 如果无法判断，保守地认为是有效的战利品箱
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LootManager] 检查墓碑失败: {ex.Message}");
        }

        // ✅ 关键优化：先检查 GameObject 名称，快速排除角色/宠物 Inventory
        // 角色 Inventory 的 GameObject 名称不会包含 "Inventory_" 前缀
        try
        {
            var objName = inv.gameObject.name;

            // 场景中的箱子 Inventory 都有 "Inventory_" 前缀（由 GetOrCreateInventory 创建）
            // 角色/宠物/临时 Inventory 没有这个前缀
            if (!objName.StartsWith("Inventory_"))
            {
                // 不是场景箱子，直接返回 false（避免遍历字典）
                return false;
            }
        }
        catch
        {
            // GameObject.name 访问失败，继续使用字典检查
        }

        // ✅ 然后检查 LootBoxInventories 字典（只有可能是箱子的才会走到这里）
        try
        {
            var dict = InteractableLootbox.Inventories;
            if (dict != null)
            {
                foreach (var kv in dict)
                {
                    if (kv.Value == inv)
                    {
                        lock (_cacheLock)
                        {
                            _validLootboxInventories.Add(inv); // ✅ 加入有效箱子缓存
                        }
                        return true; // ✅ 在字典中且不是墓碑，是可同步的箱子
                    }
                }
            }
        }
        catch
        {
            // 场景初始化期间，LootBoxInventories 可能为 null，忽略错误
        }

        // ✅ 不在字典中的 Inventory 一律返回 false
        return false;
    }

    /// <summary>
    /// ✅ 清理 Inventory 缓存（场景卸载时调用）
    /// </summary>
    public static void ClearInventoryCaches()
    {
        lock (_cacheLock)
        {
            _tombInventories.Clear();
            _validLootboxInventories.Clear();
            _lastCacheClearTime = Time.time;
        }
    }
}

public class LootManager : MonoBehaviour
{
    public static LootManager Instance;

    public int _nextLootUid = 1; // 服务器侧自增

    // 客户端：uid -> inv
    public readonly Dictionary<int, Inventory> _cliLootByUid = new();

    // ✅ 性能优化：反向索引缓存（Inventory -> uid），避免遍历字典
    private readonly Dictionary<Inventory, int> _invToUidCache = new();

    // ✅ 性能优化：反向索引缓存（Inventory -> posKey），避免遍历 Inventories 字典
    private readonly Dictionary<Inventory, int> _invToPosKeyCache = new();


    public readonly Dictionary<uint, (Inventory inv, int pos)> _cliPendingReorder = new();

    // token -> 目的地
    public readonly Dictionary<uint, PendingTakeDest> _cliPendingTake = new();

    public readonly Dictionary<int, (int capacity, List<(int pos, ItemSnapshot snap)>)> _pendingLootStatesByUid = new();

    // 服务器：uid -> inv
    public readonly Dictionary<int, Inventory> _srvLootByUid = new();

    // 服务器：容器快照广播的"抑制窗口"表 sans可用
    public readonly Dictionary<Inventory, float> _srvLootMuteUntil = new(new RefEq<Inventory>());

    // ✅ 优化：InteractableLootbox 缓存，避免频繁 FindObjectsOfType
    private readonly Dictionary<Inventory, InteractableLootbox> _invToLootboxCache = new(new RefEq<Inventory>());
    private float _lastLootboxCacheUpdate = 0f;
    private const float LOOTBOX_CACHE_REFRESH_INTERVAL = 2f; // 每2秒刷新一次缓存

    // ✅ 优化：批量广播队列，同一帧内对同一容器的多次广播合并为一次
    private readonly HashSet<Inventory> _pendingBroadcastInvs = new(new RefEq<Inventory>());
    private bool _hasPendingBroadcasts = false;

    private NetService Service => NetService.Instance;
    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;


    public void Init()
    {
        Instance = this;
        StartCoroutine(PeriodicCleanup());
    }

    /// <summary>
    /// ✅ 优化：定期清理过期数据，避免内存泄漏
    /// </summary>
    private IEnumerator PeriodicCleanup()
    {
        var wait = new WaitForSeconds(5f);
        while (true)
        {
            yield return wait;

            try
            {
                // 清理过期的静音记录
                var now = Time.time;
                var toRemove = _srvLootMuteUntil.Where(kv => kv.Value < now).Select(kv => kv.Key).ToList();
                foreach (var inv in toRemove)
                {
                    _srvLootMuteUntil.Remove(inv);
                }

                // 清理失效的缓存（被销毁的 Inventory 或 Lootbox）
                var invalidCacheKeys = _invToLootboxCache.Where(kv => !kv.Key || !kv.Value).Select(kv => kv.Key).ToList();
                foreach (var inv in invalidCacheKeys)
                {
                    _invToLootboxCache.Remove(inv);
                }

                if (toRemove.Count > 0 || invalidCacheKeys.Count > 0)
                {
                    Debug.Log($"[LootManager] 清理完成：移除 {toRemove.Count} 个过期静音记录，{invalidCacheKeys.Count} 个失效缓存");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LootManager] 定期清理失败: {ex.Message}");
            }
        }
    }


    public int ComputeLootKey(Transform t)
    {
        if (!t) return -1;
        var v = t.position * 10f;
        var x = Mathf.RoundToInt(v.x);
        var y = Mathf.RoundToInt(v.y);
        var z = Mathf.RoundToInt(v.z);
        return new Vector3Int(x, y, z).GetHashCode();
    }


    public void PutLootId(NetDataWriter w, Inventory inv)
    {
        var scene = SceneManager.GetActiveScene().buildIndex;
        var posKey = -1;
        var instanceId = -1;

        // ✅ 修复：场景切换时 LevelManager 或 Inventories 可能为空，添加多层保护
        try
        {
            // 先检查 LevelManager 是否存在
            if (LevelManager.Instance == null)
            {
                // 场景切换时 LevelManager 可能已销毁，直接跳过
                w.Put(posKey);
                w.Put(instanceId);
                w.Put(scene);
                return;
            }

            // ✅ 优化：使用反向索引缓存，避免遍历 Inventories 字典
            if (inv != null && _invToPosKeyCache.TryGetValue(inv, out var cachedPosKey))
            {
                posKey = cachedPosKey; // 从缓存中快速获取（O(1)）
            }
            else
            {
                // 缓存未命中，降级到遍历（仅第一次）
                var dict = InteractableLootbox.Inventories;
                if (inv != null && dict != null)
                    foreach (var kv in dict)
                        if (kv.Value == inv)
                        {
                            posKey = kv.Key;
                            _invToPosKeyCache[inv] = posKey; // 更新缓存
                            break;
                        }
            }
        }
        catch (Exception ex)
        {
            // 场景切换时访问 LevelManager.LootBoxInventories 可能抛异常，忽略
            Debug.LogWarning($"[LootManager] PutLootId 访问 Inventories 失败: {ex.Message}");
        }

        if (inv != null && (posKey < 0 || instanceId < 0))
        {
            try
            {
                // ✅ 优化：使用缓存查找，避免 FindObjectsOfType
                var lootbox = FindLootboxByInventory(inv);
                if (lootbox)
                {
                    posKey = ComputeLootKey(lootbox.transform);
                    instanceId = lootbox.GetInstanceID();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LootManager] PutLootId 查找 InteractableLootbox 失败: {ex.Message}");
            }
        }

        // ✅ 优化：稳定 ID 使用反向索引缓存，避免遍历字典
        var lootUid = -1;
        if (inv != null && _invToUidCache.TryGetValue(inv, out var cachedUid))
        {
            lootUid = cachedUid; // 从缓存中快速获取（O(1)）
        }
        else if (inv != null)
        {
            // 缓存未命中，降级到遍历（仅第一次）
            if (IsServer)
            {
                // 主机：从 _srvLootByUid 反查
                foreach (var kv in _srvLootByUid)
                    if (kv.Value == inv)
                    {
                        lootUid = kv.Key;
                        _invToUidCache[inv] = lootUid; // 更新缓存
                        break;
                    }
            }
            else
            {
                // 客户端：从 _cliLootByUid 反查
                foreach (var kv in _cliLootByUid)
                    if (kv.Value == inv)
                    {
                        lootUid = kv.Key;
                        _invToUidCache[inv] = lootUid; // 更新缓存
                        break;
                    }
            }
        }

        w.Put(scene);
        w.Put(posKey);
        w.Put(instanceId);
        w.Put(lootUid);
    }


    public bool TryResolveLootById(int scene, int posKey, int iid, out Inventory inv)
    {
        inv = null;

        // 先用 posKey 命中（跨词典）
        if (posKey != 0 && TryGetLootInvByKeyEverywhere(posKey, out inv)) return true;

        // 再按 iid 找 GameObject 上的 InteractableLootbox，取其 Inventory
        // ✅ 优化：使用缓存管理器，避免 FindObjectsOfType
        if (iid != 0)
            try
            {
                IEnumerable<InteractableLootbox> all = Utils.GameObjectCacheManager.Instance != null
                    ? Utils.GameObjectCacheManager.Instance.Loot.GetAllLootboxes()
                    : FindObjectsOfType<InteractableLootbox>(true);

                foreach (var b in all)
                {
                    if (!b) continue;
                    if (b.GetInstanceID() == iid && (scene < 0 || b.gameObject.scene.buildIndex == scene))
                    {
                        inv = b.Inventory; // 走到这一步，get_Inventory 的兜底会触发
                        if (inv) return true;
                    }
                }
            }
            catch
            {
            }

        return false; // 交给 TryResolveLootByHint / Server_TryResolveLootAggressive
    }

    // 兜底协程：超时自动清 Loading
    public IEnumerator ClearLootLoadingTimeout(Inventory inv, float seconds)
    {
        var t = 0f;
        while (inv && inv.Loading && t < seconds)
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (inv && inv.Loading) inv.Loading = false;
    }

    public static int ComputeLootKeyFromPos(Vector3 pos)
    {
        var v = pos * 10f;
        var x = Mathf.RoundToInt(v.x);
        var y = Mathf.RoundToInt(v.y);
        var z = Mathf.RoundToInt(v.z);
        return new Vector3Int(x, y, z).GetHashCode();
    }

    /// <summary>
    /// ✅ 优化：快速查找 InteractableLootbox，使用缓存避免 FindObjectsOfType
    /// </summary>
    public InteractableLootbox FindLootboxByInventory(Inventory inv)
    {
        if (!inv) return null;

        // 先查缓存
        if (_invToLootboxCache.TryGetValue(inv, out var cached) && cached)
        {
            return cached;
        }

        // 缓存未命中或需要刷新
        if (Time.time - _lastLootboxCacheUpdate > LOOTBOX_CACHE_REFRESH_INTERVAL)
        {
            RefreshLootboxCache();
        }

        // 再次尝试从缓存获取
        if (_invToLootboxCache.TryGetValue(inv, out cached) && cached)
        {
            return cached;
        }

        // 最后兜底：直接查找
        // ✅ 优化：使用缓存管理器，避免 FindObjectsOfType
        IEnumerable<InteractableLootbox> boxes = Utils.GameObjectCacheManager.Instance != null
            ? Utils.GameObjectCacheManager.Instance.Loot.GetAllLootboxes()
            : FindObjectsOfType<InteractableLootbox>();

        foreach (var b in boxes)
        {
            if (!b) continue;
            if (b.Inventory == inv)
            {
                _invToLootboxCache[inv] = b; // 加入缓存
                return b;
            }
        }

        return null;
    }

    /// <summary>
    /// ✅ 优化：刷新 InteractableLootbox 缓存
    /// </summary>
    private void RefreshLootboxCache()
    {
        _lastLootboxCacheUpdate = Time.time;
        _invToLootboxCache.Clear();

        try
        {
            // ✅ 优化：使用缓存管理器，避免 FindObjectsOfType
            IEnumerable<InteractableLootbox> boxes = Utils.GameObjectCacheManager.Instance != null
                ? Utils.GameObjectCacheManager.Instance.Loot.GetAllLootboxes()
                : FindObjectsOfType<InteractableLootbox>();

            foreach (var b in boxes)
            {
                if (!b) continue;
                var inv = b.Inventory;
                if (inv)
                {
                    _invToLootboxCache[inv] = b;
                }
            }
            Debug.Log($"[LootManager] 刷新缓存完成，找到 {_invToLootboxCache.Count} 个战利品箱");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LootManager] 刷新缓存失败: {ex.Message}");
        }
    }

    // 通过 inv 找到它对应的 Lootbox 世界坐标；找不到则返回 false
    public bool TryGetLootboxWorldPos(Inventory inv, out Vector3 pos)
    {
        pos = default;
        if (!inv) return false;

        // ✅ 优化：使用缓存查找
        var lootbox = FindLootboxByInventory(inv);
        if (lootbox)
        {
            pos = lootbox.transform.position;
            return true;
        }

        return false;
    }

    // 根据位置提示在半径内兜底解析对应的 lootbox（主机端用）
    private bool TryResolveLootByHint(Vector3 posHint, out Inventory inv, float radius = 2.5f)
    {
        inv = null;
        var best = float.MaxValue;
        // ✅ 优化：使用缓存管理器，避免 FindObjectsOfType
        IEnumerable<InteractableLootbox> boxes = Utils.GameObjectCacheManager.Instance != null
            ? Utils.GameObjectCacheManager.Instance.Loot.GetAllLootboxes()
            : FindObjectsOfType<InteractableLootbox>();

        foreach (var b in boxes)
        {
            if (!b || b.Inventory == null) continue;
            var d = Vector3.Distance(b.transform.position, posHint);
            if (d < radius && d < best)
            {
                best = d;
                inv = b.Inventory;
            }
        }

        return inv != null;
    }

    // 每次开箱都拉起一次“解卡”兜底，避免第二次打开卡死
    public void KickLootTimeout(Inventory inv, float seconds = 1.5f)
    {
        StartCoroutine(ClearLootLoadingTimeout(inv, seconds));
    }

    // 当前 LootView 是否就是这个容器（用它来识别“战利品容器”）
    public static bool IsCurrentLootInv(Inventory inv)
    {
        var lv = LootView.Instance;
        return lv && inv && ReferenceEquals(inv, lv.TargetInventory);
    }

    public bool Server_TryResolveLootAggressive(int scene, int posKey, int iid, Vector3 posHint, out Inventory inv)
    {
        inv = null;

        // 1) 你原有的两条路径
        if (TryResolveLootById(scene, posKey, iid, out inv)) return true;
        if (TryResolveLootByHint(posHint, out inv)) return true;

        // 2) 兜底：在 posHint 附近 3m 扫一圈，强制确保并注册
        // ✅ 优化：使用缓存管理器，避免 FindObjectsOfType
        var best = 9f; // 3m^2
        InteractableLootbox bestBox = null;
        IEnumerable<InteractableLootbox> allBoxes = Utils.GameObjectCacheManager.Instance != null
            ? Utils.GameObjectCacheManager.Instance.Loot.GetAllLootboxes()
            : FindObjectsOfType<InteractableLootbox>();

        foreach (var b in allBoxes)
        {
            if (!b || !b.gameObject.activeInHierarchy) continue;
            if (scene >= 0 && b.gameObject.scene.buildIndex != scene) continue;
            var d2 = (b.transform.position - posHint).sqrMagnitude;
            if (d2 < best)
            {
                best = d2;
                bestBox = b;
            }
        }

        if (!bestBox) return false;

        // 触发/强制创建 Inventory（原游戏逻辑会注册到 LevelManager.LootBoxInventories）
        inv = bestBox.Inventory; // 等价于 GetOrCreateInventory(b)
        if (!inv) return false;

        // 保险：把 posKey→inv 显式写入一次
        var dict = InteractableLootbox.Inventories;
        if (dict != null)
        {
            var key = ComputeLootKey(bestBox.transform);
            dict[key] = inv;
        }

        return true;
    }

    public void Server_HandleLootOpenRequest(NetPeer peer, NetDataReader r)
    {
        if (!IsServer) return;

        // 旧三元标识
        var scene = r.GetInt();
        var posKey = r.GetInt();
        var iid = r.GetInt();

        // 对齐 PutLootId：可能还带了稳定ID
        var lootUid = -1;
        if (r.AvailableBytes >= 4) lootUid = r.GetInt();

        // 请求版本（向后兼容）
        byte reqVer = 0;
        if (r.AvailableBytes >= 1) reqVer = r.GetByte();

        // 位置提示（厘米压缩），防御式读取
        var posHint = Vector3.zero;
        if (r.AvailableBytes >= 12) posHint = r.GetV3cm();

        Debug.Log($"[LOOT-REQ] 收到客户端请求: scene={scene}, posKey={posKey}, iid={iid}, lootUid={lootUid}, posHint={posHint}");

        // 先用稳定ID命中（AI掉落箱优先命中这里）
        Inventory inv = null;
        if (lootUid >= 0) _srvLootByUid.TryGetValue(lootUid, out inv);

        if (LootboxDetectUtil.IsPrivateInventory(inv))
        {
            Debug.LogWarning($"[LOOT-REQ] 拒绝：私有Inventory");
            COOPManager.LootNet.Server_SendLootDeny(peer, "no_inv");
            return;
        }

        // 命不中再走你原有"激进解析"：三元标识 + 附近3米扫描并注册
        if (inv == null && !Server_TryResolveLootAggressive(scene, posKey, iid, posHint, out inv))
        {
            Debug.LogWarning($"[LOOT-REQ] 无法解析Inventory: scene={scene}, posKey={posKey}, iid={iid}");
            COOPManager.LootNet.Server_SendLootDeny(peer, "no_inv");
            return;
        }

        Debug.Log($"[LOOT-REQ] 成功解析Inventory: {inv?.gameObject?.name}, 物品数={inv?.Content?.Count ?? 0}");

        // 只回给发起的这个 peer（不要广播）
        COOPManager.LootNet.Server_SendLootboxState(peer, inv);
    }

    public void NoteLootReorderPending(uint token, Inventory inv, int targetPos)
    {
        if (token != 0 && inv) _cliPendingReorder[token] = (inv, targetPos);
    }

    public static bool TryGetLootInvByKeyEverywhere(int posKey, out Inventory inv)
    {
        inv = null;

        // A) InteractableLootbox.Inventories
        try
        {
            var dictA = InteractableLootbox.Inventories;
            if (dictA != null && dictA.TryGetValue(posKey, out inv) && inv) return true;
        }
        catch (Exception ex)
        {
            // 🛡️ InteractableLootbox.Inventories 可能在场景切换时为 null
            Debug.LogWarning($"[LOOT] InteractableLootbox.Inventories access failed (scene loading?): {ex.Message}");
        }

        // B) LevelManager.LootBoxInventories
        try
        {
            var lm = LevelManager.Instance;
            // 🛡️ 添加更严格的 null 检查
            if (lm == null)
            {
                Debug.LogWarning("[LOOT] LevelManager.Instance is null (scene loading?)");
                return false;
            }

            var dictB = LevelManager.LootBoxInventories;
            if (dictB == null)
            {
                Debug.LogWarning("[LOOT] LevelManager.LootBoxInventories is null (scene loading?)");
                return false;
            }

            if (dictB.TryGetValue(posKey, out inv) && inv)
            {
                // 顺手回填 A，保持一致
                try
                {
                    var dictA = InteractableLootbox.Inventories;
                    if (dictA != null) dictA[posKey] = inv;
                }
                catch
                {
                }

                return true;
            }
        }
        catch (Exception ex)
        {
            // 🛡️ 捕获所有可能的 NullReferenceException
            Debug.LogWarning($"[LOOT] LevelManager.LootBoxInventories access failed (scene loading?): {ex.Message}");
        }

        inv = null;
        return false;
    }


    public InteractableLootbox ResolveDeadLootPrefabOnServer()
    {
        var any = GameplayDataSettings.Prefabs;
        try
        {
            if (any != null && any.LootBoxPrefab_Tomb != null) return any.LootBoxPrefab_Tomb;
        }
        catch
        {
        }

        if (any != null) return any.LootBoxPrefab;

        return null; // 客户端收到 DEAD_LOOT_SPAWN 时也有兜底寻找预制体的逻辑
    }


    // 发送端：把 inv 内 item 的“路径”写进包里
    public void WriteItemRef(NetDataWriter w, Inventory inv, Item item)
    {
        // 找到 inv 中的“根物品”（顶层，不在任何槽位里）
        var root = item;
        while (root != null && root.PluggedIntoSlot != null) root = root.PluggedIntoSlot.Master;
        var rootIndex = inv != null ? inv.GetIndex(root) : -1;
        w.Put(rootIndex);

        // 从 item 逆向收集到根的槽位key，再反转写出
        var keys = new List<string>();
        var cur = item;
        while (cur != null && cur.PluggedIntoSlot != null)
        {
            var s = cur.PluggedIntoSlot;
            keys.Add(s.Key ?? "");
            cur = s.Master;
        }

        keys.Reverse();
        w.Put(keys.Count);
        foreach (var k in keys) w.Put(k ?? "");
    }


    // 接收端：用“路径”从 inv 找回 item
    public Item ReadItemRef(NetDataReader r, Inventory inv)
    {
        var rootIndex = r.GetInt();
        var keyCount = r.GetInt();
        var it = inv.GetItemAt(rootIndex);
        for (var i = 0; i < keyCount && it != null; i++)
        {
            var key = r.GetString();
            var slot = it.Slots?.GetSlot(key);
            it = slot != null ? slot.Content : null;
        }

        return it;
    }


    // 统一解析容器 Inventory：优先稳定ID，再回落到三元标识
    public Inventory ResolveLootInv(int scene, int posKey, int iid, int lootUid)
    {
        Inventory inv = null;

        // 先用稳定ID（主机用 _srvLootByUid；客户端用 _cliLootByUid）
        if (lootUid >= 0)
        {
            if (IsServer)
            {
                if (_srvLootByUid != null && _srvLootByUid.TryGetValue(lootUid, out inv) && inv)
                    return inv;
            }
            else
            {
                if (_cliLootByUid != null && _cliLootByUid.TryGetValue(lootUid, out inv) && inv)
                    return inv;
            }
        }

        // 回落到 scene/posKey/iid 三元定位
        if (TryResolveLootById(scene, posKey, iid, out inv) && inv)
            return inv;

        return null;
    }

    public bool Server_IsLootMuted(Inventory inv)
    {
        if (!inv) return false;
        if (_srvLootMuteUntil.TryGetValue(inv, out var until))
        {
            if (Time.time < until) return true;
            _srvLootMuteUntil.Remove(inv); // 过期清理
        }

        return false;
    }

    public void Server_MuteLoot(Inventory inv, float seconds)
    {
        if (!inv) return;
        _srvLootMuteUntil[inv] = Time.time + Mathf.Max(0.01f, seconds);
    }

    /// <summary>
    /// ✅ 优化：将容器加入批量广播队列，同一帧内多次操作只广播一次
    /// </summary>
    public void Server_QueueLootBroadcast(Inventory inv)
    {
        if (!inv || !IsServer) return;
        if (LootboxDetectUtil.IsPrivateInventory(inv)) return;
        if (!LootboxDetectUtil.IsLootboxInventory(inv)) return;

        // 加入待广播队列
        _pendingBroadcastInvs.Add(inv);

        // 启动帧结束时的批量广播
        if (!_hasPendingBroadcasts)
        {
            _hasPendingBroadcasts = true;
            DeferedRunner.EndOfFrame(ProcessPendingBroadcasts);
        }
    }

    /// <summary>
    /// ✅ 优化：处理待广播队列，批量执行
    /// </summary>
    private void ProcessPendingBroadcasts()
    {
        if (_pendingBroadcastInvs.Count == 0)
        {
            _hasPendingBroadcasts = false;
            return;
        }

        var count = 0;
        foreach (var inv in _pendingBroadcastInvs)
        {
            try
            {
                if (!inv) continue;
                if (Server_IsLootMuted(inv)) continue;
                if (!LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv)) continue;

                COOPManager.LootNet.Server_SendLootboxState(null, inv);
                count++;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LootManager] 批量广播失败: {ex.Message}");
            }
        }

        if (count > 0)
        {
            Debug.Log($"[LootManager] 批量广播完成，发送 {count}/{_pendingBroadcastInvs.Count} 个容器状态");
        }

        _pendingBroadcastInvs.Clear();
        _hasPendingBroadcasts = false;
    }

    /// <summary>
    /// ✅ 优化：清理缓存和队列，场景切换时调用
    /// </summary>
    public void ClearCaches()
    {
        _invToLootboxCache.Clear();
        _invToUidCache.Clear(); // ✅ 同步清理反向索引缓存
        _invToPosKeyCache.Clear(); // ✅ 同步清理 posKey 缓存
        _pendingBroadcastInvs.Clear();
        _hasPendingBroadcasts = false;
        _lastLootboxCacheUpdate = 0f;
        Debug.Log("[LootManager] 缓存已清理");
    }

    private sealed class RefEq<T> : IEqualityComparer<T> where T : class
    {
        public bool Equals(T a, T b)
        {
            return ReferenceEquals(a, b);
        }

        public int GetHashCode(T obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}