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

using EscapeFromDuckovCoopMod.Net;
using EscapeFromDuckovCoopMod.Utils; // 【修复】明确指定使用非泛型 IEnumerator
using System.Reflection;
using IEnumerator = System.Collections.IEnumerator;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

public class Destructible
{
    private readonly Dictionary<uint, HealthSimpleBase> _clientDestructibles = new();


    // 用来避免 dangerFx 重复播放
    private readonly HashSet<uint> _dangerDestructibleIds = new();

    public readonly HashSet<uint> _deadDestructibleIds = new();

    // Destructible registry: id -> HealthSimpleBase
    private readonly Dictionary<uint, HealthSimpleBase> _serverDestructibles = new();
    private NetService Service => NetService.Instance;

    // 【优化】缓存 HalfObsticle 的 isDead 字段，避免重复的 AccessTools 警告
    private static FieldInfo _fieldHalfObsticleIsDead;
    private static bool _halfObsticleFieldInitialized = false;

    // 【优化】防止重复扫描标志
    private bool _scanScheduled = false;

    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;


    public void RegisterDestructible(uint id, HealthSimpleBase hs)
    {
        if (id == 0 || hs == null) return;
        if (IsServer) _serverDestructibles[id] = hs;
        else _clientDestructibles[id] = hs;
    }

    /// <summary>
    /// ✅ 清理可破坏物数据（场景卸载时调用）
    /// </summary>
    public void ClearDestructibles()
    {
        _serverDestructibles.Clear();
        _clientDestructibles.Clear();
        _deadDestructibleIds.Clear();
        _dangerDestructibleIds.Clear();
    }

    // 容错：找不到就全局扫一遍（场景切换后第一次命中时也能兜底）
    public HealthSimpleBase FindDestructible(uint id)
    {
        HealthSimpleBase hs = null;
        if (IsServer) _serverDestructibles.TryGetValue(id, out hs);
        else _clientDestructibles.TryGetValue(id, out hs);
        if (hs) return hs;

        // ✅ 优化：优先从缓存查找，减少 FindObjectsOfType 调用
        if (GameObjectCacheManager.Instance != null)
        {
            hs = GameObjectCacheManager.Instance.Destructibles.FindById(id);
            if (hs)
            {
                RegisterDestructible(id, hs);
                return hs;
            }
        }

        // 兜底：全量扫描并注册
        var all = Object.FindObjectsOfType<HealthSimpleBase>(true);
        foreach (var e in all)
        {
            var tag = e.GetComponent<NetDestructibleTag>() ?? e.gameObject.AddComponent<NetDestructibleTag>();
            RegisterDestructible(tag.id, e);
            if (tag.id == id) hs = e;
        }

        return hs;
    }


    // 客户端：用于 ENV 快照应用，静默切换到“已破坏”外观（不放爆炸特效）
    public void Client_ApplyDestructibleDead_Snapshot(uint id)
    {
        if (_deadDestructibleIds.Contains(id)) return;
        var hs = FindDestructible(id);
        if (!hs) return;

        // Breakable：关正常/危险外观，开破坏外观，关主碰撞体
        var br = hs.GetComponent<Breakable>();
        if (br)
            try
            {
                if (br.normalVisual) br.normalVisual.SetActive(false);
                if (br.dangerVisual) br.dangerVisual.SetActive(false);
                if (br.breakedVisual) br.breakedVisual.SetActive(true);
                if (br.mainCollider) br.mainCollider.SetActive(false);
            }
            catch
            {
            }

        // HalfObsticle：走它自带的 Dead 一下，避免残留交互
        var half = hs.GetComponent<HalfObsticle>();
        if (half)
            try
            {
                half.Dead(new DamageInfo());
            }
            catch
            {
            }

        // 彻底关掉所有 Collider
        try
        {
            foreach (var c in hs.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }
        catch
        {
        }

        _deadDestructibleIds.Add(id);
    }

    private static Transform FindBreakableWallRoot(Transform t)
    {
        var p = t;
        while (p != null)
        {
            var nm = p.name;
            if (!string.IsNullOrEmpty(nm) &&
                nm.IndexOf("BreakableWall", StringComparison.OrdinalIgnoreCase) >= 0)
                return p;
            p = p.parent;
        }

        return null;
    }

    private static uint ComputeStableIdForDestructible(HealthSimpleBase hs)
    {
        if (!hs) return 0u;
        var root = FindBreakableWallRoot(hs.transform);
        if (root == null) root = hs.transform;
        try
        {
            return NetDestructibleTag.ComputeStableId(root.gameObject);
        }
        catch
        {
            return 0u;
        }
    }

    /// <summary>
    /// 【优化】改为协程版本，分帧扫描避免卡顿
    /// </summary>
    private IEnumerator ScanAndMarkInitiallyDeadDestructiblesAsync(int itemsPerFrame = 20)
    {
        if (_deadDestructibleIds == null) yield break;
        if (_serverDestructibles == null || _serverDestructibles.Count == 0) yield break;

        Debug.Log($"[Destructible] 开始扫描 {_serverDestructibles.Count} 个可破坏物，每帧处理 {itemsPerFrame} 个");

        int processed = 0;
        int frameCount = 0;

        foreach (var kv in _serverDestructibles)
        {
            var id = kv.Key;
            var hs = kv.Value;
            if (!hs) continue;
            if (_deadDestructibleIds.Contains(id)) continue;

            var isDead = false;

            // 1) HP 兜底（部分 HSB 有 HealthValue）
            try
            {
                if (hs.HealthValue <= 0f) isDead = true;
            }
            catch
            {
            }

            // 2) Breakable：breaked 外观/主碰撞体关闭 => 视为"已破坏"
            if (!isDead)
                try
                {
                    var br = hs.GetComponent<Breakable>();
                    if (br)
                    {
                        var brokenView = br.breakedVisual && br.breakedVisual.activeInHierarchy;
                        var mainOff = br.mainCollider && !br.mainCollider.activeSelf;
                        if (brokenView || mainOff) isDead = true;
                    }
                }
                catch
                {
                }

            // 3) HalfObsticle：如果存在 isDead 字段，读一下（没有就忽略）
            if (!isDead)
                try
                {
                    var half = hs.GetComponent("HalfObsticle"); // 避免编译期硬引用
                    if (half != null)
                    {
                        // 【优化】只在第一次查找字段，避免重复警告
                        if (!_halfObsticleFieldInitialized)
                        {
                            var t = half.GetType();
                            try
                            {
                                _fieldHalfObsticleIsDead = AccessTools.Field(t, "isDead");
                            }
                            catch { /* 字段不存在或已重命名 */ }
                            _halfObsticleFieldInitialized = true;
                        }

                        // 使用缓存的字段
                        if (_fieldHalfObsticleIsDead != null)
                        {
                            var v = _fieldHalfObsticleIsDead.GetValue(half);
                            if (v is bool && (bool)v) isDead = true;
                        }
                    }
                }
                catch
                {
                }

            if (isDead) _deadDestructibleIds.Add(id);

            processed++;
            // 每处理指定数量就让出一帧
            if (processed % itemsPerFrame == 0)
            {
                frameCount++;
                yield return null;
            }
        }

        Debug.Log($"[Destructible] 扫描完成，共处理 {processed} 个物体，用时 {frameCount} 帧，发现 {_deadDestructibleIds.Count} 个已破坏物体");

        // 【优化】通知UI任务完成
        var syncUI = WaitingSynchronizationUI.Instance;
        if (syncUI != null) syncUI.CompleteTask("destructible", $"发现 {_deadDestructibleIds.Count} 个已破坏物体");
    }

    /// <summary>
    /// 【兼容】保留同步版本供旧代码调用（内部启动协程）
    /// </summary>
    private void ScanAndMarkInitiallyDeadDestructibles()
    {
        // 【优化】防止重复调度
        if (_scanScheduled)
        {
            Debug.Log("[Destructible] 扫描已调度，跳过重复调用");
            return;
        }
        _scanScheduled = true;

        // 使用SceneInitManager调度
        var initManager = SceneInitManager.Instance;
        if (initManager != null)
        {
            initManager.EnqueueDelayedTask(() =>
            {
                // 【优化】增加每帧处理数量，减少总帧数，加快完成速度
                NetService.Instance.StartCoroutine(ScanAndMarkInitiallyDeadDestructiblesAsync(20));
            }, 2.0f, "Destructible_Scan"); // 【优化】延迟到2秒，避免与AI种子同步冲突
        }
        else
        {
            // 降级：直接启动协程
            NetService.Instance.StartCoroutine(ScanAndMarkInitiallyDeadDestructiblesAsync(20));
        }
    }

    // 客户端：死亡复现（实际干活的内部函数）
    // 客户端：死亡复现（Breakable/半障碍/受击FX/碰撞体）
    private void Client_ApplyDestructibleDead_Inner(uint id, Vector3 point, Vector3 normal)
    {
        if (_deadDestructibleIds.Contains(id)) return;
        _deadDestructibleIds.Add(id);

        var hs = FindDestructible(id);
        if (!hs) return;

        // ★★ Breakable：复现 OnDead 里的可视化与爆炸（不做真正的扣血计算）
        var br = hs.GetComponent<Breakable>();
        if (br)
            try
            {
                // 视觉：normal/danger -> breaked
                if (br.normalVisual) br.normalVisual.SetActive(false);
                if (br.dangerVisual) br.dangerVisual.SetActive(false);
                if (br.breakedVisual) br.breakedVisual.SetActive(true);

                // 关闭主碰撞体
                if (br.mainCollider) br.mainCollider.SetActive(false);

                // 爆炸（与源码一致：LevelManager.ExplosionManager.CreateExplosion(...)）:contentReference[oaicite:9]{index=9}
                if (br.createExplosion)
                {
                    // fromCharacter 在客户端可为空，不影响范围伤害的演出
                    var di = br.explosionDamageInfo;
                    di.fromCharacter = null;
                    LevelManager.Instance.ExplosionManager.CreateExplosion(
                        hs.transform.position, br.explosionRadius, di
                    );
                }
            }
            catch
            {
                /* 忽略反编译差异引发的异常 */
            }

        // HalfObsticle：走它自带的 Dead（工程里已有）  
        var half = hs.GetComponent<HalfObsticle>();
        if (half)
            try
            {
                half.Dead(new DamageInfo { damagePoint = point, damageNormal = normal });
            }
            catch
            {
            }

        // 死亡特效（HurtVisual.DeadFx），项目里已有
        var hv = hs.GetComponent<HurtVisual>();
        if (hv && hv.DeadFx) Object.Instantiate(hv.DeadFx, hs.transform.position, hs.transform.rotation);

        // 关掉所有 Collider，防止残留可交互
        foreach (var c in hs.GetComponentsInChildren<Collider>(true)) c.enabled = false;
    }

    // 原来的 ENV_DEAD_EVENT 入口里，改为调用内部函数并记死
    public void Client_ApplyDestructibleDead(NetDataReader r)
    {
        var id = r.GetUInt();
        var point = r.GetV3cm();
        var normal = r.GetDir();
        Client_ApplyDestructibleDead_Inner(id, point, normal);
    }


    // 主机：把受击事件广播给所有客户端：包括当前位置供播放 HitFx，以及当前血量（可用于客户端UI/调试）
    public void Server_BroadcastDestructibleHurt(uint id, float newHealth, DamageInfo dmg)
    {
        if (!networkStarted || !IsServer) return;
        var w = new NetDataWriter();
        w.Put((byte)Op.ENV_HURT_EVENT);
        w.Put(id);
        w.Put(newHealth);
        // Hit视觉信息足够：点+法线
        w.PutV3cm(dmg.damagePoint);
        w.PutDir(dmg.damageNormal.sqrMagnitude < 1e-6f ? Vector3.forward : dmg.damageNormal.normalized);
        netManager.SendSmart(w, Op.ENV_HURT_EVENT);
    }

    public void Server_BroadcastDestructibleDead(uint id, DamageInfo dmg)
    {
        var w = new NetDataWriter();
        w.Put((byte)Op.ENV_DEAD_EVENT);
        w.Put(id);
        w.PutV3cm(dmg.damagePoint);
        w.PutDir(dmg.damageNormal.sqrMagnitude < 1e-6f ? Vector3.up : dmg.damageNormal.normalized);
        netManager.SendSmart(w, Op.ENV_DEAD_EVENT);
    }

    // 客户端：复现受击视觉（不改血量，不触发本地 OnHurt）
    // 客户端：复现受击视觉 + Breakable 的“危险态”显隐
    public void Client_ApplyDestructibleHurt(NetDataReader r)
    {
        var id = r.GetUInt();
        var curHealth = r.GetFloat();
        var point = r.GetV3cm();
        var normal = r.GetDir();

        // 已死亡就不播受击
        if (_deadDestructibleIds.Contains(id)) return;

        // 如果主机侧已经 <= 0，直接走死亡复现兜底
        if (curHealth <= 0f)
        {
            Client_ApplyDestructibleDead_Inner(id, point, normal);
            return;
        }

        var hs = FindDestructible(id);
        if (!hs) return;

        // 播放受击火花（项目里已有的 HurtVisual）
        var hv = hs.GetComponent<HurtVisual>();
        if (hv && hv.HitFx) Object.Instantiate(hv.HitFx, point, Quaternion.LookRotation(normal));

        // Breakable 的“危险态”切换（不改血，只做可视化）
        var br = hs.GetComponent<Breakable>();
        if (br)
            // 危险阈值：源码里是 simpleHealth.HealthValue <= dangerHealth 时切到 danger。:contentReference[oaicite:7]{index=7}
            try
            {
                // 当服务器汇报的血量低于危险阈值，且本地还没进危险态时，切显示 & 播一次 fx
                if (curHealth <= br.dangerHealth && !_dangerDestructibleIds.Contains(id))
                {
                    // normal -> danger
                    if (br.normalVisual) br.normalVisual.SetActive(false);
                    if (br.dangerVisual) br.dangerVisual.SetActive(true);
                    if (br.dangerFx) Object.Instantiate(br.dangerFx, br.transform.position, br.transform.rotation);
                    _dangerDestructibleIds.Add(id);
                }
            }
            catch
            {
                /* 防御式：反编译字段为 null 时静默 */
            }
    }

    /// <summary>
    /// ✅ 优化：使用 DestructibleCache，避免重复的 FindObjectsOfType 调用
    /// </summary>
    public void BuildDestructibleIndex()
    {
        // —— 兜底清空，防止跨图脏状态 —— //
        if (_deadDestructibleIds != null) _deadDestructibleIds.Clear();
        if (_dangerDestructibleIds != null) _dangerDestructibleIds.Clear();

        if (_serverDestructibles != null) _serverDestructibles.Clear();
        if (_clientDestructibles != null) _clientDestructibles.Clear();

        // 【优化】重置扫描标志
        _scanScheduled = false;

        // ✅ 优化：先刷新 DestructibleCache，只执行一次 FindObjectsOfType
        var cacheManager = Utils.GameObjectCacheManager.Instance;
        if (cacheManager != null)
        {
            Debug.Log("[Destructible] 使用 DestructibleCache 构建索引");
            cacheManager.Destructibles.RefreshCache();
        }

        // ✅ 优化：从缓存中获取所有可破坏物，避免再次调用 FindObjectsOfType
        // 降级方案：如果缓存不可用，使用原始方法
        HealthSimpleBase[] all = null;
        if (cacheManager != null)
        {
            // 从缓存中获取所有已索引的可破坏物
            all = cacheManager.Destructibles.GetAllDestructibles();
        }

        // 降级方案：缓存不可用时使用 FindObjectsOfType
        if (all == null || all.Length == 0)
        {
            Debug.LogWarning("[Destructible] DestructibleCache 不可用，降级使用 FindObjectsOfType");
            all = Object.FindObjectsOfType<HealthSimpleBase>(true);
        }

        // 遍历所有 HSB（包含未激活物体，避免漏 index）
        for (var i = 0; i < all.Length; i++)
        {
            var hs = all[i];
            if (!hs) continue;

            var tag = hs.GetComponent<NetDestructibleTag>();
            if (!tag) continue; // 我们只索引带有 NetDestructibleTag 的目标（墙/油桶等）

            // —— 统一计算稳定ID —— //
            var id = ComputeStableIdForDestructible(hs);
            if (id == 0u)
                // 兜底：偶发异常时用自身 gameObject 算一次
                try
                {
                    id = NetDestructibleTag.ComputeStableId(hs.gameObject);
                }
                catch
                {
                }

            tag.id = id;

            // —— 注册到现有索引（与你项目里的一致） —— //
            RegisterDestructible(tag.id, hs);
        }

        Debug.Log($"[Destructible] 索引构建完成，共注册 {_serverDestructibles.Count + _clientDestructibles.Count} 个可破坏物");

        // —— 仅主机：扫描一遍"初始即已破坏"的目标，写进 _deadDestructibleIds —— //
        if (IsServer) // ⇦ 这里用你项目中判断"是否为主机"的字段/属性；若无则换成你原有判断
            ScanAndMarkInitiallyDeadDestructibles();
    }
}