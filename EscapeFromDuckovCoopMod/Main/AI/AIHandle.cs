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
using Unity.Collections;
using Unity.Jobs;
using IEnumerator = System.Collections.IEnumerator;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace EscapeFromDuckovCoopMod;

public class AIHandle
{
    private const byte AI_LOADOUT_VER = 5;


    // --- 反编译类的私有序列化字段直达句柄---
    private static readonly AccessTools.FieldRef<CharacterRandomPreset, bool>
        FR_UsePlayerPreset = AccessTools.FieldRefAccess<CharacterRandomPreset, bool>("usePlayerPreset");

    private static readonly AccessTools.FieldRef<CharacterRandomPreset, CustomFacePreset>
        FR_FacePreset = AccessTools.FieldRefAccess<CharacterRandomPreset, CustomFacePreset>("facePreset");

    private static readonly AccessTools.FieldRef<CharacterRandomPreset, CharacterModel>
        FR_CharacterModel = AccessTools.FieldRefAccess<CharacterRandomPreset, CharacterModel>("characterModel");

    // 反射字段（Health 反编译字段）研究了20年研究出来的
    private static readonly FieldInfo FI_defaultMax =
        typeof(Health).GetField("defaultMaxHealth", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo FI_lastMax =
        typeof(Health).GetField("lastMaxHealth", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo FI__current =
        typeof(Health).GetField("_currentHealth", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo FI_characterCached =
        typeof(Health).GetField("characterCached", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo FI_hasCharacter =
        typeof(Health).GetField("hasCharacter", BindingFlags.NonPublic | BindingFlags.Instance);

    public readonly Dictionary<Health, float> _cliAiMaxOverride = new();

    // aiId -> 待应用的负载（若实体还未就绪）
    public readonly Dictionary<int, float> _cliPendingAiHealth = new();

    // 客户端：AI 血量 pending（cur 已有，这里补 max）
    public readonly Dictionary<int, float> _cliPendingAiMax = new();

    // 【优化】延迟装备同步队列（服务端）
    private readonly Dictionary<int, CharacterMainControl> _pendingLoadouts = new();

    // 待绑定时的暂存（客户端）
    private readonly Dictionary<int, AiAnimState> _pendingAiAnims = new();
    public readonly Dictionary<int, int> aiRootSeeds = new(); // rootId -> seed

    public readonly Dictionary<int, (
        List<(int slot, int tid)> equips,
        List<(int slot, int tid)> weapons,
        string faceJson,
        string modelName,
        int iconType,
        bool showName,
        string displayName
        )> pendingAiLoadouts = new();

    public bool freezeAI = true; // 先冻结用来验证一致性
    public int sceneSeed;

    // ✅ 客户端AI装备同步追踪
    private int _clientAiLoadoutsReceived = 0;
    private float _clientLastAiLoadoutTime = 0f;
    private Coroutine _clientAiLoadoutTimeoutCoroutine = null;

    private NetService Service => NetService.Instance;

    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;
    private Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    private Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;
    private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;


    public void Server_SendAiSeeds(NetPeer target = null)
    {
        if (!IsServer) return;

        aiRootSeeds.Clear();
        // 场景种子：时间戳 XOR Unity 随机
        sceneSeed = Environment.TickCount ^ Random.Range(int.MinValue, int.MaxValue);

        // ✅ 优化：使用缓存管理器获取 AI Root，避免 FindObjectsOfType
        var roots = GameObjectCacheManager.Instance != null
            ? GameObjectCacheManager.Instance.AI.GetCharacterSpawnerRoots(forceRefresh: true)
            : Object.FindObjectsOfType<CharacterSpawnerRoot>(true);

        // 先算出待发送的 (id,seed) 对；对每个 root 同时加入 “主ID(可能用guid)” 和 “兼容ID(强制忽略guid)”
        var pairs = new List<(int id, int seed)>(roots.Length * 2);
        foreach (var r in roots)
        {
            var idA = AITool.StableRootId(r); // 现有策略：SpawnerGuid!=0 就用 guid，否则哈希
            var idB = AITool.StableRootId_Alt(r); // 兼容策略：强制忽略 guid

            var seed = AITool.DeriveSeed(sceneSeed, idA);
            aiRootSeeds[idA] = seed; // 主机本地记录（可用于调试）

            pairs.Add((idA, seed));
            if (idB != idA) pairs.Add((idB, seed)); // 双映射，客户端无论算到哪条 id 都能命中
        }

        var w = writer;
        if (w == null) return;
        w.Reset();
        w.Put((byte)Op.AI_SEED_SNAPSHOT);
        w.Put(sceneSeed);
        w.Put(pairs.Count); // 注意：这里是 “(id,seed) 对”的总数

        foreach (var pr in pairs)
        {
            w.Put(pr.id);
            w.Put(pr.seed);
        }

        if (target == null) CoopTool.BroadcastReliable(w);
        else target.SendSmart(w, Op.AI_SEED_SNAPSHOT);

        Debug.Log($"[AI-SEED] 已发送 {pairs.Count} 条 Root 映射（原 Root 数={roots.Length}）目标={(target == null ? "ALL" : target.EndPoint.ToString())}");
    }

    /// <summary>
    /// 【优化】分批发送AI种子，避免场景加载后卡顿
    /// 使用后台线程计算，只在主线程发送网络包
    /// </summary>
    public IEnumerator Server_SendAiSeedsBatched(NetPeer target = null, int batchSize = 20)
    {
        if (!IsServer) yield break;

        aiRootSeeds.Clear();
        var currentSceneSeed = Environment.TickCount ^ Random.Range(int.MinValue, int.MaxValue);
        sceneSeed = currentSceneSeed;

        // 【步骤1】主线程：查找所有 AI Root
        // ✅ 优化：使用缓存管理器获取 AI Root，避免 FindObjectsOfType
        var roots = GameObjectCacheManager.Instance != null
            ? GameObjectCacheManager.Instance.AI.GetCharacterSpawnerRoots()
            : Object.FindObjectsOfType<CharacterSpawnerRoot>(true);
        if (roots == null || roots.Length == 0)
        {
            Debug.Log("[AI-SEED] 没有找到 AI Root，跳过种子发送");
            yield break;
        }

        Debug.Log($"[AI-SEED] 找到 {roots.Length} 个 AI Root，准备在后台线程计算种子...");

        // 【步骤2】后台线程：计算所有 (id, seed) 对
        List<(int id, int seed)> pairs = null;
        var computeComplete = false;

        var bgManager = EscapeFromDuckovCoopMod.Utils.BackgroundTaskManager.Instance;
        if (bgManager != null)
        {
            bgManager.RunOnBackground(
                // 后台工作：计算种子
                () =>
                {
                    var tempPairs = new List<(int id, int seed)>(roots.Length * 2);
                    foreach (var r in roots)
                    {
                        if (r == null) continue;

                        var idA = AITool.StableRootId(r);
                        var idB = AITool.StableRootId_Alt(r);
                        var seed = AITool.DeriveSeed(currentSceneSeed, idA);

                        tempPairs.Add((idA, seed));
                        if (idB != idA) tempPairs.Add((idB, seed));
                    }
                    return tempPairs;
                },
                // 主线程回调：保存结果
                (result) =>
                {
                    pairs = result;
                    computeComplete = true;
                },
                "AI_Seed_Compute"
            );

            // 等待计算完成
            while (!computeComplete)
            {
                yield return null;
            }
        }
        else
        {
            // 降级：直接在主线程计算
            pairs = new List<(int id, int seed)>(roots.Length * 2);
            foreach (var r in roots)
            {
                if (r == null) continue;

                var idA = AITool.StableRootId(r);
                var idB = AITool.StableRootId_Alt(r);
                var seed = AITool.DeriveSeed(currentSceneSeed, idA);

                aiRootSeeds[idA] = seed;
                pairs.Add((idA, seed));
                if (idB != idA) pairs.Add((idB, seed));
            }
        }

        // 主线程：更新 aiRootSeeds
        foreach (var (id, seed) in pairs)
        {
            aiRootSeeds[id] = seed;
        }

        Debug.Log($"[AI-SEED] 种子计算完成，开始分批发送 {pairs.Count} 条映射，每批 {batchSize} 条");

        // 【步骤3】主线程：分批发送网络包
        for (int i = 0; i < pairs.Count; i += batchSize)
        {
            var batchEnd = Mathf.Min(i + batchSize, pairs.Count);
            var batchCount = batchEnd - i;

            var w = writer;
            if (w == null) yield break;

            w.Reset();
            w.Put((byte)Op.AI_SEED_SNAPSHOT);
            w.Put(sceneSeed);
            w.Put(batchCount);

            for (int j = i; j < batchEnd; j++)
            {
                var pr = pairs[j];
                w.Put(pr.id);
                w.Put(pr.seed);
            }

            if (target == null) CoopTool.BroadcastReliable(w);
            else target.SendSmart(w, Op.AI_SEED_SNAPSHOT);

            Debug.Log($"[AI-SEED] 已发送批次 {i / batchSize + 1}：{batchCount} 条数据");

            yield return null;
        }

        Debug.Log($"[AI-SEED] 所有批次发送完成，共 {pairs.Count} 条");

        // 【优化】通知UI任务完成
        var syncUI = WaitingSynchronizationUI.Instance;
        if (syncUI != null) syncUI.CompleteTask("ai_seeds", $"已发送 {pairs.Count} 条");
    }

    /// <summary>
    /// 【优化】使用 Unity Job System 计算 AI 种子（最高性能版本）
    /// 参考 Fika 的实现，利用 Burst 编译器和多核并行
    /// </summary>
    public IEnumerator Server_SendAiSeedsWithJobSystem(NetPeer target = null, int batchSize = 5)
    {
        if (!IsServer) yield break;

        aiRootSeeds.Clear();
        var currentSceneSeed = Environment.TickCount ^ Random.Range(int.MinValue, int.MaxValue);
        sceneSeed = currentSceneSeed;

        // 【步骤1】主线程：查找所有 AI Root
        // ✅ 优化：使用缓存管理器获取 AI Root，避免 FindObjectsOfType
        var roots = GameObjectCacheManager.Instance != null
            ? GameObjectCacheManager.Instance.AI.GetCharacterSpawnerRoots()
            : Object.FindObjectsOfType<CharacterSpawnerRoot>(true);
        if (roots == null || roots.Length == 0)
        {
            Debug.Log("[AI-SEED] 没有找到 AI Root，跳过种子发送");
            yield break;
        }

        Debug.Log($"[AI-SEED-JobSystem] 找到 {roots.Length} 个 AI Root，使用 Job System 计算...");

        // 【步骤2】准备 Job 数据
        var rootIdsA = new NativeArray<int>(roots.Length, Allocator.TempJob);
        var rootIdsB = new NativeArray<int>(roots.Length, Allocator.TempJob);
        var calculatedSeedsA = new NativeArray<int>(roots.Length, Allocator.TempJob);
        var calculatedSeedsB = new NativeArray<int>(roots.Length, Allocator.TempJob);

        // 填充输入数据（主线程）
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null) continue;
            rootIdsA[i] = AITool.StableRootId(roots[i]);
            rootIdsB[i] = AITool.StableRootId_Alt(roots[i]);
        }

        // 【步骤3】调度 Job（后台线程并行计算）
        var job = new Jobs.AISeedPairCalculationJob
        {
            sceneSeed = currentSceneSeed,
            rootIdsA = rootIdsA,
            rootIdsB = rootIdsB,
            calculatedSeedsA = calculatedSeedsA,
            calculatedSeedsB = calculatedSeedsB
        };

        // 【修复】IJobParallelFor 使用 Schedule 方法
        // 并行执行，每个工作线程处理64个元素
        var handle = job.Schedule(roots.Length, 64, default);

        // 等待 Job 完成（不阻塞主线程）
        while (!handle.IsCompleted)
        {
            yield return null;
        }
        handle.Complete();

        Debug.Log($"[AI-SEED-JobSystem] Job 计算完成，准备构建映射表...");

        // 【步骤4】主线程：构建 pairs 列表和更新 aiRootSeeds
        var pairs = new List<(int id, int seed)>(roots.Length * 2);
        for (int i = 0; i < roots.Length; i++)
        {
            int idA = rootIdsA[i];
            int idB = rootIdsB[i];
            int seedA = calculatedSeedsA[i];
            int seedB = calculatedSeedsB[i];

            aiRootSeeds[idA] = seedA;
            pairs.Add((idA, seedA));

            if (idB != idA)
            {
                pairs.Add((idB, seedB));
            }
        }

        // 清理 NativeArray
        rootIdsA.Dispose();
        rootIdsB.Dispose();
        calculatedSeedsA.Dispose();
        calculatedSeedsB.Dispose();

        Debug.Log($"[AI-SEED-JobSystem] 开始分批发送 {pairs.Count} 条映射，每批 {batchSize} 条");

        // 【步骤5】主线程：分批发送网络包
        for (int i = 0; i < pairs.Count; i += batchSize)
        {
            var batchEnd = Mathf.Min(i + batchSize, pairs.Count);
            var batchCount = batchEnd - i;

            var w = writer;
            if (w == null) yield break;

            w.Reset();
            w.Put((byte)Op.AI_SEED_SNAPSHOT);
            w.Put(sceneSeed);
            w.Put(batchCount);

            for (int j = i; j < batchEnd; j++)
            {
                var pr = pairs[j];
                w.Put(pr.id);
                w.Put(pr.seed);
            }

            if (target == null) CoopTool.BroadcastReliable(w);
            else target.SendSmart(w, Op.AI_SEED_SNAPSHOT);

            yield return null;
        }

        Debug.Log($"[AI-SEED-JobSystem] 所有批次发送完成，共 {pairs.Count} 条");
    }


    public void HandleAiSeedSnapshot(NetDataReader r)
    {
        var receivedSceneSeed = r.GetInt();

        // 【优化】如果是新场景种子，清空旧数据；否则是追加数据（分批接收）
        if (sceneSeed != receivedSceneSeed)
        {
            sceneSeed = receivedSceneSeed;
            aiRootSeeds.Clear();
        }

        var n = r.GetInt();

        // 【优化】使用场景初始化管理器分批处理，避免一次性处理大量数据卡顿
        var initManager = SceneInitManager.Instance;
        if (initManager != null && n > 5)
        {
            // 分批处理：每批5条（进一步降低批次大小）
            var batchSize = 5;
            for (var i = 0; i < n; i += batchSize)
            {
                var currentBatchSize = Mathf.Min(batchSize, n - i);
                var batchData = new List<(int id, int seed)>(currentBatchSize);

                for (var j = 0; j < currentBatchSize; j++)
                {
                    var id = r.GetInt();
                    var seed = r.GetInt();
                    batchData.Add((id, seed));
                }

                // 将每批数据添加到任务队列
                initManager.EnqueueTask(() =>
                {
                    foreach (var (id, seed) in batchData)
                    {
                        aiRootSeeds[id] = seed;
                    }
                }, $"AI_Seed_Batch_{i / batchSize}");
            }

            Debug.Log($"[AI-SEED] 收到 {n} 个 Root 种子，已加入分批处理队列");
        }
        else
        {
            // 数据量小或管理器不可用，直接处理
            for (var i = 0; i < n; i++)
            {
                var id = r.GetInt();
                var seed = r.GetInt();
                aiRootSeeds[id] = seed;
            }

            Debug.Log($"[AI-SEED] 收到 {n} 个 Root 的种子");
        }
    }


    public void RegisterAi(int aiId, CharacterMainControl cmc)
    {
        if (!AITool.IsRealAI(cmc)) return;
        AITool.aiById[aiId] = cmc;

        // 【优化】快速路径：仅注册ID映射，其他操作延后处理
        float pendCur = -1f, pendMax = -1f;
        if (_cliPendingAiHealth.TryGetValue(aiId, out var pc))
        {
            pendCur = pc;
            _cliPendingAiHealth.Remove(aiId);
        }

        if (_cliPendingAiMax.TryGetValue(aiId, out var pm))
        {
            pendMax = pm;
            _cliPendingAiMax.Remove(aiId);
        }

        // 【优化】Health 相关操作保持同步，因为涉及游戏逻辑
        var h = cmc?.Health;
        if (h)
        {
            if (pendMax > 0f)
            {
                _cliAiMaxOverride[h] = pendMax;
                try
                {
                    FI_defaultMax?.SetValue(h, Mathf.RoundToInt(pendMax));
                }
                catch
                {
                }

                try
                {
                    FI_lastMax?.SetValue(h, -12345f);
                }
                catch
                {
                }

                try
                {
                    h.OnMaxHealthChange?.Invoke(h);
                }
                catch
                {
                }
            }

            if (pendCur >= 0f || pendMax > 0f)
            {
                var applyMax = pendMax > 0f ? pendMax : h.MaxHealth;
                try
                {
                    HealthM.Instance.ForceSetHealth(h, applyMax, Mathf.Max(0f, pendCur >= 0f ? pendCur : h.CurrentHealth));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AI-REG] aiId={aiId} ForceSetHealth失败: {ex.Message}");
                }
            }
        }

        // 【优化】不再立即发送装备同步，改为延迟批量发送
        if (IsServer && cmc)
        {
            lock (_pendingLoadouts)
            {
                _pendingLoadouts[aiId] = cmc;
            }
        }

        if (!IsServer && cmc)
        {
            var follower = cmc.GetComponent<NetAiFollower>();
            if (!follower) follower = cmc.gameObject.AddComponent<NetAiFollower>();

            // 客户端兴趣圈可见性
            if (!cmc.GetComponent<NetAiVisibilityGuard>())
                cmc.gameObject.AddComponent<NetAiVisibilityGuard>();

            try
            {
                var tag = cmc.GetComponent<NetAiTag>();
                if (tag == null) tag = cmc.gameObject.AddComponent<NetAiTag>();
                if (tag.aiId != aiId) tag.aiId = aiId;
            }
            catch
            {
            }

            if (!cmc.GetComponent<RemoteReplicaTag>()) cmc.gameObject.AddComponent<RemoteReplicaTag>();

            if (_pendingAiAnims.TryGetValue(aiId, out var st))
            {
                if (!follower) follower = cmc.gameObject.AddComponent<NetAiFollower>();
                follower.SetAnim(st.speed, st.dirX, st.dirY, st.hand, st.gunReady, st.dashing);
                _pendingAiAnims.Remove(aiId);
            }
        }

        // 消化 pending（装备/武器/脸/模型名/图标/是否显示名）
        if (pendingAiLoadouts.TryGetValue(aiId, out var data))
        {
            pendingAiLoadouts.Remove(aiId);
            Client_ApplyAiLoadout(aiId, data.equips, data.weapons, data.faceJson, data.modelName, data.iconType, data.showName, data.displayName).Forget();
        }
    }


    /// <summary>
    /// ✅ 客户端：重置AI装备同步追踪（场景切换时调用）
    /// </summary>
    public void Client_ResetAiLoadoutTracking()
    {
        _clientAiLoadoutsReceived = 0;
        _clientLastAiLoadoutTime = Time.time; // ✅ 记录开始时间
        if (_clientAiLoadoutTimeoutCoroutine != null)
        {
            ModBehaviourF.Instance.StopCoroutine(_clientAiLoadoutTimeoutCoroutine);
            _clientAiLoadoutTimeoutCoroutine = null;
        }

        // ✅ 启动初始超时检查（10秒后如果还没收到任何消息，就认为没有AI）
        _clientAiLoadoutTimeoutCoroutine = ModBehaviourF.Instance.StartCoroutine(Client_CheckAiLoadoutTimeout());
        Debug.Log("[AI-LOADOUT] 客户端追踪已重置，启动10秒初始超时检查");
    }

    /// <summary>
    /// ✅ 清理AI装备同步追踪数据（场景卸载时调用）
    /// </summary>
    public void ClearAiLoadoutTracking()
    {
        _clientAiLoadoutsReceived = 0;
        _clientLastAiLoadoutTime = 0f;

        if (_clientAiLoadoutTimeoutCoroutine != null && ModBehaviourF.Instance != null)
        {
            try
            {
                ModBehaviourF.Instance.StopCoroutine(_clientAiLoadoutTimeoutCoroutine);
            }
            catch { }
            _clientAiLoadoutTimeoutCoroutine = null;
        }
    }

    /// <summary>
    /// ✅ 客户端：收到AI装备消息时调用
    /// </summary>
    public void Client_OnAiLoadoutReceived()
    {
        if (IsServer) return;

        _clientAiLoadoutsReceived++;
        _clientLastAiLoadoutTime = Time.time;

        // 更新UI进度
        var syncUI = WaitingSynchronizationUI.Instance;
        if (syncUI != null)
        {
            syncUI.UpdateTaskStatus("ai_loadouts", false, $"已接收 {_clientAiLoadoutsReceived} 个AI");
        }

        // ✅ 不需要重启协程，只需要更新时间戳即可
        // 协程会持续监控 _clientLastAiLoadoutTime 的变化
        Debug.Log($"[AI-LOADOUT] 收到第 {_clientAiLoadoutsReceived} 个AI装备消息");
    }

    /// <summary>
    /// ✅ 客户端：检查AI装备同步超时
    /// - 初始10秒超时：如果没收到任何消息，认为场景没有AI
    /// - 静默5秒超时：收到消息后5秒内没有新消息，认为同步完成
    /// </summary>
    private IEnumerator Client_CheckAiLoadoutTimeout()
    {
        float initialTimeout = 10f;
        float silentTimeout = 5f;
        float checkInterval = 0.5f;
        float startTime = Time.time;

        while (true)
        {
            yield return new WaitForSeconds(checkInterval);

            float elapsed = Time.time - startTime;

            // 如果已经收到消息，检查静默超时
            if (_clientAiLoadoutsReceived > 0)
            {
                float timeSinceLastMessage = Time.time - _clientLastAiLoadoutTime;
                if (timeSinceLastMessage >= silentTimeout)
                {
                    Debug.Log($"[AI-LOADOUT] ✓ 客户端同步完成（静默{silentTimeout}秒），共接收 {_clientAiLoadoutsReceived} 个AI");

                    var syncUI = WaitingSynchronizationUI.Instance;
                    if (syncUI != null)
                    {
                        syncUI.CompleteTask("ai_loadouts", $"已接收 {_clientAiLoadoutsReceived} 个AI");
                    }

                    _clientAiLoadoutTimeoutCoroutine = null;
                    yield break;
                }
            }
            // 如果超过初始超时还没收到任何消息，认为场景无AI
            else if (elapsed >= initialTimeout)
            {
                Debug.Log($"[AI-LOADOUT] ✓ 客户端同步完成（初始超时{initialTimeout}秒，场景无AI）");

                var syncUI = WaitingSynchronizationUI.Instance;
                if (syncUI != null)
                {
                    syncUI.CompleteTask("ai_loadouts", "场景无AI");
                }

                _clientAiLoadoutTimeoutCoroutine = null;
                yield break;
            }
        }
    }

    /// <summary>
    /// 【优化】批量发送AI装备同步，避免场景加载后卡顿
    /// </summary>
    public IEnumerator Server_SendAiLoadoutsBatched(int batchSize = 2)
    {
        if (!IsServer) yield break;

        List<(int aiId, CharacterMainControl cmc)> loadouts = null;
        lock (_pendingLoadouts)
        {
            if (_pendingLoadouts.Count == 0)
            {
                Debug.Log("[AI-LOADOUT] 没有待同步的装备");
                yield break;
            }

            loadouts = new List<(int, CharacterMainControl)>(_pendingLoadouts.Count);
            foreach (var kv in _pendingLoadouts)
            {
                if (kv.Value != null)
                    loadouts.Add((kv.Key, kv.Value));
            }
            _pendingLoadouts.Clear();
        }

        Debug.Log($"[AI-LOADOUT] 开始分批发送 {loadouts.Count} 个AI的装备，每批 {batchSize} 个");

        int batchIndex = 0;
        for (int i = 0; i < loadouts.Count; i += batchSize)
        {
            int count = Mathf.Min(batchSize, loadouts.Count - i);
            batchIndex++;

            for (int j = 0; j < count; j++)
            {
                var (aiId, cmc) = loadouts[i + j];
                if (cmc != null)
                {
                    try
                    {
                        Server_BroadcastAiLoadout(aiId, cmc);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[AI-LOADOUT] 发送 aiId={aiId} 装备时出错: {ex.Message}");
                    }
                }
            }

            Debug.Log($"[AI-LOADOUT] 已发送批次 {batchIndex}：{count} 个AI");

            // 【优化】让出更多时间，避免网络拥堵
            yield return null; // 第一帧
            yield return null; // 第二帧：进一步降低网络压力
        }

        Debug.Log($"[AI-LOADOUT] 所有批次发送完成，共 {loadouts.Count} 个AI");

        // 【优化】通知UI任务完成
        var syncUI = WaitingSynchronizationUI.Instance;
        if (syncUI != null) syncUI.CompleteTask("ai_loadouts", $"已发送 {loadouts.Count} 个AI");
    }

    public void Server_BroadcastAiLoadout(int aiId, CharacterMainControl cmc)
    {
        if (!IsServer || cmc == null) return;

        writer.Reset();
        writer.Put((byte)Op.AI_LOADOUT_SNAPSHOT);
        writer.Put(AI_LOADOUT_VER); // v4
        writer.Put(aiId);

        // ---- 装备（5 槽）----
        var eqList = AITool.GetLocalAIEquipment(cmc);
        if (eqList == null)
        {
            Debug.LogWarning($"[AI-LOADOUT] aiId={aiId} 装备列表为null，写入空列表");
            eqList = new List<EquipmentSyncData>();
        }

        writer.Put(eqList.Count);
        foreach (var eq in eqList)
        {
            try
            {
                writer.Put(eq.SlotHash);

                // 线上的老协议依然是 int tid，这里从 string ItemId 安全转换
                var tid = 0;
                if (!string.IsNullOrEmpty(eq.ItemId))
                    int.TryParse(eq.ItemId, out tid);

                writer.Put(tid);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AI-LOADOUT] aiId={aiId} 写入装备时出错: {ex.Message}");
            }
        }

        // ---- 武器 ----
        var listW = new List<(int slot, int tid)>();
        try
        {
            var gun = cmc?.GetGun();
            var melee = cmc?.GetMeleeWeapon();
            if (gun != null) listW.Add(((int)gun.handheldSocket, gun.Item ? gun.Item.TypeID : 0));
            if (melee != null) listW.Add(((int)melee.handheldSocket, melee.Item ? melee.Item.TypeID : 0));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AI-LOADOUT] aiId={aiId} 获取武器时出错: {ex.Message}");
        }

        writer.Put(listW.Count);
        foreach (var p in listW)
        {
            try
            {
                writer.Put(p.slot);
                writer.Put(p.tid);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AI-LOADOUT] aiId={aiId} 写入武器时出错: {ex.Message}");
            }
        }

        // ---- 脸 JSON（主机权威）----
        string faceJson = null;
        //try
        //{
        //    var preset = cmc.characterPreset;
        //    if (preset)
        //    {
        //        if (FR_UsePlayerPreset(preset))
        //        {
        //            var data = LevelManager.Instance.CustomFaceManager.LoadMainCharacterSetting();
        //            faceJson = JsonUtility.ToJson(data);
        //        }
        //        else
        //        {
        //            var fp = FR_FacePreset(preset);
        //            if (fp != null) faceJson = JsonUtility.ToJson(fp.settings);
        //        }
        //    }
        //}
        //catch { }
        writer.Put(!string.IsNullOrEmpty(faceJson));
        if (!string.IsNullOrEmpty(faceJson)) writer.Put(faceJson);


        // ---- 模型名 + 图标类型 + showName(主机裁决) ----
        var modelName = AIName.NormalizePrefabName(cmc.characterModel ? cmc.characterModel.name : null);

        var iconType = 0;
        var showName = false;
        try
        {
            var pr = cmc.characterPreset;
            if (pr)
            {
                var e = (CharacterIconTypes)iconType;
                // 1) 若是 none，尝试用本地预设再取一次（有些预设在运行时被填充）
                if (e == CharacterIconTypes.none && pr.GetCharacterIcon() != null)
                    iconType = (int)AIName.FR_IconType(pr);

                // 2) 对 boss / elete 强制 showName=true，避免客户端再兜底
                e = (CharacterIconTypes)iconType;
                if (!showName && (e == CharacterIconTypes.boss || e == CharacterIconTypes.elete))
                    showName = true;
            }
        }
        catch
        {
            /* 忽略兜底异常 */
        }

        writer.Put(!string.IsNullOrEmpty(modelName));
        if (!string.IsNullOrEmpty(modelName)) writer.Put(modelName);
        writer.Put(iconType);
        writer.Put(showName); // v4 字段

        // v5：名字文本（主机裁决）
        string displayName = null;
        try
        {
            var preset = cmc.characterPreset;
            if (preset) displayName = preset.Name; // 或者你自己的名字来源
        }
        catch
        {
        }

        writer.Put(!string.IsNullOrEmpty(displayName)); // hasName
        if (!string.IsNullOrEmpty(displayName))
            writer.Put(displayName);


        Debug.Log($"[AI-SEND] ver={AI_LOADOUT_VER} aiId={aiId} model='{modelName}' icon={iconType} showName={showName}");

        CoopTool.BroadcastReliable(writer);

        if (iconType == (int)CharacterIconTypes.none)
            AIRequest.Instance.Server_TryRebroadcastIconLater(aiId, cmc);
    }

    public UniTask Client_ApplyAiLoadout(
        int aiId,
        List<(int slot, int tid)> equips,
        List<(int slot, int tid)> weapons,
        string faceJson,
        string modelName,
        int iconType,
        bool showName)
    {
        return Client_ApplyAiLoadout(
            aiId, equips, weapons, faceJson, modelName, iconType, showName, null);
    }

    public async UniTask Client_ApplyAiLoadout(
        int aiId,
        List<(int slot, int tid)> equips,
        List<(int slot, int tid)> weapons,
        string faceJson,
        string modelName,
        int iconType,
        bool showName,
        string displayNameFromHost)
    {
        if (!AITool.aiById.TryGetValue(aiId, out var cmc) || !cmc) return;

        // 1) 必要时切换模型（保持你原来的逻辑）
        CharacterModel prefab = null;
        if (!string.IsNullOrEmpty(modelName))
            prefab = AIName.FindCharacterModelByName_Any(modelName);
        if (!prefab)
            try
            {
                var pr = cmc.characterPreset;
                if (pr) prefab = FR_CharacterModel(pr);
            }
            catch
            {
            }

        try
        {
            var cur = cmc.characterModel;
            var curName = AIName.NormalizePrefabName(cur ? cur.name : null);
            var tgtName = AIName.NormalizePrefabName(prefab ? prefab.name : null);
            if (prefab && !string.Equals(curName, tgtName, StringComparison.OrdinalIgnoreCase))
            {
                var inst = Object.Instantiate(prefab);
                Debug.Log($"[AI-APPLY] aiId={aiId} SetCharacterModel -> '{tgtName}' (cur='{curName}')");
                cmc.SetCharacterModel(inst);
            }
        }
        catch
        {
        }

        // 等待模型就绪
        var model = cmc.characterModel;
        var guard = 0;
        while (!model && guard++ < 120)
        {
            await UniTask.Yield();
            model = cmc.characterModel;
        }

        if (!model) return;

        // 2) 名字 & 图标：以主机为主，客户端兜底 + 对特殊类型强制显示名字
        try
        {
            // 仅为了生态一致性，可选地把字段回写到 preset；但展示完全不用它
            var preset = cmc.characterPreset;
            if (preset)
            {
                try
                {
                    AIName.FR_IconType(preset) = (CharacterIconTypes)iconType;
                }
                catch
                {
                }

                try
                {
                    preset.showName = showName;
                }
                catch
                {
                }
            }

            // 1) 通过统一样式解析枚举 → Sprite（不从本地 preset 拿）
            var sprite = AIName.ResolveIconSprite(iconType);

            // UIStyle 可能尚未 ready；若是 null，延迟几帧重试兜底
            var tries = 0;
            while (sprite == null && tries++ < 5)
            {
                await UniTask.Yield();
                sprite = AIName.ResolveIconSprite(iconType);
            }

            // 2) 名字就是主机下发的文本；不做本地推导
            var displayName = showName ? displayNameFromHost : null;

            await AIName.RefreshNameIconWithRetries(cmc, iconType, showName, displayNameFromHost);


            Debug.Log($"[AI-APPLY] aiId={aiId} icon={(CharacterIconTypes)iconType} showName={showName} name='{displayName ?? "(null)"}'");
            Debug.Log(
                $"[NOW AI] aiId={aiId} icon={Traverse.Create(cmc.characterPreset).Field<CharacterIconTypes>("characterIconType").Value} showName={showName} name='{Traverse.Create(cmc.characterPreset).Field<string>("nameKey").Value ?? "(null)"}'");
        }
        catch
        {
        }

        // 3) 服装（保持你原来的逻辑）
        foreach (var (slotHash, typeId) in equips)
        {
            if (typeId <= 0) continue;

            var item = await COOPManager.GetItemAsync(typeId);
            if (!item) continue;

            if (slotHash == CharacterEquipmentController.armorHash || slotHash == 100)
                COOPManager.ChangeArmorModel(model, item);
            else if (slotHash == CharacterEquipmentController.helmatHash || slotHash == 200)
                COOPManager.ChangeHelmatModel(model, item);
            else if (slotHash == CharacterEquipmentController.faceMaskHash || slotHash == 300)
                COOPManager.ChangeFaceMaskModel(model, item);
            else if (slotHash == CharacterEquipmentController.backpackHash || slotHash == 400)
                COOPManager.ChangeBackpackModel(model, item);
            else if (slotHash == CharacterEquipmentController.headsetHash || slotHash == 500)
                COOPManager.ChangeHeadsetModel(model, item);
        }

        // 4)（如果你原来这里还有其它步骤，保持不动）

        // 5) 武器 —— ★ 修复“创建 pickup agent 失败，已有 agent”
        // 先清三处，再等一帧，让 Destroy 真正生效
        COOPManager.ChangeWeaponModel(model, null, HandheldSocketTypes.normalHandheld);
        COOPManager.ChangeWeaponModel(model, null, HandheldSocketTypes.meleeWeapon);
        COOPManager.ChangeWeaponModel(model, null, HandheldSocketTypes.leftHandSocket);
        await UniTask.NextFrame(); // 关键：等一帧等待销毁完成

        foreach (var (slotHash, typeId) in weapons)
        {
            if (typeId <= 0) continue;

            var item = await COOPManager.GetItemAsync(typeId);
            if (!item) continue;

            // 解析插槽：未知值统一右手
            var socket = Enum.IsDefined(typeof(HandheldSocketTypes), slotHash)
                ? (HandheldSocketTypes)slotHash
                : HandheldSocketTypes.normalHandheld;

            // —— 在挂载前，确保 Item 自身没有残留 ActiveAgent —— 
            try
            {
                var ag = item.ActiveAgent;
                if (ag && ag.gameObject) Object.Destroy(ag.gameObject);
            }
            catch
            {
                /* ignore */
            }

            try
            {
                item.Detach();
            }
            catch
            {
                /* ignore */
            }

            // 保险：目标槽再清一次，并等到帧尾
            COOPManager.ChangeWeaponModel(model, null, socket);
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);

            // 尝试挂载，若仍因“已有 agent”失败，则兜底重试一次
            try
            {
                COOPManager.ChangeWeaponModel(model, item, socket);
            }
            catch (Exception e)
            {
                var msg = e.Message ?? string.Empty;
                if (msg.Contains("已有agent") || msg.IndexOf("pickup agent", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // 再杀一次残留 + 再清 + 再等一帧，然后重试一次
                    try
                    {
                        var ag2 = item.ActiveAgent;
                        if (ag2 && ag2.gameObject) Object.Destroy(ag2.gameObject);
                    }
                    catch
                    {
                    }

                    try
                    {
                        item.Detach();
                    }
                    catch
                    {
                    }

                    COOPManager.ChangeWeaponModel(model, null, socket);
                    await UniTask.NextFrame();

                    COOPManager.ChangeWeaponModel(model, item, socket);
                }
                else
                {
                    throw; // 其它错误别吞
                }
            }
        }

        AITool.EnsureMagicBlendBound(cmc);

        if (!string.IsNullOrEmpty(faceJson)) CustomFace.ApplyFaceJsonToModel(model, faceJson);
    }


    public void Server_BroadcastAiTransforms()
    {
        if (!IsServer || AITool.aiById.Count == 0) return;

        writer.Reset();
        writer.Put((byte)Op.AI_TRANSFORM_SNAPSHOT);
        // 统计有效数量
        var cnt = 0;
        foreach (var kv in AITool.aiById)
            if (kv.Value)
                cnt++;
        writer.Put(cnt);
        foreach (var kv in AITool.aiById)
        {
            var cmc = kv.Value;
            if (!cmc) continue;
            var t = cmc.transform;
            writer.Put(kv.Key); // aiId
            writer.PutV3cm(t.position); // 压缩位置
            var fwd = cmc.characterModel.transform.rotation * Vector3.forward;
            writer.PutDir(fwd);
        }

        CoopTool.BroadcastReliable(writer);
    }

    public void Server_BroadcastAiAnimations()
    {
        if (!IsServer || AITool.aiById == null || AITool.aiById.Count == 0) return;

        var list = new List<(int id, AiAnimState st)>(AITool.aiById.Count);
        foreach (var kv in AITool.aiById)
        {
            var id = kv.Key;
            var cmc = kv.Value;
            if (!cmc) continue;

            // ① 必须是真正的 AI，且存活
            if (!AITool.IsRealAI(cmc)) continue; // 你工程里已有这个工具方法

            // ② GameObject/组件必须处于激活状态
            if (!cmc.gameObject.activeInHierarchy || !cmc.enabled) continue;

            var magic = cmc.GetComponentInChildren<CharacterAnimationControl_MagicBlend>(true);
            var anim = magic ? magic.animator : cmc.GetComponentInChildren<Animator>(true);
            if (!anim || !anim.isActiveAndEnabled || !anim.gameObject.activeInHierarchy) continue;

            var st = new AiAnimState
            {
                speed = anim.GetFloat(Animator.StringToHash("MoveSpeed")),
                dirX = anim.GetFloat(Animator.StringToHash("MoveDirX")),
                dirY = anim.GetFloat(Animator.StringToHash("MoveDirY")),
                hand = anim.GetInteger(Animator.StringToHash("HandState")),
                gunReady = anim.GetBool(Animator.StringToHash("GunReady")),
                dashing = anim.GetBool(Animator.StringToHash("Dashing"))
            };
            list.Add((id, st));
        }

        if (list.Count == 0) return;

        // —— 发送（保持你原来的分包逻辑）——
        const DeliveryMethod METHOD = DeliveryMethod.Unreliable;
        var maxSingle = 1200;
        try
        {
            maxSingle = connectedPeer != null ? connectedPeer.GetMaxSinglePacketSize(METHOD) : maxSingle;
        }
        catch
        {
        }

        const int HEADER = 16;
        const int ENTRY = 24;

        var budget = Math.Max(256, maxSingle - HEADER);
        var perPacket = Math.Max(1, budget / ENTRY);

        for (var i = 0; i < list.Count; i += perPacket)
        {
            var n = Math.Min(perPacket, list.Count - i);

            writer.Reset();
            writer.Put((byte)Op.AI_ANIM_SNAPSHOT);
            writer.Put(n);
            for (var j = 0; j < n; ++j)
            {
                var e = list[i + j];
                writer.Put(e.id);
                writer.Put(e.st.speed);
                writer.Put(e.st.dirX);
                writer.Put(e.st.dirY);
                writer.Put(e.st.hand);
                writer.Put(e.st.gunReady);
                writer.Put(e.st.dashing);
            }

            netManager.SendToAll(writer, METHOD);
        }
    }

    // 客户端：应用增量，不清空，直接补/改
    public void HandleAiSeedPatch(NetDataReader r)
    {
        var n = r.GetInt();
        for (var i = 0; i < n; i++)
        {
            var id = r.GetInt();
            var seed = r.GetInt();
            aiRootSeeds[id] = seed;
        }

        Debug.Log("[AI-SEED] 应用增量 Root 种子数: " + n);
    }
}