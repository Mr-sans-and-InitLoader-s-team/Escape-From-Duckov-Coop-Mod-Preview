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
using EscapeFromDuckovCoopMod.Net;
using EscapeFromDuckovCoopMod.Utils; // 引入智能发送扩展方法
using EscapeFromDuckovCoopMod.Utils.Logger.Core;
using EscapeFromDuckovCoopMod.Utils.Logger.Tools;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine.SceneManagement;
using static EscapeFromDuckovCoopMod.LootNet;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

internal static class ServerTuning
{
    // 远端近战伤害倍率（按需调整）
    public const float RemoteMeleeCharScale = 1.00f; // 打角色：保持原汁原味
    public const float RemoteMeleeEnvScale = 1.5f; // 打环境：稍微抬一点

    // 打环境/建筑时，用 null 作为“攻击者”，避免基于攻击者的二次系数让伤害被稀释
    public const bool UseNullAttackerForEnv = true;
}

// ===== 本人无意在此堆，只是开始想要管理好的，后来懒的开新的类了导致这个类不堪重负维护有一点点小复杂 2025/10/27 =====
public class ModBehaviourF : MonoBehaviour
{
    private const float SELF_ACCEPT_WINDOW = 0.30f;
    private const float EnsureRemoteInterval = 1.0f; // 每秒兜底一次，够用又不吵
    private const float ENV_SYNC_INTERVAL = 1.0f; // 每 1 秒广播一次；可按需 0.5~2 调
    private const float AI_TF_INTERVAL = 0.05f;
    private const float AI_ANIM_INTERVAL = 0.10f; // 10Hz 动画参数广播
    private const float AI_NAMEICON_INTERVAL = 10f;

    private const float SELF_MUTE_SEC = 0.10f;
    public static ModBehaviourF Instance; //一切的开始 Hello World!

    public static CustomFaceSettingData localPlayerCustomFace;

    public static bool LogAiHpDebug = false; // 需要时改为 true，打印 [AI-HP] 日志

    public static bool LogAiLoadoutDebug = true;

    // --- 反编译类的私有序列化字段直达句柄---
    private static readonly AccessTools.FieldRef<CharacterRandomPreset, bool> FR_UsePlayerPreset =
        AccessTools.FieldRefAccess<CharacterRandomPreset, bool>("usePlayerPreset");

    private static readonly AccessTools.FieldRef<
        CharacterRandomPreset,
        CustomFacePreset
    > FR_FacePreset = AccessTools.FieldRefAccess<CharacterRandomPreset, CustomFacePreset>(
        "facePreset"
    );

    private static readonly AccessTools.FieldRef<
        CharacterRandomPreset,
        CharacterModel
    > FR_CharacterModel = AccessTools.FieldRefAccess<CharacterRandomPreset, CharacterModel>(
        "characterModel"
    );

    private static readonly AccessTools.FieldRef<
        CharacterRandomPreset,
        CharacterIconTypes
    > FR_IconType = AccessTools.FieldRefAccess<CharacterRandomPreset, CharacterIconTypes>(
        "characterIconType"
    );

    public static readonly Dictionary<int, Pending> map = new();

    private static Transform _fallbackMuzzleAnchor;
    public float broadcastTimer;
    public float syncTimer;

    public bool Pausebool;
    public int _clientLootSetupDepth;

    public GameObject aiTelegraphFx;
    public DamageInfo _lastDeathInfo;

    // 客户端：是否把远端 AI 全部常显（默认 true）
    public bool Client_ForceShowAllRemoteAI = true;

    // 客户端：远端玩家待应用的外观缓存
    private readonly Dictionary<string, string> _cliPendingFace = new();

    // 发送去抖：只有发生明显改动才发，避免带宽爆炸
    private readonly Dictionary<int, (Vector3 pos, Vector3 dir)> _lastAiSent = new();

    // 待绑定时的暂存（客户端）
    private readonly Dictionary<int, AiAnimState> _pendingAiAnims = new();

    private readonly Queue<(int id, Vector3 p, Vector3 f)> _pendingAiTrans = new();

    private readonly Dictionary<
        int,
        (int capacity, List<(int pos, ItemSnapshot snap)>)
    > _pendingLootStates = new();

    private readonly KeyCode readyKey = KeyCode.J;

    // 主机端的节流定时器
    private float _aiAnimTimer;

    private float _aiNameIconTimer;

    private float _aiTfTimer;

    private float _ensureRemoteTick = 0f;
    private bool _envReqOnce = false;
    private string _envReqSid;

    private float _envSyncTimer;

    private int _spectateIdx = -1;
    private float _spectateNextSwitchTime;

    private bool isinit; // 判断玩家装备slot监听初始哈的

    private bool isinit2;
    private NetService Service => NetService.Instance;
    public bool IsServer => Service != null && Service.IsServer;
    public NetManager netManager => Service?.netManager;
    public NetDataWriter writer => Service?.writer;
    public NetPeer connectedPeer => Service?.connectedPeer;
    public PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    public bool networkStarted => Service != null && Service.networkStarted;
    public string manualIP => Service?.manualIP;
    public List<string> hostList => Service?.hostList;
    public HashSet<string> hostSet => Service?.hostSet;
    public bool isConnecting => Service != null && Service.isConnecting;
    public string manualPort => Service?.manualPort;
    public string status => Service?.status;
    public int port => Service?.port ?? 0;
    public float broadcastInterval => Service?.broadcastInterval ?? 5f;
    public float syncInterval => Service?.syncInterval ?? 0.015f; // =========== Mod开发者注意现在是TI版本也就是满血版无同步延迟，0.03 ~33ms ===================

    public Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    public Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;
    public Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;
    public Dictionary<string, PlayerStatus> clientPlayerStatuses => Service?.clientPlayerStatuses;
    public bool ClientLootSetupActive => networkStarted && !IsServer && _clientLootSetupDepth > 0;

    // —— 工具：对外暴露两个只读状态 —— //
    public bool IsClient => networkStarted && !IsServer;

    //全局变量地狱的结束

    private void Awake()
    {
        Debug.Log("ModBehaviour Awake");
        Instance = this;

        // 测试所有日志等级的颜色输出
        LoggerHelper.Log(LogLevel.None, "【测试】None 等级日志 - 无颜色");
        LoggerHelper.LogInfo("【测试】Info 等级日志 - 信息颜色");
        LoggerHelper.LogWarning("【测试】Warning 等级日志 - 警告颜色");
        LoggerHelper.LogError("【测试】Error 等级日志 - 错误颜色");
        LoggerHelper.LogFatal("【测试】Fatal 等级日志 - 致命错误颜色");

        // 初始化玩家信息数据库
        InitializePlayerDatabase();
    }

    /// <summary>
    /// 初始化玩家信息数据库
    /// </summary>
    private void InitializePlayerDatabase()
    {
        try
        {
            var playerDb = Utils.Database.PlayerInfoDatabase.Instance;

            // 获取当前玩家的 Steam 信息
            if (Steamworks.SteamAPI.IsSteamRunning())
            {
                var steamId = Steamworks.SteamUser.GetSteamID().ToString();
                var playerName = Steamworks.SteamFriends.GetPersonaName();

                // 获取头像 URL（大头像）
                var avatarHandle = Steamworks.SteamFriends.GetLargeFriendAvatar(Steamworks.SteamUser.GetSteamID());
                string avatarUrl = null;

                if (avatarHandle > 0)
                {
                    // Steam 头像 URL 格式
                    avatarUrl = $"https://steamcdn-a.akamaihd.net/steamcommunity/public/images/avatars/{avatarHandle:x2}/{avatarHandle:x16}_full.jpg";
                }

                // 添加到数据库
                playerDb.AddOrUpdatePlayer(
                    steamId: steamId,
                    playerName: playerName,
                    avatarUrl: avatarUrl,
                    isLocal: true,
                    lastUpdate: DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                );

                Debug.Log($"[PlayerDB] 本地玩家信息已缓存:");
                Debug.Log($"  SteamID: {steamId}");
                Debug.Log($"  名字: {playerName}");
                Debug.Log($"  头像URL: {avatarUrl ?? "未获取"}");

                // 导出 JSON 到日志（调试用）
                var json = playerDb.ExportToJsonWithStats(indented: true);
                Debug.Log($"[PlayerDB] 玩家数据库:\n{json}");
            }
            else
            {
                Debug.LogWarning("[PlayerDB] Steam 未初始化，无法获取玩家信息");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PlayerDB] 初始化玩家数据库失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void Update()
    {
        if (CharacterMainControl.Main != null && !isinit)
        {
            isinit = true;
            Traverse
                .Create(CharacterMainControl.Main.EquipmentController)
                .Field<Slot>("armorSlot")
                .Value.onSlotContentChanged += LocalPlayerManager
                .Instance
                .ModBehaviour_onSlotContentChanged;
            Traverse
                .Create(CharacterMainControl.Main.EquipmentController)
                .Field<Slot>("helmatSlot")
                .Value.onSlotContentChanged += LocalPlayerManager
                .Instance
                .ModBehaviour_onSlotContentChanged;
            Traverse
                .Create(CharacterMainControl.Main.EquipmentController)
                .Field<Slot>("faceMaskSlot")
                .Value.onSlotContentChanged += LocalPlayerManager
                .Instance
                .ModBehaviour_onSlotContentChanged;
            Traverse
                .Create(CharacterMainControl.Main.EquipmentController)
                .Field<Slot>("backpackSlot")
                .Value.onSlotContentChanged += LocalPlayerManager
                .Instance
                .ModBehaviour_onSlotContentChanged;
            Traverse
                .Create(CharacterMainControl.Main.EquipmentController)
                .Field<Slot>("headsetSlot")
                .Value.onSlotContentChanged += LocalPlayerManager
                .Instance
                .ModBehaviour_onSlotContentChanged;

            CharacterMainControl.Main.OnHoldAgentChanged += LocalPlayerManager
                .Instance
                .Main_OnHoldAgentChanged;
        }

        //暂停显示出鼠标
        if (Pausebool)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        if (CharacterMainControl.Main == null)
            isinit = false;

        //if (ModUI.Instance != null && Input.GetKeyDown(KeyCode.Home)) ModUI.Instance.showUI = !ModUI.Instance.showUI;

        if (networkStarted)
        {
            netManager.PollEvents();

            // 🕐 主机端：检查玩家加入超时
            if (IsServer)
            {
                Service.CheckJoinTimeouts();
            }

            SceneNet.Instance.TrySendSceneReadyOnce();
            if (!isinit2)
            {
                isinit2 = true;
                if (!IsServer)
                    HealthM.Instance.Client_ReportSelfHealth_IfReadyOnce();
            }

            // if (IsServer) Server_EnsureAllHealthHooks();

            if (!IsServer && !isConnecting)
            {
                broadcastTimer += Time.deltaTime;
                if (broadcastTimer >= broadcastInterval)
                {
                    CoopTool.SendBroadcastDiscovery();
                    broadcastTimer = 0f;
                }
            }

            syncTimer += Time.deltaTime;
            if (syncTimer >= syncInterval)
            {
                SendLocalPlayerStatus.Instance.SendPositionUpdate();
                SendLocalPlayerStatus.Instance.SendAnimationStatus();
                syncTimer = 0f;

                //if (!IsServer)
                //{
                //    if (MultiSceneCore.Instance != null && MultiSceneCore.MainSceneID != "Base")
                //    {
                //        if (LevelManager.Instance.MainCharacter != null && LevelManager.Instance.MainCharacter.Health.MaxHealth > 0f)
                //        {
                //            // Debug.Log(LevelManager.Instance.MainCharacter.Health.CurrentHealth);
                //            if (LevelManager.Instance.MainCharacter.Health.CurrentHealth <= 0f && Client_IsSpawnProtected())
                //            {
                //                // Debug.Log(LevelManager.Instance.MainCharacter.Health.CurrentHealth);
                //                Client_EnsureSelfDeathEvent(LevelManager.Instance.MainCharacter.Health, LevelManager.Instance.MainCharacter);
                //            }
                //        }
                //    }
                //}
            }

            if (
                !IsServer
                && !string.IsNullOrEmpty(SceneNet.Instance._sceneReadySidSent)
                && _envReqSid != SceneNet.Instance._sceneReadySidSent
            )
            {
                _envReqSid = SceneNet.Instance._sceneReadySidSent; // 本场景只请求一次
                COOPManager.Weather.Client_RequestEnvSync(); // 向主机要时间/天气快照
            }

            if (IsServer)
            {
                _aiNameIconTimer += Time.deltaTime;
                if (_aiNameIconTimer >= AI_NAMEICON_INTERVAL)
                {
                    _aiNameIconTimer = 0f;

                    foreach (var kv in AITool.aiById)
                    {
                        var id = kv.Key;
                        var cmc = kv.Value;
                        if (!cmc)
                            continue;

                        var pr = cmc.characterPreset;
                        if (!pr)
                            continue;

                        var iconType = 0;
                        var showName = false;
                        try
                        {
                            iconType = (int)FR_IconType(pr);
                            showName = pr.showName;
                            // 运行期可能刚补上了图标，兜底再查一次
                            if (iconType == 0 && pr.GetCharacterIcon() != null)
                                iconType = (int)FR_IconType(pr);
                        }
                        catch { }

                        // 只给“有图标 or 需要显示名字”的 AI 发
                        if (iconType != 0 || showName)
                            AIName.Server_BroadcastAiNameIcon(id, cmc);
                    }
                }
            }

            // 主机：周期广播环境快照（不重）
            if (IsServer)
            {
                _envSyncTimer += Time.deltaTime;
                if (_envSyncTimer >= ENV_SYNC_INTERVAL)
                {
                    _envSyncTimer = 0f;
                    COOPManager.Weather.Server_BroadcastEnvSync();
                }

                _aiAnimTimer += Time.deltaTime;
                if (_aiAnimTimer >= AI_ANIM_INTERVAL)
                {
                    _aiAnimTimer = 0f;
                    COOPManager.AIHandle.Server_BroadcastAiAnimations();
                }
            }

            var burst = 64; // 每帧最多处理这么多条，稳扎稳打
            while (AITool._aiSceneReady && _pendingAiTrans.Count > 0 && burst-- > 0)
            {
                var (id, p, f) = _pendingAiTrans.Dequeue();
                AITool.ApplyAiTransform(id, p, f);
            }

            if (NetService.Instance.netManager != null)
            {
                if (!SteamP2PLoader.Instance._isOptimized && SteamP2PLoader.Instance.UseSteamP2P)
                {
                    NetService.Instance.netManager.UpdateTime = 1;
                    SteamP2PLoader.Instance._isOptimized = true;
                    Debug.Log("[SteamP2P扩展] ✓ LiteNetLib网络线程已优化 (1ms 更新周期)");
                }
            }
        }

        if (networkStarted && IsServer)
        {
            _aiTfTimer += Time.deltaTime;
            if (_aiTfTimer >= AI_TF_INTERVAL)
            {
                _aiTfTimer = 0f;
                COOPManager.AIHandle.Server_BroadcastAiTransforms();
            }
        }

        LocalPlayerManager.Instance.UpdatePlayerStatuses();
        LocalPlayerManager.Instance.UpdateRemoteCharacters();

        //if (Input.GetKeyDown(ModUI.Instance.toggleWindowKey)) ModUI.Instance.showPlayerStatusWindow = !ModUI.Instance.showPlayerStatusWindow;

        COOPManager.GrenadeM.ProcessPendingGrenades();

        if (!IsServer)
            if (CoopTool._cliSelfHpPending && CharacterMainControl.Main != null)
            {
                HealthM.Instance.ApplyHealthAndEnsureBar(
                    CharacterMainControl.Main.gameObject,
                    CoopTool._cliSelfHpMax,
                    CoopTool._cliSelfHpCur
                );
                CoopTool._cliSelfHpPending = false;
            }

        if (IsServer)
            HealthM.Instance.Server_EnsureAllHealthHooks();
        if (!IsServer)
            CoopTool.Client_ApplyPendingSelfIfReady();
        if (!IsServer)
            HealthM.Instance.Client_ReportSelfHealth_IfReadyOnce();

        // 投票期间按 J 切换准备
        if (SceneNet.Instance.sceneVoteActive && Input.GetKeyDown(readyKey))
        {
            SceneNet.Instance.localReady = !SceneNet.Instance.localReady;
            if (IsServer)
            {
                // 主机使用新的 JSON 投票系统
                var myId = Service.GetPlayerId(null);
                SceneVoteMessage.Host_HandleReadyToggle(myId, SceneNet.Instance.localReady);
            }
            else
            {
                // 客户端使用新的 JSON 投票系统
                SceneVoteMessage.Client_ToggleReady(SceneNet.Instance.localReady);
            }
        }

        // 主机：定期广播投票状态
        if (IsServer)
        {
            SceneVoteMessage.Host_Update();
        }

        if (networkStarted)
        {
            SceneNet.Instance.TrySendSceneReadyOnce();
            if (_envReqSid != SceneNet.Instance._sceneReadySidSent)
            {
                _envReqSid = SceneNet.Instance._sceneReadySidSent;
                COOPManager.Weather.Client_RequestEnvSync();
            }

            // 主机：每帧确保给所有 Health 打钩（含新生成/换图后新克隆）
            if (IsServer)
                HealthM.Instance.Server_EnsureAllHealthHooks();

            // 客户端：本场景里若还没成功上报，就每帧重试直到成功
            if (!IsServer && !HealthTool._cliInitHpReported)
                HealthM.Instance.Client_ReportSelfHealth_IfReadyOnce();

            // 客户端：给自己的 Health 持续打钩，变化就上报
            if (!IsServer)
                HealthTool.Client_HookSelfHealth();
        }

        if (Spectator.Instance._spectatorActive)
        {
            ClosureView.Instance.gameObject.SetActive(false);
            // 动态剔除“已死/被销毁/不在本地图”的目标
            Spectator.Instance._spectateList = Spectator
                .Instance._spectateList.Where(c =>
                {
                    if (!LocalPlayerManager.Instance.IsAlive(c))
                        return false;

                    var mySceneId = localPlayerStatus != null ? localPlayerStatus.SceneId : null;
                    if (string.IsNullOrEmpty(mySceneId))
                        LocalPlayerManager.Instance.ComputeIsInGame(out mySceneId);

                    // 反查该 CMC 对应的 peer 的 SceneId
                    string peerScene = null;
                    if (IsServer)
                    {
                        foreach (var kv in remoteCharacters)
                            if (
                                kv.Value != null
                                && kv.Value.GetComponent<CharacterMainControl>() == c
                            )
                            {
                                if (
                                    !SceneM._srvPeerScene.TryGetValue(kv.Key, out peerScene)
                                    && playerStatuses.TryGetValue(kv.Key, out var st)
                                )
                                    peerScene = st?.SceneId;
                                break;
                            }
                    }
                    else
                    {
                        foreach (var kv in clientRemoteCharacters)
                            if (
                                kv.Value != null
                                && kv.Value.GetComponent<CharacterMainControl>() == c
                            )
                            {
                                if (clientPlayerStatuses.TryGetValue(kv.Key, out var st))
                                    peerScene = st?.SceneId;
                                break;
                            }
                    }

                    return Spectator.AreSameMap(mySceneId, peerScene);
                })
                .ToList();

            // 全员阵亡 → 退出观战并弹出结算
            if (Spectator.Instance._spectateList.Count == 0 || SceneM.AllPlayersDead())
            {
                Spectator.Instance.EndSpectatorAndShowClosure();
                return;
            }

            if (_spectateIdx < 0 || _spectateIdx >= Spectator.Instance._spectateList.Count)
                _spectateIdx = 0;

            // 当前目标若死亡，自动跳到下一个
            if (
                !LocalPlayerManager.Instance.IsAlive(Spectator.Instance._spectateList[_spectateIdx])
            )
                Spectator.Instance.SpectateNext();

            // 鼠标左/右键切换（加个轻微节流）
            if (Time.unscaledTime >= _spectateNextSwitchTime)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    Spectator.Instance.SpectateNext();
                    _spectateNextSwitchTime = Time.unscaledTime + 0.15f;
                }

                if (Input.GetMouseButtonDown(1))
                {
                    Spectator.Instance.SpectatePrev();
                    _spectateNextSwitchTime = Time.unscaledTime + 0.15f;
                }
            }
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded_IndexDestructibles;
        SceneManager.sceneUnloaded += OnSceneUnloaded_Cleanup; // ✅ 新增：场景卸载清理
        LevelManager.OnAfterLevelInitialized += LevelManager_OnAfterLevelInitialized;
        LevelManager.OnLevelInitialized += OnLevelInitialized_IndexDestructibles;

        SceneManager.sceneLoaded += SceneManager_sceneLoaded;
        LevelManager.OnLevelInitialized += LevelManager_OnLevelInitialized;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded_IndexDestructibles;
        SceneManager.sceneUnloaded -= OnSceneUnloaded_Cleanup; // ✅ 新增：场景卸载清理
        LevelManager.OnLevelInitialized -= OnLevelInitialized_IndexDestructibles;
        //   LevelManager.OnAfterLevelInitialized -= _OnAfterLevelInitialized_ServerGate;

        SceneManager.sceneLoaded -= SceneManager_sceneLoaded;
        LevelManager.OnLevelInitialized -= LevelManager_OnLevelInitialized;
    }

    private void OnDestroy()
    {
        NetService.Instance.StopNetwork();
    }

    /// <summary>
    /// ✅ 场景卸载时清理所有数据结构
    /// 修复：主机在大型地图撤离时崩溃（数据结构未清理导致旧场景对象引用残留）
    /// </summary>
    private void OnSceneUnloaded_Cleanup(Scene scene)
    {
        Debug.Log($"[MOD] ========== 场景卸载清理开始: {scene.name} ==========");

        try
        {
            // 1. 清理 AI 数据
            Debug.Log("[MOD-Cleanup] 清理 AI 数据...");
            AITool.ResetAiSerials();
            if (COOPManager.AIHandle != null)
            {
                COOPManager.AIHandle.ClearAiLoadoutTracking();
            }

            // 2. 清理战利品缓存
            Debug.Log("[MOD-Cleanup] 清理战利品数据...");
            if (LootManager.Instance != null)
            {
                LootManager.Instance.ClearCaches();
                LootManager.Instance._srvLootByUid.Clear();
                LootManager.Instance._pendingLootStatesByUid.Clear();
                LootManager.Instance._srvLootMuteUntil.Clear();
                LootManager.Instance._cliLootByUid.Clear();
                LootManager.Instance._cliPendingTake.Clear();
            }

            // ✅ 清理 Inventory 缓存（墓碑/有效箱子）
            LootboxDetectUtil.ClearInventoryCaches();

            // 3. 清理掉落物品
            Debug.Log("[MOD-Cleanup] 清理掉落物品...");
            ItemTool.serverDroppedItems.Clear();
            ItemTool.clientDroppedItems.Clear();

            // 4. 清理游戏对象缓存
            Debug.Log("[MOD-Cleanup] 清理游戏对象缓存...");
            if (Utils.GameObjectCacheManager.Instance != null)
            {
                Utils.GameObjectCacheManager.Instance.ClearAllCaches();
            }

            // 5. 清空异步消息队列
            Debug.Log("[MOD-Cleanup] 清空异步消息队列...");
            if (Utils.AsyncMessageQueue.Instance != null)
            {
                Utils.AsyncMessageQueue.Instance.ClearQueue();
                Utils.AsyncMessageQueue.Instance.DisableBulkMode();
            }

            // 6. 清理可破坏物
            Debug.Log("[MOD-Cleanup] 清理可破坏物数据...");
            if (COOPManager.destructible != null)
            {
                COOPManager.destructible.ClearDestructibles();
            }

            // 7. 清理远程玩家角色（如果离开游戏场景）
            if (!string.IsNullOrEmpty(scene.name) && scene.name != "MainMenu" && scene.name != "LoadingScreen")
            {
                Debug.Log("[MOD-Cleanup] 清理远程玩家数据...");
                if (IsServer && remoteCharacters != null)
                {
                    foreach (var kv in remoteCharacters.ToList())
                    {
                        if (kv.Value != null)
                        {
                            try { Object.Destroy(kv.Value); } catch { }
                        }
                    }
                    remoteCharacters.Clear();
                }

                if (!IsServer && clientRemoteCharacters != null)
                {
                    foreach (var kv in clientRemoteCharacters.ToList())
                    {
                        if (kv.Value != null)
                        {
                            try { Object.Destroy(kv.Value); } catch { }
                        }
                    }
                    clientRemoteCharacters.Clear();
                }
            }

            // 8. 强制关闭同步UI（确保UI一定关闭）
            Debug.Log("[MOD-Cleanup] 强制关闭同步UI...");
            if (WaitingSynchronizationUI.Instance != null)
            {
                WaitingSynchronizationUI.Instance.ForceCloseIfVisible("场景卸载");
            }

            // 9. 强制垃圾回收（可选，仅在大型地图卸载时）
            if (scene.buildIndex > 0) // 非主菜单场景
            {
                Debug.Log("[MOD-Cleanup] 触发垃圾回收...");
                System.GC.Collect();
            }

            Debug.Log($"[MOD] ========== 场景卸载清理完成: {scene.name} ==========");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MOD] 场景卸载清理失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void LevelManager_OnAfterLevelInitialized()
    {
        if (IsServer && networkStarted)
            SceneNet.Instance.Server_SceneGateAsync().Forget();
    }

    private void LevelManager_OnLevelInitialized()
    {
        // 【优化】立即显示同步等待UI（在场景初始化开始时）
        Debug.Log("[MOD] LevelManager_OnLevelInitialized 开始，准备显示同步UI");
        var syncUI = WaitingSynchronizationUI.Instance;
        if (syncUI != null)
        {
            Debug.Log("[MOD] 找到同步UI实例，开始显示");
            syncUI.Show();
            syncUI.UpdatePlayerList();
        }
        else
        {
            Debug.LogWarning("[MOD] 同步UI实例为null！");
        }

        // 【优化】立即执行的关键任务
        AITool.ResetAiSerials();

        // ✅ 刷新游戏对象缓存管理器
        if (GameObjectCacheManager.Instance != null)
        {
            GameObjectCacheManager.Instance.RefreshAllCaches();
        }

        // ✅ 清理战利品管理器缓存
        if (LootManager.Instance != null)
        {
            LootManager.Instance.ClearCaches();
        }

        // ✅ 重新启用异步消息队列的批量处理模式（刷新计时器）
        // AsyncQueue 默认在创建时就启用批量模式，这里重新启用以刷新持续时间
        if (Utils.AsyncMessageQueue.Instance != null && !IsServer)
        {
            Utils.AsyncMessageQueue.Instance.EnableBulkMode();
        }

        // ✅ 客户端重置AI装备同步追踪
        if (!IsServer)
        {
            COOPManager.AIHandle.Client_ResetAiLoadoutTracking();
        }

        // ✅ 重置场景门控状态，为下一次场景切换做准备
        // ⚠️ 注意：不要在这里清空 _srvGateReadyPids！
        // ⚠️ 因为此方法在场景加载早期触发，而放行在 OnAfterLevelInitialized 中进行
        // ⚠️ 如果在这里清空，客户端的举手记录会在放行前丢失
        if (IsServer)
        {
            SceneNet.Instance._srvSceneGateOpen = false;
            // ❌ 不要在这里清空：SceneNet.Instance._srvGateReadyPids.Clear();
        }
        else
        {
            SceneNet.Instance._cliSceneGateReleased = false;
        }

        // 【优化】注册同步任务（UI已经在场景初始化时显示）

        if (syncUI != null)
        {
            Debug.Log("[MOD] 注册同步任务");
            // 注册同步任务
            if (!IsServer)
            {
                syncUI.RegisterTask("weather", "环境同步");
                syncUI.RegisterTask("player_health", "玩家状态同步");
                syncUI.RegisterTask("ai_loadouts", "AI装备接收"); // ✅ 客户端也需要追踪AI装备接收进度
            }

            if (IsServer)
            {
                syncUI.RegisterTask("ai_seeds", "AI种子同步");
                syncUI.RegisterTask("ai_loadouts", "AI装备同步");
                syncUI.RegisterTask("destructible", "可破坏物扫描");
            }

            syncUI.RegisterTask("ai_names", "AI名称初始化");
        }

        // 【优化】立即执行玩家相关任务（P0优先级）
        if (!IsServer)
        {
            HealthM.Instance.Client_ReportSelfHealth_IfReadyOnce();
            if (syncUI != null)
                syncUI.CompleteTask("player_health");
        }

        SceneNet.Instance.TrySendSceneReadyOnce();

        // 【优化】使用场景初始化管理器分批延迟执行任务，避免卡顿
        var initManager = SceneInitManager.Instance;
        if (initManager != null)
        {
            // P1：环境同步（延迟1秒，等待场景完全加载）
            initManager.EnqueueDelayedTask(
                () =>
                {
                    if (!IsServer)
                    {
                        COOPManager.Weather.Client_RequestEnvSync();
                        var ui = WaitingSynchronizationUI.Instance;
                        if (ui != null)
                            ui.CompleteTask("weather", "完成");
                    }
                },
                1.0f,
                "Weather_EnvSync"
            );

            // P1：AI种子同步（延迟2秒，使用后台线程+协程优化）
            if (IsServer)
            {
                initManager.EnqueueDelayedTask(
                    () =>
                    {
                        // 【优化】直接使用后台线程+协程方案（最佳稳定性和性能平衡）
                        // 性能提升：75-85%，完全不阻塞主线程
                        StartCoroutine(
                            COOPManager.AIHandle.Server_SendAiSeedsBatched(batchSize: 5)
                        );

                        var ui = WaitingSynchronizationUI.Instance;
                        if (ui != null)
                            ui.UpdateTaskStatus("ai_seeds", false, "计算中...");
                    },
                    2.0f,
                    "AI_Seeds"
                );
            }

            // P2：AI装备同步（延迟3.5秒，批量发送避免卡顿）
            if (IsServer)
            {
                initManager.EnqueueDelayedTask(
                    () =>
                    {
                        // 【优化】分批发送AI装备，每批2个，避免网络拥堵和卡顿
                        // 进一步降低批量大小，防止在 Spawning bodies 阶段造成卡顿
                        StartCoroutine(
                            COOPManager.AIHandle.Server_SendAiLoadoutsBatched(batchSize: 2)
                        );

                        var ui = WaitingSynchronizationUI.Instance;
                        if (ui != null)
                            ui.UpdateTaskStatus("ai_loadouts", false, "发送中...");
                    },
                    3.5f,
                    "AI_Loadouts"
                );
            }

            // P2：AI名称重置（延迟3秒）
            initManager.EnqueueDelayedTask(
                () =>
                {
                    AIName.Client_ResetNameIconSeal_OnLevelInit();

                    var ui = WaitingSynchronizationUI.Instance;
                    if (ui != null)
                        ui.CompleteTask("ai_names", "完成");
                },
                3.0f,
                "AI_Names"
            );
        }
        else
        {
            // 降级：如果管理器不可用，使用原始逻辑
            if (!IsServer)
                COOPManager.Weather.Client_RequestEnvSync();
            if (IsServer)
                COOPManager.AIHandle.Server_SendAiSeeds();
            AIName.Client_ResetNameIconSeal_OnLevelInit();
        }
    }

    //arg!!!!!!!!!!!
    private void SceneManager_sceneLoaded(Scene arg0, LoadSceneMode arg1)
    {
        SceneNet.Instance.TrySendSceneReadyOnce();
        if (!IsServer)
            COOPManager.Weather.Client_RequestEnvSync();
    }

    public void OnNetworkReceive(
        NetPeer peer,
        NetPacketReader reader,
        byte channelNumber,
        DeliveryMethod deliveryMethod
    )
    {
        // 统一：读取 1 字节的操作码（Op）
        if (reader.AvailableBytes <= 0)
        {
            reader.Recycle();
            return;
        }

        var op = (Op)reader.GetByte();
        //  Debug.Log($"[RECV OP] {(byte)op}, avail={reader.AvailableBytes}");

        switch (op)
        {
            // ===== 主机 -> 客户端：下发全量玩家状态 =====
            case Op.PLAYER_STATUS_UPDATE:
                if (!IsServer)
                {
                    var playerCount = reader.GetInt();
                    clientPlayerStatuses.Clear();

                    for (var i = 0; i < playerCount; i++)
                    {
                        var endPoint = reader.GetString();
                        var playerName = reader.GetString();
                        var latency = reader.GetInt();
                        var isInGame = reader.GetBool();
                        var position = reader.GetVector3();
                        var rotation = reader.GetQuaternion();

                        var sceneId = reader.GetString();
                        // ✅ 不再读取 faceJson，通过 PLAYER_APPEARANCE 包接收

                        var equipmentCount = reader.GetInt();
                        var equipmentList = new List<EquipmentSyncData>();
                        for (var j = 0; j < equipmentCount; j++)
                            equipmentList.Add(EquipmentSyncData.Deserialize(reader));

                        var weaponCount = reader.GetInt();
                        var weaponList = new List<WeaponSyncData>();
                        for (var j = 0; j < weaponCount; j++)
                            weaponList.Add(WeaponSyncData.Deserialize(reader));

                        // 如果是自己的状态，更新本地玩家的延迟值后继续
                        if (NetService.Instance.IsSelfId(endPoint))
                        {
                            if (localPlayerStatus != null)
                            {
                                localPlayerStatus.Latency = latency; // 更新客户端到主机的延迟
                            }
                            continue;
                        }

                        if (!clientPlayerStatuses.TryGetValue(endPoint, out var st))
                            st = clientPlayerStatuses[endPoint] = new PlayerStatus();

                        st.EndPoint = endPoint;
                        st.PlayerName = playerName;
                        st.Latency = latency;
                        st.IsInGame = isInGame;
                        st.LastIsInGame = isInGame;
                        st.Position = position;
                        st.Rotation = rotation;
                        // ✅ CustomFaceJson 通过 PLAYER_APPEARANCE 包单独接收
                        st.EquipmentList = equipmentList;
                        st.WeaponList = weaponList;

                        if (!string.IsNullOrEmpty(sceneId))
                        {
                            st.SceneId = sceneId;
                            SceneNet.Instance._cliLastSceneIdByPlayer[endPoint] = sceneId; // 给 A 的兜底也喂一份
                        }

                        if (
                            clientRemoteCharacters.TryGetValue(st.EndPoint, out var existing)
                            && existing != null
                        )
                            CustomFace.Client_ApplyFaceIfAvailable(
                                st.EndPoint,
                                existing,
                                st.CustomFaceJson
                            );

                        if (isInGame)
                        {
                            if (
                                !clientRemoteCharacters.ContainsKey(endPoint)
                                || clientRemoteCharacters[endPoint] == null
                            )
                            {
                                // ✅ 使用缓存或状态中的外观数据
                                var faceJson = st.CustomFaceJson ?? string.Empty;
                                CreateRemoteCharacter
                                    .CreateRemoteCharacterForClient(
                                        endPoint,
                                        position,
                                        rotation,
                                        faceJson
                                    )
                                    .Forget();
                            }
                            else
                            {
                                var go = clientRemoteCharacters[endPoint];
                                var ni = NetInterpUtil.Attach(go);
                                ni?.Push(st.Position, st.Rotation);
                            }

                            foreach (var e in equipmentList)
                                COOPManager
                                    .ClientPlayer_Apply.ApplyEquipmentUpdate_Client(
                                        endPoint,
                                        e.SlotHash,
                                        e.ItemId
                                    )
                                    .Forget();
                            foreach (var w in weaponList)
                                COOPManager
                                    .ClientPlayer_Apply.ApplyWeaponUpdate_Client(
                                        endPoint,
                                        w.SlotHash,
                                        w.ItemId
                                    )
                                    .Forget();
                        }
                    }
                }

                break;

            // ===== 客户端 -> 主机：上报自身状态 =====
            case Op.CLIENT_STATUS_UPDATE:
                if (IsServer)
                    COOPManager.ClientHandle.HandleClientStatusUpdate(peer, reader);
                break;

            // ===== 位置信息（量化版本）=====
            case Op.POSITION_UPDATE:
                if (IsServer)
                {
                    var endPointC = reader.GetString();
                    var posS = reader.GetV3cm(); // ← 原来是 GetVector3()
                    var dirS = reader.GetDir();
                    var rotS = Quaternion.LookRotation(dirS, Vector3.up);

                    COOPManager.PublicHandleUpdate.HandlePositionUpdate_Q(
                        peer,
                        endPointC,
                        posS,
                        rotS
                    );
                }
                else
                {
                    var endPointS = reader.GetString();
                    var posS = reader.GetV3cm(); // ← 原来是 GetVector3()
                    var dirS = reader.GetDir();
                    var rotS = Quaternion.LookRotation(dirS, Vector3.up);

                    if (NetService.Instance.IsSelfId(endPointS))
                        break;

                    // 防御性：若包损坏，不推进插值也不拉起角色
                    if (
                        float.IsNaN(posS.x)
                        || float.IsNaN(posS.y)
                        || float.IsNaN(posS.z)
                        || float.IsInfinity(posS.x)
                        || float.IsInfinity(posS.y)
                        || float.IsInfinity(posS.z)
                    )
                        break;

                    if (!clientPlayerStatuses.TryGetValue(endPointS, out var st))
                        st = clientPlayerStatuses[endPointS] = new PlayerStatus
                        {
                            EndPoint = endPointS,
                            IsInGame = true,
                        };

                    st.Position = posS;
                    st.Rotation = rotS;

                    if (clientRemoteCharacters.TryGetValue(endPointS, out var go) && go != null)
                    {
                        var ni = NetInterpUtil.Attach(go);
                        ni?.Push(st.Position, st.Rotation); // 原有：位置与根旋转插值

                        var cmc = go.GetComponentInChildren<CharacterMainControl>(true);
                        if (cmc && cmc.modelRoot)
                        {
                            var e = st.Rotation.eulerAngles;
                            cmc.modelRoot.transform.rotation = Quaternion.Euler(0f, e.y, 0f);
                        }
                    }
                    else
                    {
                        CreateRemoteCharacter
                            .CreateRemoteCharacterForClient(
                                endPointS,
                                posS,
                                rotS,
                                st.CustomFaceJson
                            )
                            .Forget();
                    }
                }

                break;

            //动画
            case Op.ANIM_SYNC:
                if (IsServer)
                {
                    // 保持客户端 -> 主机
                    COOPManager.PublicHandleUpdate.HandleClientAnimationStatus(peer, reader);
                }
                else
                {
                    // 保持主机 -> 客户端（playerId）
                    var playerId = reader.GetString();
                    if (NetService.Instance.IsSelfId(playerId))
                        break;

                    var moveSpeed = reader.GetFloat();
                    var moveDirX = reader.GetFloat();
                    var moveDirY = reader.GetFloat();
                    var isDashing = reader.GetBool();
                    var isAttacking = reader.GetBool();
                    var handState = reader.GetInt();
                    var gunReady = reader.GetBool();
                    var stateHash = reader.GetInt();
                    var normTime = reader.GetFloat();

                    if (clientRemoteCharacters.TryGetValue(playerId, out var obj) && obj != null)
                    {
                        var ai = AnimInterpUtil.Attach(obj);
                        ai?.Push(
                            new AnimSample
                            {
                                speed = moveSpeed,
                                dirX = moveDirX,
                                dirY = moveDirY,
                                dashing = isDashing,
                                attack = isAttacking,
                                hand = handState,
                                gunReady = gunReady,
                                stateHash = stateHash,
                                normTime = normTime,
                            }
                        );
                    }
                }

                break;

            // ===== 装备更新 =====
            case Op.EQUIPMENT_UPDATE:
                if (IsServer)
                {
                    COOPManager.PublicHandleUpdate.HandleEquipmentUpdate(peer, reader);
                }
                else
                {
                    var endPoint = reader.GetString();
                    if (NetService.Instance.IsSelfId(endPoint))
                        break;
                    var slotHash = reader.GetInt();
                    var itemId = reader.GetString();
                    COOPManager
                        .ClientPlayer_Apply.ApplyEquipmentUpdate_Client(endPoint, slotHash, itemId)
                        .Forget();
                }

                break;

            // ===== 武器更新 =====
            case Op.PLAYERWEAPON_UPDATE:
                if (IsServer)
                {
                    COOPManager.PublicHandleUpdate.HandleWeaponUpdate(peer, reader);
                }
                else
                {
                    var endPoint = reader.GetString();
                    if (NetService.Instance.IsSelfId(endPoint))
                        break;
                    var slotHash = reader.GetInt();
                    var itemId = reader.GetString();
                    COOPManager
                        .ClientPlayer_Apply.ApplyWeaponUpdate_Client(endPoint, slotHash, itemId)
                        .Forget();
                }

                break;

            case Op.FIRE_REQUEST:
                if (IsServer)
                    COOPManager.WeaponHandle.HandleFireRequest(peer, reader);
                break;

            case Op.FIRE_EVENT:
                if (!IsServer)
                    //Debug.Log("[RECV FIRE_EVENT] opcode path");
                    COOPManager.WeaponHandle.HandleFireEvent(reader);
                break;

            case Op.JSON:
                // 处理JSON消息 - 使用路由器根据type字段分发
                JsonMessageRouter.HandleJsonMessage(reader, peer);
                break;

            case Op.GRENADE_THROW_REQUEST:
                if (IsServer)
                    COOPManager.GrenadeM.HandleGrenadeThrowRequest(peer, reader);
                break;
            case Op.GRENADE_SPAWN:
                if (!IsServer)
                    COOPManager.GrenadeM.HandleGrenadeSpawn(reader);
                break;
            case Op.GRENADE_EXPLODE:
                if (!IsServer)
                    COOPManager.GrenadeM.HandleGrenadeExplode(reader);
                break;

            //case Op.DISCOVER_REQUEST:
            //    if (IsServer) HandleDiscoverRequest(peer, reader);
            //    break;
            //case Op.DISCOVER_RESPONSE:
            //    if (!IsServer) HandleDiscoverResponse(peer, reader);
            //    break;
            case Op.ITEM_DROP_REQUEST:
                if (IsServer)
                    COOPManager.ItemHandle.HandleItemDropRequest(peer, reader);
                break;

            case Op.ITEM_SPAWN:
                if (!IsServer)
                    COOPManager.ItemHandle.HandleItemSpawn(reader);
                break;
            case Op.ITEM_PICKUP_REQUEST:
                if (IsServer)
                    COOPManager.ItemHandle.HandleItemPickupRequest(peer, reader);
                break;
            case Op.ITEM_DESPAWN:
                if (!IsServer)
                    COOPManager.ItemHandle.HandleItemDespawn(reader);
                break;

            case Op.MELEE_ATTACK_REQUEST:
                if (IsServer)
                    COOPManager.WeaponHandle.HandleMeleeAttackRequest(peer, reader);
                break;
            case Op.MELEE_ATTACK_SWING:
                {
                    if (!IsServer)
                    {
                        var shooter = reader.GetString();
                        var delay = reader.GetFloat();

                        //先找玩家远端
                        if (
                            !NetService.Instance.IsSelfId(shooter)
                            && clientRemoteCharacters.TryGetValue(shooter, out var who)
                            && who
                        )
                        {
                            var anim = who.GetComponentInChildren<CharacterAnimationControl_MagicBlend>(
                                true
                            );
                            if (anim != null)
                                anim.OnAttack();

                            var cmc = who.GetComponent<CharacterMainControl>();
                            var model = cmc ? cmc.characterModel : null;
                            if (model)
                                MeleeFx.SpawnSlashFx(model);
                        }
                        //兼容 AI:xxx
                        else if (shooter.StartsWith("AI:"))
                        {
                            if (
                                int.TryParse(shooter.Substring(3), out var aiId)
                                && AITool.aiById.TryGetValue(aiId, out var cmc)
                                && cmc
                            )
                            {
                                var anim =
                                    cmc.GetComponentInChildren<CharacterAnimationControl_MagicBlend>(
                                        true
                                    );
                                if (anim != null)
                                    anim.OnAttack();

                                var model = cmc.characterModel;
                                if (model)
                                    MeleeFx.SpawnSlashFx(model);
                            }
                        }
                    }

                    break;
                }

            case Op.MELEE_HIT_REPORT:
                if (IsServer)
                    COOPManager.WeaponHandle.HandleMeleeHitReport(peer, reader);
                break;

            case Op.ENV_HURT_REQUEST:
                if (IsServer)
                    COOPManager.HurtM.Server_HandleEnvHurtRequest(peer, reader);
                break;
            case Op.ENV_HURT_EVENT:
                if (!IsServer)
                    COOPManager.destructible.Client_ApplyDestructibleHurt(reader);
                break;
            case Op.ENV_DEAD_EVENT:
                if (!IsServer)
                    COOPManager.destructible.Client_ApplyDestructibleDead(reader);
                break;

            case Op.PLAYER_HEALTH_REPORT:
                {
                    if (IsServer)
                    {
                        var max = reader.GetFloat();
                        var cur = reader.GetFloat();

                        var playerId = Service.GetPlayerId(peer);

                        // 🔍 JSON日志：主机收到血量上报
                        var logData = new Dictionary<string, object>
                        {
                            ["event"] = "Server_ReceiveHealthReport",
                            ["playerId"] = playerId,
                            ["maxHealth"] = max,
                            ["currentHealth"] = cur,
                            ["hasRemoteCharacter"] =
                                remoteCharacters != null && remoteCharacters.ContainsKey(peer),
                            ["time"] = Time.time,
                        };
                        // Debug.Log(
                        //     $"[HP_RECEIVE] {Newtonsoft.Json.JsonConvert.SerializeObject(logData)}"
                        // );

                        if (max <= 0f)
                        {
                            Debug.LogWarning(
                                $"[HP_RECEIVE] ⚠️ 收到无效血量，缓存: 玩家={playerId}, max={max}, cur={cur}"
                            );
                            HealthTool._srvPendingHp[peer] = (max, cur);
                            break;
                        }

                        if (
                            remoteCharacters != null
                            && remoteCharacters.TryGetValue(peer, out var go)
                            && go
                        )
                        {
                            // Debug.Log($"[HP_RECEIVE] ✓ 应用血量到远程角色: 玩家={playerId}");
                            // 主机本地先写实自己能立刻看到
                            HealthM.Instance.ApplyHealthAndEnsureBar(go, max, cur);

                            // 再用统一广播流程，发给本人 + 其他客户端
                            var h = go.GetComponentInChildren<Health>(true);
                            if (h)
                                HealthM.Instance.Server_OnHealthChanged(peer, h);
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"[HP_RECEIVE] ⚠️ 远程角色未创建，缓存血量: 玩家={playerId}"
                            );
                            //远端克隆还没创建缓存起来，等钩到 Health 后应用
                            HealthTool._srvPendingHp[peer] = (max, cur);
                        }
                    }

                    break;
                }

            case Op.AUTH_HEALTH_SELF:
                {
                    var max = reader.GetFloat();
                    var cur = reader.GetFloat();

                    if (max <= 0f)
                    {
                        CoopTool._cliSelfHpMax = max;
                        CoopTool._cliSelfHpCur = cur;
                        CoopTool._cliSelfHpPending = true;
                        break;
                    }

                    // --- 防回弹：受击窗口内不接受“比本地更高”的回显 ---
                    var shouldApply = true;
                    try
                    {
                        var main = CharacterMainControl.Main;
                        var selfH = main ? main.Health : null;
                        if (selfH)
                        {
                            var localCur = selfH.CurrentHealth;
                            // 仅在“刚受击的短时间窗”里做保护；平时允许正常回显（例如治疗）
                            if (Time.time - HealthTool._cliLastSelfHurtAt <= SELF_ACCEPT_WINDOW)
                                // 如果回显值会让血量“变多”（典型回弹），判定为陈旧 echo 丢弃
                                if (cur > localCur + 0.0001f)
                                {
                                    Debug.Log(
                                        $"[HP][SelfEcho] drop stale echo in window: local={localCur:F3} srv={cur:F3}"
                                    );

                                    shouldApply = false;
                                }
                        }
                    }
                    catch { }

                    HealthM.Instance._cliApplyingSelfSnap = true;
                    HealthM.Instance._cliEchoMuteUntil = Time.time + SELF_MUTE_SEC;
                    try
                    {
                        if (shouldApply)
                        {
                            if (CoopTool._cliSelfHpPending)
                            {
                                CoopTool._cliSelfHpMax = max;
                                CoopTool._cliSelfHpCur = cur;
                                CoopTool.Client_ApplyPendingSelfIfReady();
                            }
                            else
                            {
                                var main = CharacterMainControl.Main;
                                var go = main ? main.gameObject : null;
                                if (go)
                                {
                                    var h = main.Health;
                                    var cmc = main;
                                    if (h)
                                    {
                                        try
                                        {
                                            h.autoInit = false;
                                        }
                                        catch { }

                                        HealthTool.BindHealthToCharacter(h, cmc);
                                        HealthM.Instance.ForceSetHealth(h, max, cur);
                                    }
                                }

                                CoopTool._cliSelfHpPending = false;
                            }
                        }
                        // 丢弃这帧自回显，不改本地血量
                    }
                    finally
                    {
                        HealthM.Instance._cliApplyingSelfSnap = false;
                    }

                    break;
                }

            case Op.AUTH_HEALTH_REMOTE:
                {
                    if (!IsServer)
                    {
                        var playerId = reader.GetString();
                        var max = reader.GetFloat();
                        var cur = reader.GetFloat();

                        // 无效快照直接挂起，避免把 0/0 覆盖到血条
                        if (max <= 0f)
                        {
                            CoopTool._cliPendingRemoteHp[playerId] = (max, cur);
                            break;
                        }

                        if (
                            clientRemoteCharacters != null
                            && clientRemoteCharacters.TryGetValue(playerId, out var go)
                            && go
                        )
                            HealthM.Instance.ApplyHealthAndEnsureBar(go, max, cur);
                        else
                            CoopTool._cliPendingRemoteHp[playerId] = (max, cur);
                    }

                    break;
                }

            case Op.PLAYER_BUFF_SELF_APPLY:
                if (!IsServer)
                    COOPManager.Buff.HandlePlayerBuffSelfApply(reader);
                break;
            case Op.HOST_BUFF_PROXY_APPLY:
                if (!IsServer)
                    COOPManager.Buff.HandleBuffProxyApply(reader);
                break;

            case Op.SCENE_VOTE_START:
                {
                    if (!IsServer)
                    {
                        SceneNet.Instance.Client_OnSceneVoteStart(reader);
                        // 观战中收到“开始投票”，记一个“投票结束就结算”的意图
                        if (Spectator.Instance._spectatorActive)
                            Spectator.Instance._spectatorEndOnVotePending = true;
                    }

                    break;
                }

            case Op.SCENE_VOTE_REQ:
                {
                    if (IsServer)
                    {
                        var targetId = reader.GetString();
                        var flags = reader.GetByte();
                        bool hasCurtain,
                            useLoc,
                            notifyEvac,
                            saveToFile;
                        PackFlag.UnpackFlags(
                            flags,
                            out hasCurtain,
                            out useLoc,
                            out notifyEvac,
                            out saveToFile
                        );

                        string curtainGuid = null;
                        if (hasCurtain)
                            SceneNet.TryGetString(reader, out curtainGuid);
                        if (!SceneNet.TryGetString(reader, out var locName))
                            locName = string.Empty;

                        // ★ 主机若正处于观战，记下“投票结束就结算”的意图
                        if (Spectator.Instance._spectatorActive)
                            Spectator.Instance._spectatorEndOnVotePending = true;

                        SceneNet.Instance.Host_BeginSceneVote_Simple(
                            targetId,
                            curtainGuid,
                            notifyEvac,
                            saveToFile,
                            useLoc,
                            locName
                        );
                    }

                    break;
                }

            case Op.SCENE_READY_SET:
                {
                    if (IsServer)
                    {
                        var ready = reader.GetBool();
                        SceneNet.Instance.Server_OnSceneReadySet(peer, ready);
                    }
                    else
                    {
                        var pid = reader.GetString();
                        var rdy = reader.GetBool();
                        var localPid = SceneNet.Instance.NormalizeParticipantId(pid);

                        if (
                            !SceneNet.Instance.sceneReady.ContainsKey(localPid)
                            && SceneNet.Instance.sceneParticipantIds.Contains(localPid)
                        )
                            SceneNet.Instance.sceneReady[localPid] = false;

                        if (SceneNet.Instance.sceneReady.ContainsKey(localPid))
                        {
                            SceneNet.Instance.sceneReady[localPid] = rdy;
                            Debug.Log($"[SCENE] READY_SET -> {localPid} (srv='{pid}') = {rdy}");
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"[SCENE] READY_SET for unknown pid '{pid}'. participants=[{string.Join(",", SceneNet.Instance.sceneParticipantIds)}]"
                            );
                        }
                    }

                    break;
                }

            case Op.SCENE_BEGIN_LOAD:
                {
                    if (!IsServer)
                    {
                        // ✅ 关键优化：主机放行开始场景加载时，立即启用异步队列批量处理模式
                        // 这是客户端开始接收大量 LOOT_STATE 消息的起点，必须提前准备好高速处理通道
                        if (Utils.AsyncMessageQueue.Instance != null)
                        {
                            Utils.AsyncMessageQueue.Instance.EnableBulkMode();
                            Debug.Log(
                                "[SCENE_LOAD] ✅ 客户端：启用异步消息队列批量模式，准备接收场景数据"
                            );
                        }

                        // 观战玩家：投票结束时直接弹死亡结算，不参与接下来的本地切图
                        if (
                            Spectator.Instance._spectatorActive
                            && Spectator.Instance._spectatorEndOnVotePending
                        )
                        {
                            Spectator.Instance._spectatorEndOnVotePending = false;
                            SceneNet.Instance.sceneVoteActive = false;
                            SceneNet.Instance.sceneReady.Clear();
                            SceneNet.Instance.localReady = false;

                            Spectator.Instance.EndSpectatorAndShowClosure(); // 直接用你现成的方法弹结算
                            break; // 不再调用 Client_OnBeginSceneLoad(reader)
                        }

                        // 普通玩家照常走
                        SceneNet.Instance.Client_OnBeginSceneLoad(reader);
                    }

                    break;
                }

            case Op.SCENE_CANCEL:
                {
                    // 调用统一的取消投票处理方法（包含触发器重置）
                    if (!IsServer)
                    {
                        SceneNet.Instance.Client_OnVoteCancelled();
                        Debug.Log("[COOP] 收到服务器取消投票通知");
                    }
                    else
                    {
                        // 服务器端直接清除状态（不应该收到这个消息，但保险起见）
                        SceneNet.Instance.sceneVoteActive = false;
                        SceneNet.Instance.sceneReady.Clear();
                        SceneNet.Instance.localReady = false;
                        EscapeFromDuckovCoopMod.Utils.SceneTriggerResetter.ResetAllSceneTriggers();
                    }

                    // 处理观战玩家
                    if (
                        Spectator.Instance._spectatorActive
                        && Spectator.Instance._spectatorEndOnVotePending
                    )
                    {
                        Spectator.Instance._spectatorEndOnVotePending = false;
                        Spectator.Instance.EndSpectatorAndShowClosure();
                    }

                    break;
                }

            case Op.SCENE_READY:
                {
                    var id = reader.GetString(); // 发送者 id（EndPoint）
                    var sid = reader.GetString(); // SceneId（string）
                    var pos = reader.GetVector3(); // 初始位置
                    var rot = reader.GetQuaternion();
                    // ✅ faceJson 已拆分到独立的 PLAYER_APPEARANCE 包，不再从这里读取

                    if (IsServer)
                        SceneNet.Instance.Server_HandleSceneReady(peer, id, sid, pos, rot);
                    // 客户端若收到这条（主机广播），实际创建工作由 REMOTE_CREATE 完成，这里不处理
                    break;
                }

            case Op.PLAYER_APPEARANCE:
                {
                    var playerId = reader.GetString();
                    var faceJson = reader.GetString();

                    // 更新玩家外观数据（主机和客户端都处理）
                    if (IsServer)
                    {
                        // 主机：保存到 playerStatuses 并广播给其他玩家
                        if (peer != null && playerStatuses.TryGetValue(peer, out var status))
                        {
                            status.CustomFaceJson = faceJson;

                            // 转发给其他客户端
                            var w = new NetDataWriter();
                            w.Put((byte)Op.PLAYER_APPEARANCE);
                            w.Put(playerId);
                            w.Put(faceJson);
                            netManager?.SendSmartExcept(w, Op.PLAYER_APPEARANCE, peer);
                        }
                    }
                    else
                    {
                        // 客户端：保存到 clientPlayerStatuses 或缓存
                        if (clientPlayerStatuses.TryGetValue(playerId, out var status))
                        {
                            status.CustomFaceJson = faceJson;
                        }
                        else
                        {
                            // 玩家还未创建，缓存外观数据
                            CustomFace._cliPendingFace[playerId] = faceJson;
                        }

                        // 如果玩家已存在，立即应用外观
                        if (clientRemoteCharacters.TryGetValue(playerId, out var go) && go != null)
                        {
                            CustomFace.Client_ApplyFaceIfAvailable(playerId, go, faceJson);
                        }
                    }
                    break;
                }

            case Op.ENV_SYNC_REQUEST:
                if (IsServer)
                    COOPManager.Weather.Server_BroadcastEnvSync(peer);
                break;

            case Op.ENV_SYNC_STATE:
                {
                    // 客户端应用
                    if (!IsServer)
                    {
                        var day = reader.GetLong();
                        var sec = reader.GetDouble();
                        var scale = reader.GetFloat();
                        var seed = reader.GetInt();
                        var forceW = reader.GetBool();
                        var forceWVal = reader.GetInt();
                        var curWeather = reader.GetInt();
                        var stormLv = reader.GetByte();

                        var lootCount = 0;
                        try
                        {
                            lootCount = reader.GetInt();
                        }
                        catch
                        {
                            lootCount = 0;
                        }

                        var vis = new Dictionary<int, bool>(lootCount);
                        for (var i = 0; i < lootCount; ++i)
                        {
                            var k = 0;
                            var on = false;
                            try
                            {
                                k = reader.GetInt();
                            }
                            catch { }

                            try
                            {
                                on = reader.GetBool();
                            }
                            catch { }

                            vis[k] = on;
                        }

                        Client_ApplyLootVisibility(vis);

                        // 再读门快照（如果主机这次没带就是 0）
                        var doorCount = 0;
                        try
                        {
                            doorCount = reader.GetInt();
                        }
                        catch
                        {
                            doorCount = 0;
                        }

                        for (var i = 0; i < doorCount; ++i)
                        {
                            var dk = 0;
                            var cl = false;
                            try
                            {
                                dk = reader.GetInt();
                            }
                            catch { }

                            try
                            {
                                cl = reader.GetBool();
                            }
                            catch { }

                            COOPManager.Door.Client_ApplyDoorState(dk, cl);
                        }

                        var deadCount = 0;
                        try
                        {
                            deadCount = reader.GetInt();
                        }
                        catch
                        {
                            deadCount = 0;
                        }

                        for (var i = 0; i < deadCount; ++i)
                        {
                            uint did = 0;
                            try
                            {
                                did = reader.GetUInt();
                            }
                            catch { }

                            if (did != 0)
                                COOPManager.destructible.Client_ApplyDestructibleDead_Snapshot(did);
                        }

                        COOPManager.Weather.Client_ApplyEnvSync(
                            day,
                            sec,
                            scale,
                            seed,
                            forceW,
                            forceWVal,
                            curWeather,
                            stormLv
                        );
                    }

                    break;
                }

            case Op.LOOT_REQ_OPEN:
                {
                    if (IsServer)
                        LootManager.Instance.Server_HandleLootOpenRequest(peer, reader);
                    break;
                }

            case Op.LOOT_STATE:
                {
                    if (IsServer)
                        break;

                    // ✅ 优化：将战利品状态消息加入异步队列，避免阻塞主线程
                    if (Utils.AsyncMessageQueue.Instance != null)
                    {
                        Utils.AsyncMessageQueue.Instance.EnqueueMessage(
                            (LiteNetLib.Utils.NetDataReader r) =>
                                COOPManager.LootNet.Client_ApplyLootboxState(r),
                            reader
                        );
                    }
                    else
                    {
                        // 降级：如果异步队列未初始化，直接处理
                        COOPManager.LootNet.Client_ApplyLootboxState(reader);
                    }

                    break;
                }
            case Op.LOOT_REQ_PUT:
                {
                    if (!IsServer)
                        break;
                    COOPManager.LootNet.Server_HandleLootPutRequest(peer, reader);
                    break;
                }
            case Op.LOOT_REQ_TAKE:
                {
                    if (!IsServer)
                        break;
                    COOPManager.LootNet.Server_HandleLootTakeRequest(peer, reader);
                    break;
                }
            case Op.LOOT_PUT_OK:
                {
                    if (IsServer)
                        break;
                    COOPManager.LootNet.Client_OnLootPutOk(reader);
                    break;
                }
            case Op.LOOT_TAKE_OK:
                {
                    if (IsServer)
                        break;
                    COOPManager.LootNet.Client_OnLootTakeOk(reader);
                    break;
                }

            case Op.LOOT_DENY:
                {
                    if (IsServer)
                        break;
                    var reason = reader.GetString();
                    Debug.LogWarning($"[LOOT] 请求被拒绝：{reason}");

                    // no_inv 不要立刻重试，避免请求风暴
                    if (reason == "no_inv")
                        break;

                    // 其它可恢复类错误（如 rm_fail/bad_snapshot）再温和地刷新一次
                    var lv = LootView.Instance;
                    var inv = lv ? lv.TargetInventory : null;
                    if (inv)
                        COOPManager.LootNet.Client_RequestLootState(inv);
                    break;
                }

            case Op.AI_SEED_SNAPSHOT:
                {
                    if (!IsServer)
                        COOPManager.AIHandle.HandleAiSeedSnapshot(reader);
                    break;
                }
            case Op.AI_LOADOUT_SNAPSHOT:
                {
                    var ver = reader.GetByte();
                    var aiId = reader.GetInt();

                    var ne = reader.GetInt();
                    var equips = new List<(int slot, int tid)>(ne);
                    for (var i = 0; i < ne; ++i)
                    {
                        var sh = reader.GetInt();
                        var tid = reader.GetInt();
                        equips.Add((sh, tid));
                    }

                    var nw = reader.GetInt();
                    var weapons = new List<(int slot, int tid)>(nw);
                    for (var i = 0; i < nw; ++i)
                    {
                        var sh = reader.GetInt();
                        var tid = reader.GetInt();
                        weapons.Add((sh, tid));
                    }

                    var hasFace = reader.GetBool();
                    var faceJson = hasFace ? reader.GetString() : null;

                    var hasModelName = reader.GetBool();
                    var modelName = hasModelName ? reader.GetString() : null;

                    var iconType = reader.GetInt();

                    var showName = false;
                    if (ver >= 4)
                        showName = reader.GetBool();

                    string displayName = null;
                    if (ver >= 5)
                    {
                        var hasName = reader.GetBool();
                        if (hasName)
                            displayName = reader.GetString();
                    }

                    if (IsServer)
                        break;

                    // ✅ 客户端收到AI装备消息，更新追踪
                    COOPManager.AIHandle.Client_OnAiLoadoutReceived();

                    if (LogAiLoadoutDebug)
                        Debug.Log(
                            $"[AI-RECV] ver={ver} aiId={aiId} model='{modelName}' icon={iconType} showName={showName} faceLen={(faceJson != null ? faceJson.Length : 0)}"
                        );

                    if (AITool.aiById.TryGetValue(aiId, out var cmc) && cmc)
                        COOPManager
                            .AIHandle.Client_ApplyAiLoadout(
                                aiId,
                                equips,
                                weapons,
                                faceJson,
                                modelName,
                                iconType,
                                showName,
                                displayName
                            )
                            .Forget();
                    else
                        COOPManager.AIHandle.pendingAiLoadouts[aiId] = (
                            equips,
                            weapons,
                            faceJson,
                            modelName,
                            iconType,
                            showName,
                            displayName
                        );

                    break;
                }

            case Op.AI_TRANSFORM_SNAPSHOT:
                {
                    if (IsServer)
                        break;
                    var n = reader.GetInt();

                    if (!AITool._aiSceneReady)
                    {
                        for (var i = 0; i < n; ++i)
                        {
                            var aiId = reader.GetInt();
                            var p = reader.GetV3cm();
                            var f = reader.GetDir();
                            if (_pendingAiTrans.Count < 512)
                                _pendingAiTrans.Enqueue((aiId, p, f)); // 防“Mr.Sans”炸锅
                        }

                        break;
                    }

                    for (var i = 0; i < n; i++)
                    {
                        var aiId = reader.GetInt();
                        var p = reader.GetV3cm();
                        var f = reader.GetDir();
                        AITool.ApplyAiTransform(aiId, p, f); // 抽成函数复用下面冲队列逻辑
                    }

                    break;
                }

            case Op.AI_ANIM_SNAPSHOT:
                {
                    if (!IsServer)
                    {
                        var n = reader.GetInt();
                        for (var i = 0; i < n; ++i)
                        {
                            var id = reader.GetInt();
                            var st = new AiAnimState
                            {
                                speed = reader.GetFloat(),
                                dirX = reader.GetFloat(),
                                dirY = reader.GetFloat(),
                                hand = reader.GetInt(),
                                gunReady = reader.GetBool(),
                                dashing = reader.GetBool(),
                            };
                            if (!AITool.Client_ApplyAiAnim(id, st))
                                _pendingAiAnims[id] = st;
                        }
                    }

                    break;
                }

            case Op.AI_ATTACK_SWING:
                {
                    if (!IsServer)
                    {
                        var id = reader.GetInt();
                        if (AITool.aiById.TryGetValue(id, out var cmc) && cmc)
                        {
                            var anim = cmc.GetComponent<CharacterAnimationControl_MagicBlend>();
                            if (anim != null)
                                anim.OnAttack();
                            var model = cmc.characterModel;
                            if (model)
                                MeleeFx.SpawnSlashFx(model);
                        }
                    }

                    break;
                }

            case Op.AI_HEALTH_SYNC:
                {
                    var id = reader.GetInt();

                    float max = 0f,
                        cur = 0f;
                    if (reader.AvailableBytes >= 8)
                    {
                        max = reader.GetFloat();
                        cur = reader.GetFloat();
                    }
                    else
                    {
                        cur = reader.GetFloat();
                    }

                    COOPManager.AIHealth.Client_ApplyAiHealth(id, max, cur);
                    break;
                }

            case Op.AI_HEALTH_REPORT:
                {
                    if (IsServer)
                        COOPManager.AIHealth.HandleAiHealthReport(peer, reader);
                    break;
                }

            // --- 客户端：读取 aiId，并把它传下去 ---
            case Op.DEAD_LOOT_SPAWN:
                {
                    var scene = reader.GetInt();
                    var aiId = reader.GetInt();
                    var lootUid = reader.GetInt();
                    var pos = reader.GetV3cm();
                    var rot = reader.GetQuaternion();
                    if (SceneManager.GetActiveScene().buildIndex != scene)
                        break;

                    DeadLootBox.Instance.SpawnDeadLootboxAt(aiId, lootUid, pos, rot);
                    break;
                }

            case Op.AI_NAME_ICON:
                {
                    if (IsServer)
                        break;

                    var aiId = reader.GetInt();
                    var iconType = reader.GetInt();
                    var showName = reader.GetBool();
                    string displayName = null;
                    var hasName = reader.GetBool();
                    if (hasName)
                        displayName = reader.GetString();

                    if (AITool.aiById.TryGetValue(aiId, out var cmc) && cmc)
                        AIName
                            .RefreshNameIconWithRetries(cmc, iconType, showName, displayName)
                            .Forget();
                    else
                        Debug.LogWarning("[AI_icon_Name 10s] cmc is null!");
                    // 若当前还没绑定上 cmc，就先忽略；每 10s 会兜底播一遍
                    break;
                }

            case Op.PLAYER_DEAD_TREE:
                {
                    if (!IsServer)
                        break;
                    var pos = reader.GetV3cm();
                    var rot = reader.GetQuaternion();

                    var snap = ItemTool.ReadItemSnapshot(reader);
                    var tmpRoot = ItemTool.BuildItemFromSnapshot(snap);
                    if (!tmpRoot)
                    {
                        Debug.LogWarning("[LOOT] PLAYER_DEAD_TREE BuildItemFromSnapshot failed.");
                        break;
                    }

                    var deadPfb = LootManager.Instance.ResolveDeadLootPrefabOnServer();
                    var box = InteractableLootbox.CreateFromItem(
                        tmpRoot,
                        pos + Vector3.up * 0.10f,
                        rot,
                        true,
                        deadPfb
                    );
                    if (box)
                        DeadLootBox.Instance.Server_OnDeadLootboxSpawned(box, null); // 用新版重载：会发 lootUid + aiId + 随后 LOOT_STATE

                    if (remoteCharacters.TryGetValue(peer, out var proxy) && proxy)
                    {
                        Destroy(proxy);
                        remoteCharacters.Remove(peer);
                    }

                    // B) 广播给所有客户端：这个玩家的远程代理需要销毁
                    if (
                        playerStatuses.TryGetValue(peer, out var st)
                        && !string.IsNullOrEmpty(st.EndPoint)
                    )
                    {
                        var w2 = writer;
                        w2.Reset();
                        w2.Put((byte)Op.REMOTE_DESPAWN);
                        w2.Put(st.EndPoint); // 客户端用 EndPoint 当 key
                        netManager.SendSmart(w2, Op.REMOTE_DESPAWN);
                    }

                    if (tmpRoot && tmpRoot.gameObject)
                        Destroy(tmpRoot.gameObject);
                    break;
                }

            case Op.LOOT_REQ_SPLIT:
                {
                    if (!IsServer)
                        break;
                    COOPManager.LootNet.Server_HandleLootSplitRequest(peer, reader);
                    break;
                }

            case Op.REMOTE_DESPAWN:
                {
                    if (IsServer)
                        break; // 只客户端处理
                    var id = reader.GetString();
                    if (clientRemoteCharacters.TryGetValue(id, out var go) && go)
                        Destroy(go);
                    clientRemoteCharacters.Remove(id);
                    break;
                }

            case Op.AI_SEED_PATCH:
                COOPManager.AIHandle.HandleAiSeedPatch(reader);
                break;

            case Op.AUDIO_EVENT:
                {
                    var payload = CoopAudioEventPayload.Read(reader);

                    if (IsServer)
                    {
                        AudioEventMessage.ServerBroadcastExcept(payload, peer);
                        CoopAudioSync.HandleIncoming(payload);
                    }
                    else
                    {
                        CoopAudioSync.HandleIncoming(payload);
                    }

                    break;
                }

            case Op.DOOR_REQ_SET:
                {
                    if (IsServer)
                        COOPManager.Door.Server_HandleDoorSetRequest(peer, reader);
                    break;
                }
            case Op.DOOR_STATE:
                {
                    if (!IsServer)
                    {
                        var k = reader.GetInt();
                        var cl = reader.GetBool();
                        COOPManager.Door.Client_ApplyDoorState(k, cl);
                    }

                    break;
                }

            case Op.LOOT_REQ_SLOT_UNPLUG:
                {
                    if (IsServer)
                        COOPManager.LootNet.Server_HandleLootSlotUnplugRequest(peer, reader);
                    break;
                }
            case Op.LOOT_REQ_SLOT_PLUG:
                {
                    if (IsServer)
                        COOPManager.LootNet.Server_HandleLootSlotPlugRequest(peer, reader);
                    break;
                }

            case Op.SCENE_GATE_READY:
                {
                    if (IsServer)
                    {
                        var pid = reader.GetString();
                        var sid = reader.GetString();

                        // ✅ 使用 peer 对象作为键，而不是字符串 pid
                        // 这样可以避免客户端自报的 pid 与主机记录的 EndPoint 不匹配的问题
                        var peerEndpoint =
                            peer != null && peer.EndPoint != null
                                ? peer.EndPoint.ToString()
                                : "Unknown";
                        Debug.Log(
                            $"[GATE] 收到客户端举手：客户端报告的pid={pid}, peer实际地址={peerEndpoint}, sid={sid}, 当前门状态: {(SceneNet.Instance._srvSceneGateOpen ? "已开门" : "未开门")}"
                        );

                        // 若主机还没确定 gate 的 sid，就用第一次 READY 的 sid
                        if (string.IsNullOrEmpty(SceneNet.Instance._srvGateSid))
                            SceneNet.Instance._srvGateSid = sid;

                        if (sid == SceneNet.Instance._srvGateSid)
                        {
                            // ✅ 使用 peer 对象作为键（通过 playerStatuses 查找对应的 EndPoint）
                            if (
                                peer != null
                                && playerStatuses.TryGetValue(peer, out var status)
                                && status != null
                            )
                            {
                                SceneNet.Instance._srvGateReadyPids.Add(status.EndPoint);
                                Debug.Log(
                                    $"[GATE] 记录举手客户端：{status.EndPoint} (客户端报告={pid})，当前已举手: {SceneNet.Instance._srvGateReadyPids.Count} 人"
                                );

                                // ✅ 迟到放行：如果主机已经开门，立即放行该客户端
                                if (SceneNet.Instance._srvSceneGateOpen)
                                {
                                    var w = new NetDataWriter();
                                    w.Put((byte)Op.SCENE_GATE_RELEASE);
                                    w.Put(sid ?? "");
                                    peer.SendSmart(w, Op.SCENE_GATE_RELEASE);
                                    Debug.Log($"[GATE] 迟到放行：{status.EndPoint}");

                                    // ✅ 修复：异步发送战利品箱全量同步，避免主线程死锁
                                    StartCoroutine(SendLootFullSyncDelayed(peer));
                                }
                            }
                            else
                            {
                                Debug.LogWarning(
                                    $"[GATE] 无法找到客户端的 playerStatus，peer={peerEndpoint}"
                                );
                            }
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"[GATE] sid 不匹配：客户端={sid}, 主机={SceneNet.Instance._srvGateSid}"
                            );
                        }
                    }

                    break;
                }

            case Op.SCENE_GATE_RELEASE:
                {
                    if (!IsServer)
                    {
                        var sid = reader.GetString();
                        // 允许首次对齐或服务端/客户端估算不一致的情况
                        if (
                            string.IsNullOrEmpty(SceneNet.Instance._cliGateSid)
                            || sid == SceneNet.Instance._cliGateSid
                        )
                        {
                            SceneNet.Instance._cliGateSid = sid;
                            SceneNet.Instance._cliSceneGateReleased = true;
                            Debug.Log($"[GATE] ✅ 客户端收到主机放行：sid={sid}");
                            HealthM.Instance.Client_ReportSelfHealth_IfReadyOnce();
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"[GATE] release sid mismatch: srv={sid}, cli={SceneNet.Instance._cliGateSid} — accepting"
                            );
                            SceneNet.Instance._cliGateSid = sid; // 对齐后仍放行
                            SceneNet.Instance._cliSceneGateReleased = true;
                            HealthM.Instance.Client_ReportSelfHealth_IfReadyOnce();
                        }
                    }

                    break;
                }

            case Op.PLAYER_HURT_EVENT:
                if (!IsServer)
                    HealthM.Instance.Client_ApplySelfHurtFromServer(reader);
                break;

            default:
                // 有未知 opcode 时给出警告，便于排查（比如双端没一起更新）
                Debug.LogWarning($"Unknown opcode: {(byte)op}");
                break;
        }

        reader.Recycle();
    }

    private void OnSceneLoaded_IndexDestructibles(Scene s, LoadSceneMode m)
    {
        if (!networkStarted)
            return;
        COOPManager.destructible.BuildDestructibleIndex();

        HealthTool._cliHookedSelf = false;

        if (!IsServer)
        {
            HealthTool._cliInitHpReported = false; // 允许再次上报
            HealthM.Instance.Client_ReportSelfHealth_IfReadyOnce(); // 你已有的方法（只上报一次）
        }

        try
        {
            if (!networkStarted || localPlayerStatus == null)
                return;

            var ok = LocalPlayerManager.Instance.ComputeIsInGame(out var sid);
            localPlayerStatus.SceneId = sid;
            localPlayerStatus.IsInGame = ok;

            if (!IsServer)
                Send_ClientStatus.Instance.SendClientStatusUpdate();
            else
                SendLocalPlayerStatus.Instance.SendPlayerStatusUpdate();
        }
        catch { }
    }

    /// <summary>
    /// ✅ 优化：移除重复的 BuildDestructibleIndex 调用
    /// 现在由 DestructibleCache 统一管理，在 RefreshAllCaches 中已经刷新
    /// </summary>
    private void OnLevelInitialized_IndexDestructibles()
    {
        if (!networkStarted)
            return;

        // ⚠️ 已移除重复调用：BuildDestructibleIndex 已在 OnSceneLoaded 中调用
        // ⚠️ DestructibleCache 会在 RefreshAllCaches 中刷新，BuildDestructibleIndex 会使用缓存
        // COOPManager.destructible.BuildDestructibleIndex();

        Debug.Log("[MOD] OnLevelInitialized_IndexDestructibles: 跳过重复的 BuildDestructibleIndex 调用");
    }

    /// <summary>
    /// ✅ 延迟发送战利品箱全量同步（避免主线程死锁）
    /// ⚠️ 注意：大型地图（>500个箱子）会导致网络IO阻塞，已禁用全量同步
    /// </summary>
    private System.Collections.IEnumerator SendLootFullSyncDelayed(NetPeer peer)
    {
        // 等待一帧，让主线程先完成其他操作
        yield return null;

        // ⚠️ 禁用战利品全量同步：在大型地图上会导致网络IO阻塞，主线程卡死
        // 解决方案：完全依赖增量同步（LOOT_STATE 消息），由玩家打开箱子时触发同步
        Debug.Log($"[MOD-GATE] 战利品全量同步已禁用（避免大型地图网络IO阻塞） → {peer.EndPoint}");
        Debug.Log($"[MOD-GATE] 战利品将通过增量同步（玩家交互时）自动同步");

        yield break;

        /* 原始代码（已禁用）
        try
        {
            Debug.Log($"[MOD-GATE] 开始发送战利品箱全量同步 → {peer.EndPoint}");
            LootFullSyncMessage.Host_SendLootFullSync(peer);
            Debug.Log($"[MOD-GATE] 战利品箱全量同步发送完成 → {peer.EndPoint}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[MOD-GATE] 发送战利品箱全量同步失败: {ex.Message}\n{ex.StackTrace}");
        }
        */
    }

    public struct Pending
    {
        public Inventory inv;
        public int srcPos;
        public int count;
    }
}
