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

// 使用宏定义启用新的消息处理系统，方便随时回退
#define USE_NEW_OP_NETMESSAGECONSUMER

using Duckov.UI;
using EscapeFromDuckovCoopMod.Net;
using EscapeFromDuckovCoopMod.Utils; // 引入智能发送扩展方法
using EscapeFromDuckovCoopMod.Utils.Logger.Core;
using EscapeFromDuckovCoopMod.Utils.Logger.Tools;
using EscapeFromDuckovCoopMod.Utils.NetHelper;
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
public partial class ModBehaviourF : MonoBehaviour
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

#if USE_NEW_OP_NETMESSAGECONSUMER
        // 注册 Op 处理器
        RegisterOpHandlers();
#endif
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
                syncUI.RegisterTask("weather", "ui.sync.task.weather");
                syncUI.RegisterTask("player_health", "ui.sync.task.playerHealth");
                syncUI.RegisterTask("ai_loadouts_receive", "ui.sync.task.aiLoadoutsReceive"); // ✅ 客户端也需要追踪AI装备接收进度
            }

            if (IsServer)
            {
                syncUI.RegisterTask("ai_seeds", "ui.sync.task.aiSeeds");
                syncUI.RegisterTask("ai_loadouts", "ui.sync.task.aiLoadouts");
                syncUI.RegisterTask("destructible", "ui.sync.task.destructible");
            }

            syncUI.RegisterTask("ai_names", "ui.sync.task.aiNames");
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

#if USE_NEW_OP_NETMESSAGECONSUMER
        // 使用新的消息处理系统，通过 NetMessageConsumer 分发到各个注册的处理器
        // 注意：若使用本处理方式，需要增添或修改对OP的处理逻辑，请前往与本文件同目录的 Mod_RegisterOpHandler 文件中修改 RegisterOpHandlers 方法
        NetMessageConsumer.Instance.OnNetworkReceive(peer, reader, channelNumber, deliveryMethod);
#else
        // 旧版本：使用 switch 语句，但调用封装好的处理方法
        var op = (Op)reader.GetByte();

        switch (op)
        {
            case Op.PLAYER_STATUS_UPDATE:
                HandlePlayerStatusUpdate(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.CLIENT_STATUS_UPDATE:
                HandleClientStatusUpdate(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.POSITION_UPDATE:
                HandlePositionUpdate(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.ANIM_SYNC:
                HandleAnimSync(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.EQUIPMENT_UPDATE:
                HandleEquipmentUpdate(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.PLAYERWEAPON_UPDATE:
                HandleWeaponUpdate(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.FIRE_REQUEST:
                HandleFireRequest(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.FIRE_EVENT:
                HandleFireEvent(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.JSON:
                HandleJsonMessage(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.GRENADE_THROW_REQUEST:
                HandleGrenadeThrowRequest(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.GRENADE_SPAWN:
                HandleGrenadeSpawn(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.GRENADE_EXPLODE:
                HandleGrenadeExplode(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.ITEM_DROP_REQUEST:
                HandleItemDropRequest(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.ITEM_SPAWN:
                HandleItemSpawn(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.ITEM_PICKUP_REQUEST:
                HandleItemPickupRequest(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.ITEM_DESPAWN:
                HandleItemDespawn(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.MELEE_ATTACK_REQUEST:
                HandleMeleeAttackRequest(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.MELEE_ATTACK_SWING:
                HandleMeleeAttackSwing(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.MELEE_HIT_REPORT:
                HandleMeleeHitReport(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.ENV_HURT_REQUEST:
                HandleEnvHurtRequest(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.ENV_HURT_EVENT:
                HandleEnvHurtEvent(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.ENV_DEAD_EVENT:
                HandleEnvDeadEvent(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.PLAYER_HEALTH_REPORT:
                HandlePlayerHealthReport(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.AUTH_HEALTH_SELF:
                HandleAuthHealthSelf(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.AUTH_HEALTH_REMOTE:
                HandleAuthHealthRemote(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.PLAYER_BUFF_SELF_APPLY:
                HandlePlayerBuffSelfApply(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.HOST_BUFF_PROXY_APPLY:
                HandleHostBuffProxyApply(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.SCENE_VOTE_START:
                HandleSceneVoteStart(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.SCENE_VOTE_REQ:
                HandleSceneVoteReq(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.SCENE_READY_SET:
                HandleSceneReadySet(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.SCENE_BEGIN_LOAD:
                HandleSceneBeginLoad(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.SCENE_CANCEL:
                HandleSceneCancel(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.SCENE_READY:
                HandleSceneReady(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.PLAYER_APPEARANCE:
                HandlePlayerAppearance(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.ENV_SYNC_REQUEST:
                HandleEnvSyncRequest(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.ENV_SYNC_STATE:
                HandleEnvSyncState(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.LOOT_REQ_OPEN:
                HandleLootReqOpen(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.LOOT_STATE:
                HandleLootState(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.LOOT_REQ_PUT:
                HandleLootReqPut(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.LOOT_REQ_TAKE:
                HandleLootReqTake(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.LOOT_PUT_OK:
                HandleLootPutOk(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.LOOT_TAKE_OK:
                HandleLootTakeOk(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.LOOT_DENY:
                HandleLootDeny(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.AI_SEED_SNAPSHOT:
                HandleAiSeedSnapshot(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.AI_LOADOUT_SNAPSHOT:
                HandleAiLoadoutSnapshot(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.AI_TRANSFORM_SNAPSHOT:
                HandleAiTransformSnapshot(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.AI_ANIM_SNAPSHOT:
                HandleAiAnimSnapshot(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.AI_ATTACK_SWING:
                HandleAiAttackSwing(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.AI_HEALTH_SYNC:
                HandleAiHealthSync(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.AI_HEALTH_REPORT:
                HandleAiHealthReport(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.DEAD_LOOT_SPAWN:
                HandleDeadLootSpawn(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.AI_NAME_ICON:
                HandleAiNameIcon(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.PLAYER_DEAD_TREE:
                HandlePlayerDeadTree(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.LOOT_REQ_SPLIT:
                HandleLootReqSplit(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.REMOTE_DESPAWN:
                HandleRemoteDespawn(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.AI_SEED_PATCH:
                HandleAiSeedPatch(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.AUDIO_EVENT:
                HandleAudioEvent(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.DOOR_REQ_SET:
                HandleDoorReqSet(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.DOOR_STATE:
                HandleDoorState(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.LOOT_REQ_SLOT_UNPLUG:
                HandleLootReqSlotUnplug(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.LOOT_REQ_SLOT_PLUG:
                HandleLootReqSlotPlug(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.SCENE_GATE_READY:
                HandleSceneGateReady(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.SCENE_GATE_RELEASE:
                HandleSceneGateRelease(peer, reader, channelNumber, deliveryMethod);
                break;

            case Op.PLAYER_HURT_EVENT:
                HandlePlayerHurtEvent(peer, reader, channelNumber, deliveryMethod);
                break;

            default:
                // 有未知 opcode 时给出警告，便于排查（比如双端没一起更新）
                Debug.LogWarning($"Unknown opcode: {(byte)op}");
                break;
        }
#endif
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
