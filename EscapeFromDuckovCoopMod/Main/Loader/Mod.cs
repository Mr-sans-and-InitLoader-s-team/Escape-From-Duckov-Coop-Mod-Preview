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
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;
using UnityEngine.SceneManagement;
using static EscapeFromDuckovCoopMod.LootNet;

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
    private const float EnsureRemoteInterval = 1.0f; // 每秒兜底一次，够用又不吵
    private const float ENV_SYNC_INTERVAL = 1.0f; // 每 1 秒广播一次；可按需 0.5~2 调
    private const float AI_TF_INTERVAL = 0.05f;
    private const float AI_ANIM_INTERVAL = 0.10f; // 10Hz 动画参数广播
    private const float AI_NAMEICON_INTERVAL = 10f;
    public static ModBehaviourF Instance; //一切的开始 Hello World!

    public static CustomFaceSettingData localPlayerCustomFace;

    public static bool LogAiHpDebug = false; // 需要时改为 true，打印 [AI-HP] 日志

    public static bool LogAiLoadoutDebug = true;

    // --- 反编译类的私有序列化字段直达句柄---
    private static readonly AccessTools.FieldRef<CharacterRandomPreset, bool>
        FR_UsePlayerPreset = AccessTools.FieldRefAccess<CharacterRandomPreset, bool>("usePlayerPreset");

    private static readonly AccessTools.FieldRef<CharacterRandomPreset, CustomFacePreset>
        FR_FacePreset = AccessTools.FieldRefAccess<CharacterRandomPreset, CustomFacePreset>("facePreset");

    private static readonly AccessTools.FieldRef<CharacterRandomPreset, CharacterModel>
        FR_CharacterModel = AccessTools.FieldRefAccess<CharacterRandomPreset, CharacterModel>("characterModel");

    private static readonly AccessTools.FieldRef<CharacterRandomPreset, CharacterIconTypes>
        FR_IconType = AccessTools.FieldRefAccess<CharacterRandomPreset, CharacterIconTypes>("characterIconType");

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
    public readonly Dictionary<int, AiAnimState> _pendingAiAnims = new();

    public readonly Queue<(int id, Vector3 p, Vector3 f)> _pendingAiTrans = new();

    private readonly Dictionary<int, (int capacity, List<(int pos, ItemSnapshot snap)>)> _pendingLootStates = new();

    private readonly KeyCode readyKey = KeyCode.J;

    // 主机端的节流定时器
    private float _aiAnimTimer;

    private float _aiNameIconTimer;

    private float _aiTfTimer;

    private float _ensureRemoteTick = 0f;
    private bool _envReqOnce = false;
    private string _envReqSid;
    
    private int _positionRecvCount = 0;
    private float _lastPosRecvLogTime = 0;

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
        
        // 首先创建HybridRPCManager（必须在RPC注册之前）
        var rpcManagerGO = new GameObject("HybridRPCManager");
        DontDestroyOnLoad(rpcManagerGO);
        rpcManagerGO.AddComponent<Net.HybridP2P.HybridRPCManager>();
        Debug.Log("[Mod] HybridRPCManager initialized");
        
        // 注册所有核心网络 RPCs（现在HybridRPCManager.Instance已可用）
        Net.HybridP2P.CoreNetworkRPCs.RegisterAllRPCs();
        
        // 初始化击杀播报系统
        var killFeedGO = new GameObject("KillFeedManager");
        DontDestroyOnLoad(killFeedGO);
        killFeedGO.AddComponent<KillFeedManager>().Init();
        Debug.Log("[Mod] KillFeedManager initialized");
        
        // 初始化统一投票系统
        var voteGO = new GameObject("UnifiedVoteSystem");
        DontDestroyOnLoad(voteGO);
        voteGO.AddComponent<Net.HybridP2P.UnifiedVoteSystem>();
        Debug.Log("[Mod] UnifiedVoteSystem initialized");
        
        var latencyGO = new GameObject("NetworkLatencyMonitor");
        DontDestroyOnLoad(latencyGO);
        latencyGO.AddComponent<Net.Core.NetworkLatencyMonitor>();
        Debug.Log("[Mod] NetworkLatencyMonitor initialized");
        
        var hybridTransportGO = new GameObject("HybridTransport");
        DontDestroyOnLoad(hybridTransportGO);
        hybridTransportGO.AddComponent<Net.Core.HybridTransport>();
        Debug.Log("[Mod] HybridTransport initialized");
        
        var restVoteGO = new GameObject("RESTfulVoteSystem");
        DontDestroyOnLoad(restVoteGO);
        restVoteGO.AddComponent<Net.Core.RESTfulVoteSystem>();
        Debug.Log("[Mod] RESTfulVoteSystem initialized");
    }

    private void Update()
    {
        if (CharacterMainControl.Main != null && !isinit)
        {
            isinit = true;
            Traverse.Create(CharacterMainControl.Main.EquipmentController).Field<Slot>("armorSlot").Value.onSlotContentChanged +=
                LocalPlayerManager.Instance.ModBehaviour_onSlotContentChanged;
            Traverse.Create(CharacterMainControl.Main.EquipmentController).Field<Slot>("helmatSlot").Value.onSlotContentChanged +=
                LocalPlayerManager.Instance.ModBehaviour_onSlotContentChanged;
            Traverse.Create(CharacterMainControl.Main.EquipmentController).Field<Slot>("faceMaskSlot").Value.onSlotContentChanged +=
                LocalPlayerManager.Instance.ModBehaviour_onSlotContentChanged;
            Traverse.Create(CharacterMainControl.Main.EquipmentController).Field<Slot>("backpackSlot").Value.onSlotContentChanged +=
                LocalPlayerManager.Instance.ModBehaviour_onSlotContentChanged;
            Traverse.Create(CharacterMainControl.Main.EquipmentController).Field<Slot>("headsetSlot").Value.onSlotContentChanged +=
                LocalPlayerManager.Instance.ModBehaviour_onSlotContentChanged;

            CharacterMainControl.Main.OnHoldAgentChanged += LocalPlayerManager.Instance.Main_OnHoldAgentChanged;
        }


        //暂停显示出鼠标
        if (Pausebool)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        if (CharacterMainControl.Main == null) isinit = false;

        //if (ModUI.Instance != null && Input.GetKeyDown(KeyCode.Home)) ModUI.Instance.showUI = !ModUI.Instance.showUI;

        if (networkStarted)
        {
            netManager.PollEvents();
            SceneNet.Instance.TrySendSceneReadyOnce();
            if (!isinit2)
            {
                isinit2 = true;
                if (!IsServer) HealthM.Instance.Client_ReportSelfHealth_IfReadyOnce();
            }

            // if (IsServer) Server_EnsureAllHealthHooks();

            if (!IsServer && !isConnecting && NetService.Instance.TransportMode != NetworkTransportMode.SteamP2P)
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

            if (!IsServer && !string.IsNullOrEmpty(SceneNet.Instance._sceneReadySidSent) && _envReqSid != SceneNet.Instance._sceneReadySidSent)
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
                        if (!cmc) continue;

                        var pr = cmc.characterPreset;
                        if (!pr) continue;

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
                        catch
                        {
                        }

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

        if (IsServer) HealthM.Instance.Server_EnsureAllHealthHooks();
        if (!IsServer) HealthM.Instance.Client_ReportSelfHealth_IfReadyOnce();

        // 投票期间按 J 切换准备
        if (SceneNet.Instance.sceneVoteActive && Input.GetKeyDown(readyKey))
        {
            SceneNet.Instance.localReady = !SceneNet.Instance.localReady;
            if (IsServer) SceneNet.Instance.Server_OnSceneReadySet(null, SceneNet.Instance.localReady); // 主机自己也走同一套
            else SceneNet.Instance.Client_SendReadySet(SceneNet.Instance.localReady); // 客户端上报主机
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
            if (IsServer) HealthM.Instance.Server_EnsureAllHealthHooks();

            // 客户端：本场景里若还没成功上报，就每帧重试直到成功
            if (!IsServer && !HealthTool._cliInitHpReported) HealthM.Instance.Client_ReportSelfHealth_IfReadyOnce();

            // 客户端：给自己的 Health 持续打钩，变化就上报
            if (!IsServer) HealthTool.Client_HookSelfHealth();
        }

        if (Spectator.Instance._spectatorActive)
        {
            ClosureView.Instance.gameObject.SetActive(false);
            // 动态剔除“已死/被销毁/不在本地图”的目标
            Spectator.Instance._spectateList = Spectator.Instance._spectateList.Where(c =>
            {
                if (!LocalPlayerManager.Instance.IsAlive(c)) return false;

                var mySceneId = localPlayerStatus != null ? localPlayerStatus.SceneId : null;
                if (string.IsNullOrEmpty(mySceneId))
                    LocalPlayerManager.Instance.ComputeIsInGame(out mySceneId);

                // 反查该 CMC 对应的 peer 的 SceneId
                string peerScene = null;
                if (IsServer)
                {
                    foreach (var kv in remoteCharacters)
                        if (kv.Value != null && kv.Value.GetComponent<CharacterMainControl>() == c)
                        {
                            if (!SceneM._srvPeerScene.TryGetValue(kv.Key, out peerScene) && playerStatuses.TryGetValue(kv.Key, out var st))
                                peerScene = st?.SceneId;
                            break;
                        }
                }
                else
                {
                    foreach (var kv in clientRemoteCharacters)
                        if (kv.Value != null && kv.Value.GetComponent<CharacterMainControl>() == c)
                        {
                            if (clientPlayerStatuses.TryGetValue(kv.Key, out var st)) peerScene = st?.SceneId;
                            break;
                        }
                }

                return Spectator.AreSameMap(mySceneId, peerScene);
            }).ToList();


            // 全员阵亡 → 退出观战并弹出结算
            if (Spectator.Instance._spectateList.Count == 0 || SceneM.AllPlayersDead())
            {
                Spectator.Instance.EndSpectatorAndShowClosure();
                return;
            }

            if (_spectateIdx < 0 || _spectateIdx >= Spectator.Instance._spectateList.Count)
                _spectateIdx = 0;

            // 当前目标若死亡，自动跳到下一个
            if (!LocalPlayerManager.Instance.IsAlive(Spectator.Instance._spectateList[_spectateIdx]))
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
        LevelManager.OnAfterLevelInitialized += LevelManager_OnAfterLevelInitialized;
        LevelManager.OnLevelInitialized += OnLevelInitialized_IndexDestructibles;


        SceneManager.sceneLoaded += SceneManager_sceneLoaded;
        LevelManager.OnLevelInitialized += LevelManager_OnLevelInitialized;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded_IndexDestructibles;
        LevelManager.OnLevelInitialized -= OnLevelInitialized_IndexDestructibles;
        //   LevelManager.OnAfterLevelInitialized -= _OnAfterLevelInitialized_ServerGate;

        SceneManager.sceneLoaded -= SceneManager_sceneLoaded;
        LevelManager.OnLevelInitialized -= LevelManager_OnLevelInitialized;
    }

    private void OnDestroy()
    {
        NetService.Instance.StopNetwork();
    }

    private void LevelManager_OnAfterLevelInitialized()
    {
        if (IsServer && networkStarted)
            SceneNet.Instance.Server_SceneGateAsync().Forget();
    }

    private void LevelManager_OnLevelInitialized()
    {
        AITool.ResetAiSerials();
        if (!IsServer) HealthM.Instance.Client_ReportSelfHealth_IfReadyOnce();
        SceneNet.Instance.TrySendSceneReadyOnce();
        if (!IsServer) COOPManager.Weather.Client_RequestEnvSync();

        if (IsServer) COOPManager.AIHandle.Server_SendAiSeeds();
        AIName.Client_ResetNameIconSeal_OnLevelInit();
    }

    //arg!!!!!!!!!!!
    private void SceneManager_sceneLoaded(Scene arg0, LoadSceneMode arg1)
    {
        SceneNet.Instance.TrySendSceneReadyOnce();
        if (!IsServer) COOPManager.Weather.Client_RequestEnvSync();
    }


    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        // 统一：读取 1 字节的操作码（Op）
        if (reader.AvailableBytes <= 0)
        {
            reader.Recycle();
            return;
        }

        // 先查看是否为RPC消息或RESTful消息（不消耗reader）
        byte firstByte = reader.PeekByte();
        
        if (firstByte == 255) // RPC_MESSAGE_TYPE
        {
            // 处理RPC消息
            var rpcManager = EscapeFromDuckovCoopMod.Net.HybridP2P.HybridRPCManager.Instance;
            if (rpcManager != null)
            {
                long connectionId = 0;
                
                // Steam P2P模式：优先使用SteamID作为稳定的connectionId
                if (peer != null && peer.EndPoint != null)
                {
                    var mapper = EscapeFromDuckovCoopMod.VirtualEndpointManager.Instance;
                    if (mapper != null && mapper.TryGetSteamID(peer.EndPoint, out var steamId))
                    {
                        connectionId = (long)steamId.m_SteamID;
                    }
                    else if (peer.Id > 0)
                    {
                        connectionId = peer.Id;
                    }
                    else
                    {
                        // 回退：使用peer对象的哈希码
                        connectionId = peer.GetHashCode();
                    }
                }
                
                rpcManager.ProcessLANMessage(connectionId, reader, peer);
            }
            reader.Recycle();
            return;
        }
        
        if (firstByte == 254) // RESTful Request
        {
            reader.GetByte();
            string json = reader.GetString();
            var restTransport = Net.Core.SimpleRESTfulTransport.Instance;
            if (restTransport != null)
            {
                restTransport.OnReceiveRequest(json);
            }
            reader.Recycle();
            return;
        }
        
        if (firstByte == 253) // RESTful Response
        {
            reader.GetByte();
            string json = reader.GetString();
            var restTransport = Net.Core.SimpleRESTfulTransport.Instance;
            if (restTransport != null)
            {
                restTransport.OnReceiveResponse(json);
            }
            reader.Recycle();
            return;
        }

        var op = (Op)reader.GetByte();
        
        if ((byte)op == 19)
        {
            Debug.Log($"[VOTE-RECV-ENTRY] 收到投票消息 op=19 (SCENE_VOTE_START), IsServer={IsServer}, peer={peer?.EndPoint}, availableBytes={reader.AvailableBytes}");
        }
        
        if (IsServer && peer != null)
        {
            var relay = EscapeFromDuckovCoopMod.Net.HybridP2P.HybridP2PRelay.Instance;
            if (relay != null)
            {
                var endPoint = peer.EndPoint.ToString();
                if (!relay.ValidateAndRelayPacket(endPoint, reader, (byte)op))
                {
                    Debug.LogWarning($"[ModBehaviourF] Packet validation failed from {endPoint}, op={op}");
                    reader.Recycle();
                    return;
                }
                
                var latency = relay.GetLatency(endPoint);
                if (Service.playerStatuses.TryGetValue(peer, out var status))
                {
                    status.Latency = (int)latency;
                    status.NATType = relay.GetConnectionNATType(endPoint);
                    status.UseRelay = relay.IsUsingRelay(endPoint);
                }
            }
        }
        else if (!IsServer && peer != null)
        {
            var relay = EscapeFromDuckovCoopMod.Net.HybridP2P.HybridP2PRelay.Instance;
            if (relay != null)
            {
                var endPoint = peer.EndPoint.ToString();
                var latency = relay.GetLatency(endPoint);
                if (Service.playerStatuses.TryGetValue(peer, out var status))
                {
                    status.Latency = (int)latency;
                    status.NATType = relay.GetConnectionNATType(endPoint);
                    status.UseRelay = relay.IsUsingRelay(endPoint);
                }
            }
        }
        //  Debug.Log($"[RECV OP] {(byte)op}, avail={reader.AvailableBytes}");

        switch (op)
        {
            case Op.AUTH_HEALTH_REMOTE:
            case Op.MELEE_ATTACK_SWING:
            case Op.SCENE_VOTE_START:
            case Op.SCENE_READY_SET:
            case Op.SCENE_BEGIN_LOAD:
            case Op.SCENE_CANCEL:
            case Op.LOOT_STATE:
            case Op.LOOT_PUT_OK:
            case Op.LOOT_TAKE_OK:
            case Op.LOOT_DENY:
            case Op.AI_SEED_SNAPSHOT:
            case Op.AI_LOADOUT_SNAPSHOT:
            case Op.AI_TRANSFORM_SNAPSHOT:
            case Op.AI_ANIM_SNAPSHOT:
            case Op.AI_ATTACK_SWING:
            case Op.AI_HEALTH_SYNC:
            case Op.AI_NAME_ICON:
            case Op.AI_SEED_PATCH:
            case Op.DEAD_LOOT_SPAWN:
            case Op.DISCOVER_REQUEST:
            case Op.DISCOVER_RESPONSE:
            case Op.ENV_SYNC_REQUEST:
            case Op.ENV_SYNC_STATE:
            case Op.DOOR_REQ_SET:
            case Op.DOOR_STATE:
            case Op.LOOT_REQ_SLOT_UNPLUG:
            case Op.LOOT_REQ_SLOT_PLUG:
            case Op.SCENE_VOTE_REQ:
            case Op.AI_HEALTH_REPORT:
            case Op.AI_FREEZE_TOGGLE:
            case Op.AI_ATTACK_TELL:
            case Op.DEAD_LOOT_DESPAWN:
            case Op.SCENE_GATE_READY:
            case Op.SCENE_GATE_RELEASE:
            case Op.PLAYER_DEAD_TREE:
            case Op.PLAYER_HURT_EVENT:
            case Op.HOST_BUFF_PROXY_APPLY:
            case Op.PLAYER_BUFF_SELF_APPLY:
            case Op.ENV_HURT_REQUEST:
            case Op.ENV_HURT_EVENT:
            case Op.ENV_DEAD_EVENT:
            case Op.MELEE_ATTACK_REQUEST:
            case Op.MELEE_HIT_REPORT:
            case Op.LOOT_REQ_SPLIT:
            case Op.LOOT_REQ_OPEN:
            case Op.LOOT_REQ_PUT:
            case Op.LOOT_REQ_TAKE:
                break;

            default:
                if ((byte)op != 200)
                {
                    Debug.LogWarning($"Unknown opcode: {(byte)op}");
                }
                break;
        }

        reader.Recycle();
    }

    private void OnSceneLoaded_IndexDestructibles(Scene s, LoadSceneMode m)
    {
        if (!networkStarted) return;
        COOPManager.destructible.BuildDestructibleIndex();

        HealthTool._cliHookedSelf = false;

        if (!IsServer)
        {
            HealthTool._cliInitHpReported = false; // 允许再次上报
            HealthM.Instance.Client_ReportSelfHealth_IfReadyOnce(); // 你已有的方法（只上报一次）
        }

        try
        {
            if (!networkStarted || localPlayerStatus == null) return;

            var ok = LocalPlayerManager.Instance.ComputeIsInGame(out var sid);
            localPlayerStatus.SceneId = sid;
            localPlayerStatus.IsInGame = ok;

            if (!IsServer) Send_ClientStatus.Instance.SendClientStatusUpdate();
            else SendLocalPlayerStatus.Instance.SendPlayerStatusUpdate();
        }
        catch
        {
        }
    }

    private void OnLevelInitialized_IndexDestructibles()
    {
        if (!networkStarted) return;
        COOPManager.destructible.BuildDestructibleIndex();
    }

    public struct Pending
    {
        public Inventory inv;
        public int srcPos;
        public int count;
    }
}
