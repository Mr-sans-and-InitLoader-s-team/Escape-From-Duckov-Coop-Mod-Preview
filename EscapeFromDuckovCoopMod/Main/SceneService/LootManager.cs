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

using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
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
        try
        {
            var boxes = Object.FindObjectsOfType<InteractableLootbox>(true);
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

        // 已经是需搜索就别重复改（幂等）
        var need = false;
        try
        {
            need = inv.NeedInspection;
        }
        catch
        {
        }

        if (need) return;

        try
        {
            lb.needInspect = true;
        }
        catch
        {
        }

        try
        {
            inv.NeedInspection = true;
        }
        catch
        {
        }

        // 只把顶层物品置为未鉴定即可（Inventory 可 foreach）
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

    public static bool IsLootboxInventory(Inventory inv)
    {
        if (inv == null) return false;
        // 排除私有库存（仓库/宠物包）
        if (IsPrivateInventory(inv)) return false;

        var dict = InteractableLootbox.Inventories;
        if (dict != null)
            foreach (var kv in dict)
                if (kv.Value == inv)
                    return true;
        var boxes = Object.FindObjectsOfType<InteractableLootbox>(true);
        foreach (var b in boxes)
            if (b && b.Inventory == inv)
                return true;

        return false;
    }

    public static InteractableLootbox TryGetInventoryLootBox(Inventory inv)
    {
        if (inv == null) return null;

        // 遍历场景中的所有 InteractableLootbox 来找到对应的
        var boxes = Object.FindObjectsOfType<InteractableLootbox>(true);
        foreach (var box in boxes)
        {
            if (box && box.Inventory == inv)
            {
                return box;
            }
        }

        return null;
    }
}

public class LootManager : MonoBehaviour
{
    public static LootManager Instance;

    public int _nextLootUid = 1; // 服务器侧自增

    // 客户端：uid -> inv
    public readonly Dictionary<int, Inventory> _cliLootByUid = new();


    public readonly Dictionary<uint, (Inventory inv, int pos)> _cliPendingReorder = new();

    // token -> 目的地
    public readonly Dictionary<uint, PendingTakeDest> _cliPendingTake = new();

    public readonly Dictionary<int, (int capacity, List<(int pos, ItemSnapshot snap)>)> _pendingLootStatesByUid = new();

    // 服务器：uid -> inv
    public readonly Dictionary<int, Inventory> _srvLootByUid = new();

    // 服务器：容器快照广播的“抑制窗口”表 sans可用
    public readonly Dictionary<Inventory, float> _srvLootMuteUntil = new(new RefEq<Inventory>());

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
        
        // 初始化墓碑持久化系统
        if (TombstonePersistence.Instance == null)
        {
            var go = new GameObject("TombstonePersistence");
            go.AddComponent<TombstonePersistence>().Init();
            DontDestroyOnLoad(go);
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

        var dict = InteractableLootbox.Inventories;
        if (inv != null && dict != null)
            foreach (var kv in dict)
                if (kv.Value == inv)
                {
                    posKey = kv.Key;
                    break;
                }

        if (inv != null && (posKey < 0 || instanceId < 0))
        {
            var boxes = FindObjectsOfType<InteractableLootbox>();
            foreach (var b in boxes)
            {
                if (!b) continue;
                if (b.Inventory == inv)
                {
                    posKey = ComputeLootKey(b.transform);
                    instanceId = b.GetInstanceID();
                    break;
                }
            }
        }

        // 稳定 ID（仅死亡箱子会命中，其它容器写 -1）
        var lootUid = -1;
        if (IsServer)
        {
            // 主机：从 _srvLootByUid 反查
            foreach (var kv in _srvLootByUid)
                if (kv.Value == inv)
                {
                    lootUid = kv.Key;
                    break;
                }
        }
        else
        {
            // 客户端：从 _cliLootByUid 反查（关键修复）
            foreach (var kv in _cliLootByUid)
                if (kv.Value == inv)
                {
                    lootUid = kv.Key;
                    break;
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
        if (iid != 0)
            try
            {
                var all = FindObjectsOfType<InteractableLootbox>(true);
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

    // 通过 inv 找到它对应的 Lootbox 世界坐标；找不到则返回 false
    public bool TryGetLootboxWorldPos(Inventory inv, out Vector3 pos)
    {
        pos = default;
        if (!inv) return false;
        var boxes = FindObjectsOfType<InteractableLootbox>();
        foreach (var b in boxes)
        {
            if (!b) continue;
            if (b.Inventory == inv)
            {
                pos = b.transform.position;
                return true;
            }
        }

        return false;
    }

    // 根据位置提示在半径内兜底解析对应的 lootbox（主机端用）
    private bool TryResolveLootByHint(Vector3 posHint, out Inventory inv, float radius = 2.5f)
    {
        inv = null;
        var best = float.MaxValue;
        var boxes = FindObjectsOfType<InteractableLootbox>();
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
        var best = 9f; // 3m^2
        InteractableLootbox bestBox = null;
        foreach (var b in FindObjectsOfType<InteractableLootbox>())
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

    public void Server_HandleLootOpenRequest(NetPeer peer, NetPacketReader r)
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

        // 先用稳定ID命中（AI掉落箱优先命中这里）
        Inventory inv = null;
        if (lootUid >= 0) _srvLootByUid.TryGetValue(lootUid, out inv);

        if (LootboxDetectUtil.IsPrivateInventory(inv))
        {
            COOPManager.LootNet.Server_SendLootDeny(peer, "no_inv");
            return;
        }

        // 命不中再走你原有“激进解析”：三元标识 + 附近3米扫描并注册
        if (inv == null && !Server_TryResolveLootAggressive(scene, posKey, iid, posHint, out inv))
        {
            COOPManager.LootNet.Server_SendLootDeny(peer, "no_inv");
            return;
        }

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
        var dictA = InteractableLootbox.Inventories;
        if (dictA != null && dictA.TryGetValue(posKey, out inv) && inv) return true;

        // B) LevelManager.LootBoxInventories
        try
        {
            var lm = LevelManager.Instance;
            var dictB = lm != null ? LevelManager.LootBoxInventories : null;
            if (dictB != null && dictB.TryGetValue(posKey, out inv) && inv)
            {
                // 顺手回填 A，保持一致
                try
                {
                    if (dictA != null) dictA[posKey] = inv;
                }
                catch
                {
                }

                return true;
            }
        }
        catch
        {
        }

        inv = null;
        return false;
    }


    public InteractableLootbox ResolveDeadLootPrefabOnServer()
    {
        var any = GameplayDataSettings.Prefabs;
        Debug.Log($"[TOMBSTONE] ResolveDeadLootPrefabOnServer - GameplayDataSettings.Prefabs is null: {any == null}");
        
        try
        {
            if (any != null && any.LootBoxPrefab_Tomb != null) 
            {
                Debug.Log($"[TOMBSTONE] Using correct tomb prefab: {any.LootBoxPrefab_Tomb.name}");
                return any.LootBoxPrefab_Tomb;
            }
            else if (any != null)
            {
                Debug.LogWarning($"[TOMBSTONE] LootBoxPrefab_Tomb is null, falling back to LootBoxPrefab: {any.LootBoxPrefab?.name ?? "null"}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[TOMBSTONE] Exception getting LootBoxPrefab_Tomb: {e.Message}");
        }

        if (any != null && any.LootBoxPrefab != null) 
        {
            Debug.LogWarning($"[TOMBSTONE] Using fallback prefab (may show as wrong model): {any.LootBoxPrefab.name}");
            return any.LootBoxPrefab;
        }

        Debug.LogError("[TOMBSTONE] No prefab available for dead loot");
        return null; // 客户端收到 DEAD_LOOT_SPAWN 时也有兜底寻找预制体的逻辑
    }

    /// <summary>
    /// 获取玩家墓碑预制体（真正的墓碑外观）
    /// </summary>
    private InteractableLootbox GetPlayerTombstonePrefab()
    {
        var any = GameplayDataSettings.Prefabs;
        Debug.Log($"[TOMBSTONE] GameplayDataSettings.Prefabs is null: {any == null}");
        
        try
        {
            if (any != null)
            {
                Debug.Log($"[TOMBSTONE] LootBoxPrefab_Tomb is null: {any.LootBoxPrefab_Tomb == null}");
                if (any.LootBoxPrefab_Tomb != null) 
                {
                    Debug.Log($"[TOMBSTONE] Found LootBoxPrefab_Tomb: {any.LootBoxPrefab_Tomb.name}");
                    return any.LootBoxPrefab_Tomb;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TOMBSTONE] Failed to get LootBoxPrefab_Tomb: {e.Message}");
        }

        // 如果没有找到墓碑预制体，尝试使用默认预制体
        Debug.LogWarning("[TOMBSTONE] LootBoxPrefab_Tomb not found, trying to use default LootBoxPrefab");
        try
        {
            if (any != null && any.LootBoxPrefab != null)
            {
                Debug.Log($"[TOMBSTONE] Using fallback LootBoxPrefab: {any.LootBoxPrefab.name}");
                return any.LootBoxPrefab;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TOMBSTONE] Failed to get fallback LootBoxPrefab: {e.Message}");
        }
        
        Debug.LogError("[TOMBSTONE] No suitable prefab found for player tombstone");
        return null;
    }

    /// <summary>
    /// 获取AI战利品预制体（烤鸡外观）
    /// </summary>
    private InteractableLootbox GetAILootPrefab()
    {
        var any = GameplayDataSettings.Prefabs;
        try
        {
            if (any != null && any.LootBoxPrefab != null) 
            {
                return any.LootBoxPrefab;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TOMBSTONE] Failed to get LootBoxPrefab: {e.Message}");
        }

        // 如果没有找到战利品预制体，返回null
        Debug.LogWarning("[TOMBSTONE] LootBoxPrefab not found, AI loot will not be created");
        return null;
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
    public Item ReadItemRef(NetPacketReader r, Inventory inv)
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

    // ========== 墓碑持久化相关方法 ==========

    /// <summary>
    /// 场景加载时恢复墓碑数据到内存
    /// </summary>
    public void LoadSceneTombstones(string sceneId)
    {
        if (!IsServer || TombstonePersistence.Instance == null)
        {
            return;
        }

        try
        {
            Debug.Log($"[TOMBSTONE] Loading tombstones for scene: {sceneId}");
            
            // 首先清理内存中物品为0的墓碑
            CleanupEmptyTombstones();
            
            // 扫描所有可能的墓碑文件
            var tombstoneDir = Path.Combine(Application.streamingAssetsPath, "TombstoneData");
            Debug.Log($"[TOMBSTONE] Scanning tombstone directory: {tombstoneDir}");
            
            if (Directory.Exists(tombstoneDir))
            {
                var tombstoneFiles = Directory.GetFiles(tombstoneDir, "*_tombstones.json");
                Debug.Log($"[TOMBSTONE] Found {tombstoneFiles.Length} tombstone files in directory");
                
                var totalTombstonesProcessed = 0;
                var totalItemsRestored = 0;
                
                foreach (var filePath in tombstoneFiles)
                {
                    try
                    {
                        var fileName = Path.GetFileName(filePath);
                        var fileSize = new FileInfo(filePath).Length;
                        
                        // 使用TombstonePersistence的解码方法来获取真实的用户ID
                        var userId = TombstonePersistence.Instance.DecodeUserIdFromFileName(fileName);
                        
                        Debug.Log($"[TOMBSTONE] Processing file: {fileName} (size: {fileSize} bytes) for decoded userId: {userId}");
                        
                        var tombstones = TombstonePersistence.Instance.GetSceneTombstones(userId, sceneId);
                        Debug.Log($"[TOMBSTONE] User {userId} has {tombstones.Count} tombstones in scene {sceneId}");
                        
                        foreach (var tombstone in tombstones)
                        {
                            // 跳过没有物品的墓碑
                            if (tombstone.items == null || tombstone.items.Count == 0)
                            {
                                Debug.Log($"[TOMBSTONE] Skipping empty tombstone: lootUid={tombstone.lootUid}");
                                continue;
                            }
                            
                            bool needsRestoration = false;
                            
                            // 检查墓碑是否需要恢复
                            if (!_srvLootByUid.ContainsKey(tombstone.lootUid))
                            {
                                needsRestoration = true;
                                Debug.Log($"[TOMBSTONE] Tombstone not in memory: lootUid={tombstone.lootUid}");
                            }
                            else
                            {
                                // 检查对应的游戏对象是否还存在，或者物品是否为空
                                var existingInv = _srvLootByUid[tombstone.lootUid];
                                if (existingInv == null || !DoesTombstoneGameObjectExist(existingInv) || IsInventoryEmpty(existingInv))
                                {
                                    needsRestoration = true;
                                    if (existingInv == null)
                                    {
                                        Debug.Log($"[TOMBSTONE] Tombstone inventory is null, needs restoration: lootUid={tombstone.lootUid}");
                                    }
                                    else if (!DoesTombstoneGameObjectExist(existingInv))
                                    {
                                        Debug.Log($"[TOMBSTONE] Tombstone game object destroyed, needs restoration: lootUid={tombstone.lootUid}");
                                    }
                                    else if (IsInventoryEmpty(existingInv))
                                    {
                                        Debug.Log($"[TOMBSTONE] Tombstone inventory is empty, needs restoration: lootUid={tombstone.lootUid}");
                                    }
                                }
                                else
                                {
                                    Debug.Log($"[TOMBSTONE] ⚠ Tombstone already exists in scene: lootUid={tombstone.lootUid}");
                                }
                            }
                            
                            if (needsRestoration)
                            {
                                Debug.Log($"[TOMBSTONE] Restoring tombstone: userId={userId}, lootUid={tombstone.lootUid}, items={tombstone.items.Count}");
                                
                                // 创建墓碑游戏对象和Inventory
                                var restoredInv = CreateDummyInventoryFromTombstone(tombstone);
                                if (restoredInv != null)
                                {
                                    _srvLootByUid[tombstone.lootUid] = restoredInv;
                                    totalTombstonesProcessed++;
                                    totalItemsRestored += tombstone.items.Count;
                                    
                                    // 广播墓碑恢复信息给所有客户端
                                    BroadcastTombstoneRestored(tombstone, restoredInv);
                                    
                                    Debug.Log($"[TOMBSTONE] ✓ Successfully restored tombstone: userId={userId}, lootUid={tombstone.lootUid}, items={tombstone.items.Count}");
                                }
                                else
                                {
                                    Debug.LogWarning($"[TOMBSTONE] ✗ Failed to create inventory for tombstone: userId={userId}, lootUid={tombstone.lootUid}");
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[TOMBSTONE] ✗ Failed to process tombstone file {filePath}: {e}");
                    }
                }
                
                Debug.Log($"[TOMBSTONE] === RESTORATION SUMMARY ===");
                Debug.Log($"[TOMBSTONE] Scene: {sceneId}");
                Debug.Log($"[TOMBSTONE] Files processed: {tombstoneFiles.Length}");
                Debug.Log($"[TOMBSTONE] Tombstones restored: {totalTombstonesProcessed}");
                Debug.Log($"[TOMBSTONE] Total items restored: {totalItemsRestored}");
                Debug.Log($"[TOMBSTONE] Memory dictionary size: {_srvLootByUid.Count}");
            }
            else
            {
                Debug.LogWarning($"[TOMBSTONE] Tombstone directory does not exist: {tombstoneDir}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[TOMBSTONE] Failed to load scene tombstones: {e}");
        }
    }

    /// <summary>
    /// 检查墓碑对应的游戏对象是否还存在
    /// </summary>
    private bool DoesTombstoneGameObjectExist(Inventory inventory)
    {
        if (inventory == null)
        {
            return false;
        }

        try
        {
            // 尝试通过Inventory找到对应的InteractableLootbox
            var lootbox = LootboxDetectUtil.TryGetInventoryLootBox(inventory);
            if (lootbox != null && lootbox.gameObject != null)
            {
                // 检查游戏对象是否在当前场景中且处于活动状态
                return lootbox.gameObject.scene.isLoaded && lootbox.gameObject.activeInHierarchy;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TOMBSTONE] Error checking tombstone existence: {e.Message}");
        }

        return false;
    }

    /// <summary>
    /// 创建虚拟Inventory用于占位，并在场景中生成实际的墓碑
    /// </summary>
    private Inventory CreateDummyInventoryFromTombstone(TombstoneData tombstone)
    {
        try
        {
            Debug.Log($"[TOMBSTONE] Creating tombstone in scene: lootUid={tombstone.lootUid}, position={tombstone.position}, items={tombstone.items.Count}");
            
            // 输出物品详细信息
            for (int i = 0; i < tombstone.items.Count; i++)
            {
                var item = tombstone.items[i];
                Debug.Log($"[TOMBSTONE]   Creating item {i}: pos={item.position}, typeId={item.snapshot.typeId}, stack={item.snapshot.stack}");
            }
            
            // 清理同一位置的空墓碑
            CleanupEmptyLootboxesAtPosition(tombstone.position, 1.0f);
            
            // 暂时使用统一的预制体选择逻辑，优先使用墓碑预制体
            var deadPfb = ResolveDeadLootPrefabOnServer();
            if (deadPfb == null)
            {
                Debug.LogError("[TOMBSTONE] Failed to get dead loot prefab");
                return null;
            }
            
            Debug.Log($"[TOMBSTONE] Using prefab: {deadPfb.name} for tombstone with aiId: {tombstone.aiId}");
            
            // 在场景中创建墓碑
            var tombstoneGO = Object.Instantiate(deadPfb.gameObject, tombstone.position, tombstone.rotation);
            var lootbox = tombstoneGO.GetComponent<InteractableLootbox>();
            if (lootbox == null)
            {
                Debug.LogError("[TOMBSTONE] Created tombstone has no InteractableLootbox component");
                Object.Destroy(tombstoneGO);
                return null;
            }
            
            var inv = lootbox.Inventory;
            if (inv == null)
            {
                Debug.LogError("[TOMBSTONE] Created tombstone has no Inventory");
                Object.Destroy(tombstoneGO);
                return null;
            }
            
            // 设置容量
            inv.SetCapacity(Mathf.Max(10, tombstone.items.Count));
            
            // 重建物品
            var rebuiltItems = 0;
            foreach (var tombstoneItem in tombstone.items)
            {
                try
                {
                    var item = ItemTool.BuildItemFromSnapshot(tombstoneItem.snapshot);
                    if (item != null)
                    {
                        inv.AddAt(item, tombstoneItem.position);
                        rebuiltItems++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[TOMBSTONE] Failed to rebuild item at position {tombstoneItem.position}: {e}");
                }
            }
            
            // 标记为需要检视
            inv.NeedInspection = true;
            
            // 注册到InteractableLootbox.Inventories字典
            var dict = InteractableLootbox.Inventories;
            if (dict != null)
            {
                var posKey = ComputeLootKey(lootbox.transform);
                dict[posKey] = inv;
                Debug.Log($"[TOMBSTONE] Registered tombstone inventory with posKey: {posKey}");
            }
            
            // 注册到_srvLootByUid字典，这样客机可以通过lootUid直接找到
            if (tombstone.lootUid >= 0)
            {
                _srvLootByUid[tombstone.lootUid] = inv;
                Debug.Log($"[TOMBSTONE] Registered tombstone inventory with lootUid: {tombstone.lootUid}");
            }
            
            Debug.Log($"[TOMBSTONE] Successfully created tombstone: lootUid={tombstone.lootUid}, rebuiltItems={rebuiltItems}");
            return inv;
        }
        catch (Exception e)
        {
            Debug.LogError($"[TOMBSTONE] Failed to create tombstone from data: {e}");
            return null;
        }
    }

    /// <summary>
    /// 保存墓碑数据到持久化存储
    /// </summary>
    public void SaveTombstoneData(string userId, int lootUid, string sceneId, Vector3 position, Quaternion rotation, int aiId, Inventory inventory)
    {
        if (!IsServer || TombstonePersistence.Instance == null)
        {
            return;
        }

        try
        {
            TombstonePersistence.Instance.AddTombstone(userId, lootUid, sceneId, position, rotation, aiId, inventory);
            Debug.Log($"[TOMBSTONE] Saved tombstone data: userId={userId}, lootUid={lootUid}, sceneId={sceneId}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[TOMBSTONE] Failed to save tombstone data: {e}");
        }
    }

    /// <summary>
    /// 更新墓碑物品数据（当物品被拾取时）
    /// </summary>
    public void UpdateTombstoneItems(string userId, int lootUid, Inventory inventory)
    {
        if (!IsServer || TombstonePersistence.Instance == null)
        {
            return;
        }

        try
        {
            TombstonePersistence.Instance.UpdateTombstoneItems(userId, lootUid, inventory);
            Debug.Log($"[TOMBSTONE] Updated tombstone items: userId={userId}, lootUid={lootUid}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[TOMBSTONE] Failed to update tombstone items: {e}");
        }
    }

    /// <summary>
    /// 获取当前场景ID（标准化处理）
    /// </summary>
    public string GetCurrentSceneId()
    {
        try
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var sceneName = scene.name;
            
            // 标准化场景名称，移除数字后缀
            if (sceneName.StartsWith("Level_GroundZero"))
            {
                return "Level_GroundZero"; // 统一使用基础名称
            }
            
            return sceneName;
        }
        catch
        {
            return "unknown_scene";
        }
    }

    /// <summary>
    /// 清理内存中物品为0的墓碑
    /// </summary>
    private void CleanupEmptyTombstones()
    {
        if (!IsServer)
        {
            return;
        }

        try
        {
            var emptyTombstones = new List<int>();
            
            foreach (var kv in _srvLootByUid)
            {
                var lootUid = kv.Key;
                var inventory = kv.Value;
                
                if (inventory == null || IsInventoryEmpty(inventory))
                {
                    emptyTombstones.Add(lootUid);
                }
            }
            
            foreach (var lootUid in emptyTombstones)
            {
                _srvLootByUid.Remove(lootUid);
                Debug.Log($"[TOMBSTONE] Cleaned up empty tombstone from memory: lootUid={lootUid}");
            }
            
            if (emptyTombstones.Count > 0)
            {
                Debug.Log($"[TOMBSTONE] Cleaned up {emptyTombstones.Count} empty tombstones from memory");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[TOMBSTONE] Failed to cleanup empty tombstones: {e}");
        }
    }

    /// <summary>
    /// 检查Inventory是否为空（没有物品）
    /// </summary>
    private bool IsInventoryEmpty(Inventory inventory)
    {
        if (inventory == null)
        {
            return true;
        }

        try
        {
            // 检查Content列表
            if (inventory.Content == null || inventory.Content.Count == 0)
            {
                return true;
            }

            // 检查是否所有位置都是null
            var hasItems = false;
            for (int i = 0; i < inventory.Content.Count; i++)
            {
                var item = inventory.GetItemAt(i);
                if (item != null)
                {
                    hasItems = true;
                    break;
                }
            }

            return !hasItems;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TOMBSTONE] Error checking if inventory is empty: {e.Message}");
            return true; // 出错时认为是空的，需要恢复
        }
    }

    /// <summary>
    /// 清理指定位置附近的空墓碑
    /// </summary>
    public void CleanupEmptyLootboxesAtPosition(Vector3 position, float radius)
    {
        if (!IsServer)
        {
            return;
        }

        try
        {
            var lootboxes = Object.FindObjectsOfType<InteractableLootbox>();
            var cleanedCount = 0;

            foreach (var lootbox in lootboxes)
            {
                if (lootbox == null || lootbox.gameObject == null)
                {
                    continue;
                }

                // 检查距离
                var distance = Vector3.Distance(lootbox.transform.position, position);
                if (distance > radius)
                {
                    continue;
                }

                // 检查是否为空
                var inventory = lootbox.Inventory;
                if (inventory != null && IsInventoryEmpty(inventory))
                {
                    Debug.Log($"[TOMBSTONE] Cleaning up empty lootbox at position: {lootbox.transform.position}, distance: {distance:F2}");
                    
                    // 从字典中移除
                    var dict = InteractableLootbox.Inventories;
                    if (dict != null)
                    {
                        var keysToRemove = new List<int>();
                        foreach (var kv in dict)
                        {
                            if (kv.Value == inventory)
                            {
                                keysToRemove.Add(kv.Key);
                            }
                        }
                        foreach (var key in keysToRemove)
                        {
                            dict.Remove(key);
                        }
                    }
                    
                    // 从服务端墓碑字典中移除
                    var uidsToRemove = new List<int>();
                    foreach (var kv in _srvLootByUid)
                    {
                        if (kv.Value == inventory)
                        {
                            uidsToRemove.Add(kv.Key);
                        }
                    }
                    foreach (var uid in uidsToRemove)
                    {
                        _srvLootByUid.Remove(uid);
                    }
                    
                    // 销毁游戏对象
                    Object.Destroy(lootbox.gameObject);
                    cleanedCount++;
                }
            }

            if (cleanedCount > 0)
            {
                Debug.Log($"[TOMBSTONE] Cleaned up {cleanedCount} empty lootboxes at position {position}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[TOMBSTONE] Failed to cleanup empty lootboxes at position: {e}");
        }
    }

    /// <summary>
    /// 广播墓碑恢复信息给所有客户端
    /// </summary>
    private void BroadcastTombstoneRestored(TombstoneData tombstone, Inventory inventory)
    {
        if (!IsServer || !networkStarted || netManager == null)
        {
            return;
        }

        try
        {
            Debug.Log($"[TOMBSTONE] Broadcasting tombstone restoration: lootUid={tombstone.lootUid}, position={tombstone.position}");
            
            // 使用专门的TOMBSTONE_RESTORE消息类型来通知客户端
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
            
            writer.Reset();
            writer.Put((byte)Op.TOMBSTONE_RESTORE);
            writer.Put(scene);
            writer.Put(tombstone.lootUid);
            writer.PutV3cm(tombstone.position);
            writer.PutQuaternion(tombstone.rotation);
            
            // 广播给所有客户端
            netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
            
            Debug.Log($"[TOMBSTONE] Broadcasted tombstone restoration to all clients: lootUid={tombstone.lootUid}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[TOMBSTONE] Failed to broadcast tombstone restoration: {e}");
        }
    }

    /// <summary>
    /// 处理客户端死亡时上报的剩余物品信息（从墓碑中减去这些物品）
    /// </summary>
    public void HandlePlayerDeathEquipment(string userId, int lootUid, List<int> remainingItemTypeIds)
    {
        Debug.Log($"[TOMBSTONE] HandlePlayerDeathEquipment called - IsServer={IsServer}, TombstonePersistence.Instance={TombstonePersistence.Instance != null}");
        
        if (!IsServer || TombstonePersistence.Instance == null)
        {
            Debug.LogWarning($"[TOMBSTONE] HandlePlayerDeathEquipment early return - IsServer={IsServer}, TombstonePersistence.Instance={TombstonePersistence.Instance != null}");
            return;
        }

        try
        {
            Debug.Log($"[TOMBSTONE] Received player remaining items report: userId={userId}, lootUid={lootUid}, remaining items count={remainingItemTypeIds.Count}");
            
            // 输出剩余物品详细信息
            for (int i = 0; i < remainingItemTypeIds.Count; i++)
            {
                Debug.Log($"[TOMBSTONE] Remaining item {i}: TypeID={remainingItemTypeIds[i]}");
            }
            
            // 从墓碑中移除这些剩余物品（减法操作）
            TombstonePersistence.Instance.RemoveEquipmentFromTombstone(userId, lootUid, remainingItemTypeIds);
            
            Debug.Log($"[TOMBSTONE] Processed remaining items removal for player death: userId={userId}, lootUid={lootUid}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[TOMBSTONE] Failed to handle player remaining items: {e}");
        }
    }

    /// <summary>
    /// 客户端重连后强制重新请求所有可见战利品箱的状态
    /// </summary>
    public void Client_ForceResyncAllLootboxes()
    {
        if (IsServer || !networkStarted)
        {
            return;
        }

        try
        {
            Debug.Log("[LOOT] 开始强制重新同步当前场景的战利品箱");
            
            // 首先清理无效的缓存条目
            CleanupInvalidLootboxCaches();
            
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var lootboxes = Object.FindObjectsOfType<InteractableLootbox>(false); // 只查找活跃的对象
            var syncCount = 0;
            var skippedCount = 0;
            
            foreach (var lootbox in lootboxes)
            {
                if (lootbox == null || lootbox.gameObject == null || lootbox.Inventory == null)
                {
                    skippedCount++;
                    continue;
                }

                // 检查是否在当前场景中
                if (lootbox.gameObject.scene != currentScene)
                {
                    skippedCount++;
                    continue;
                }

                // 检查游戏对象是否处于活跃状态
                if (!lootbox.gameObject.activeInHierarchy)
                {
                    skippedCount++;
                    continue;
                }

                var inv = lootbox.Inventory;
                
                // 跳过私有库存
                if (LootboxDetectUtil.IsPrivateInventory(inv))
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    // 设置为加载状态
                    inv.Loading = true;
                    
                    // 请求最新状态
                    COOPManager.LootNet.Client_RequestLootState(inv);
                    
                    // 设置超时保护
                    KickLootTimeout(inv, 2.0f);
                    
                    syncCount++;
                    
                    Debug.Log($"[LOOT] 请求同步战利品箱: {lootbox.name} at {lootbox.transform.position}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[LOOT] 请求同步战利品箱失败: {lootbox.name}, 错误: {e.Message}");
                    skippedCount++;
                }
            }
            
            Debug.Log($"[LOOT] 完成强制重新同步，当前场景: {currentScene.name}, 请求同步: {syncCount} 个, 跳过: {skippedCount} 个战利品箱");
        }
        catch (Exception e)
        {
            Debug.LogError($"[LOOT] 强制重新同步所有战利品箱失败: {e}");
        }
    }

    /// <summary>
    /// 清理无效的战利品箱缓存条目
    /// </summary>
    private void CleanupInvalidLootboxCaches()
    {
        if (IsServer)
        {
            return;
        }

        try
        {
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var cleanedCount = 0;

            // 清理客户端战利品箱映射中的无效条目
            var invalidUids = new List<int>();
            foreach (var kv in _cliLootByUid)
            {
                var lootUid = kv.Key;
                var inventory = kv.Value;
                
                if (inventory == null)
                {
                    invalidUids.Add(lootUid);
                    continue;
                }
                
                // 检查对应的InteractableLootbox是否还存在且在当前场景中
                var lootbox = LootboxDetectUtil.TryGetInventoryLootBox(inventory);
                if (lootbox == null || lootbox.gameObject == null || 
                    !lootbox.gameObject.activeInHierarchy || 
                    lootbox.gameObject.scene != currentScene)
                {
                    invalidUids.Add(lootUid);
                }
            }
            
            foreach (var uid in invalidUids)
            {
                _cliLootByUid.Remove(uid);
                cleanedCount++;
            }

            // 清理待处理的战利品状态
            var invalidPendingUids = new List<int>();
            foreach (var kv in _pendingLootStatesByUid)
            {
                var lootUid = kv.Key;
                // 如果对应的战利品箱已经不在当前场景，清理待处理状态
                if (!_cliLootByUid.ContainsKey(lootUid))
                {
                    invalidPendingUids.Add(lootUid);
                }
            }
            
            foreach (var uid in invalidPendingUids)
            {
                _pendingLootStatesByUid.Remove(uid);
                cleanedCount++;
            }
            
            if (cleanedCount > 0)
            {
                Debug.Log($"[LOOT] 清理了 {cleanedCount} 个无效的战利品箱缓存条目");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[LOOT] 清理无效战利品箱缓存失败: {e}");
        }
    }
}