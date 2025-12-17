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

using System;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;
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
    public static ModBehaviourF Instance; //一切的开始 Hello World!

    public static CustomFaceSettingData localPlayerCustomFace;

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

    public static readonly Dictionary<GameObject, string> PhantomPlayerNames = new();

    private static Transform _fallbackMuzzleAnchor;
    public float broadcastTimer;
    public float syncTimer;

    public bool Pausebool;
    public int _clientLootSetupDepth;

    public GameObject aiTelegraphFx;
    public DamageInfo _lastDeathInfo;


    // 客户端：远端玩家待应用的外观缓存
    private readonly Dictionary<string, string> _cliPendingFace = new();

    private readonly Dictionary<int, (int capacity, List<(int pos, ItemSnapshot snap)>)> _pendingLootStates = new();

    private readonly KeyCode readyKey = KeyCode.J;

    private float _ensureRemoteTick = 0f;
    private string _envReqSid;


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
        PerformanceDiagnostics.Instance.Reset();
        NetDiagnostics.Instance.Reset();
    }

    private void Update()
    {
        PerformanceDiagnostics.Instance.Update(Time.unscaledDeltaTime);

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

            if (!IsServer && !string.IsNullOrEmpty(SceneNet.Instance._sceneReadySidSent) &&
                _envReqSid != SceneNet.Instance._sceneReadySidSent)
            {
                _envReqSid = SceneNet.Instance._sceneReadySidSent; // 本场景只请求一次
                COOPManager.Weather.Client_RequestSnapshot();
            }

            if (IsServer)
            {
                COOPManager.Weather.Server_Update(Time.deltaTime);
                COOPManager.AI?.Server_Update(Time.deltaTime);
                COOPManager.ItemNet?.Server_Update(Time.deltaTime);
            }
            else
            {
                COOPManager.Weather.Client_Update(Time.deltaTime);
                COOPManager.AI?.Client_Update(Time.deltaTime);
                COOPManager.ItemNet?.Client_Update(Time.deltaTime);
            }

            if (NetService.Instance.netManager != null)
            {
                if (!SteamP2PLoader.Instance._isOptimized && SteamP2PLoader.Instance.UseSteamP2P)
                {
                    NetService.Instance.netManager.UpdateTime = 1;
                    SteamP2PLoader.Instance._isOptimized = true;
                    Debug.Log("[SteamP2P] LiteNetLib thread optimized (1ms update cycle)");
                }
            }
        }

        LocalPlayerManager.Instance.UpdatePlayerStatuses();
        LocalPlayerManager.Instance.UpdateRemoteCharacters();

        //if (Input.GetKeyDown(ModUI.Instance.toggleWindowKey)) ModUI.Instance.showPlayerStatusWindow = !ModUI.Instance.showPlayerStatusWindow;

        COOPManager.GrenadeM.ProcessPendingGrenades();

        if (IsServer) HealthM.Instance.Server_EnsureAllHealthHooks();

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
                COOPManager.Weather.Client_RequestSnapshot();
            }

            // 主机：每帧确保给所有 Health 打钩（含新生成/换图后新克隆）
            if (IsServer) HealthM.Instance.Server_EnsureAllHealthHooks();

            // 客户端：本场景里若还没成功上报，就每帧重试直到成功

            // 客户端：给自己的 Health 持续打钩，变化就上报
            HealthTool.Client_HookSelfHealth();
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

                if (IsServer)
                {
                    var rpc = new SpectatorForceEndRpc();
                    CoopTool.SendRpc(in rpc);
                }

                return;
            }

            if (_spectateIdx < 0 || _spectateIdx >= Spectator.Instance._spectateList.Count)
                _spectateIdx = 0;

            // 当前目标若死亡，自动跳到下一个
            if (!LocalPlayerManager.Instance.IsAlive(Spectator.Instance._spectateList[_spectateIdx]))
                Spectator.Instance.SpectateNext();

            // 客户端观战：按 F8 直接退出观战并进入结算
            if (!IsServer && Input.GetKeyDown(KeyCode.F8))
            {
                Spectator.Instance.EndSpectatorAndShowClosure(true);
                return;
            }

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
        SceneManager.sceneUnloaded += OnSceneUnloaded_ClearRegistries;
        LevelManager.OnAfterLevelInitialized += LevelManager_OnAfterLevelInitialized;
        LevelManager.OnLevelInitialized += OnLevelInitialized_IndexDestructibles;


        SceneManager.sceneLoaded += SceneManager_sceneLoaded;
        LevelManager.OnLevelInitialized += LevelManager_OnLevelInitialized;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded_IndexDestructibles;
        SceneManager.sceneUnloaded -= OnSceneUnloaded_ClearRegistries;
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
        SceneNet.Instance.TrySendSceneReadyOnce();
        if (!IsServer) COOPManager.Weather.Client_RequestSnapshot();
    }

    private void OnSceneUnloaded_ClearRegistries(Scene scene)
    {
        CoopSyncDatabase.Environment.Clear();
        CoopSyncDatabase.AI.Clear();
        CoopSyncDatabase.Drops.Clear();
        COOPManager.AI?.Reset();
        COOPManager.ItemNet?.Reset();
        COOPManager.ExplosiveBarrels?.Reset();
    }

    //arg!!!!!!!!!!!
    private void SceneManager_sceneLoaded(Scene arg0, LoadSceneMode arg1)
    {
        SceneNet.Instance.TrySendSceneReadyOnce();
        if (!IsServer) COOPManager.Weather.Client_RequestSnapshot();
    }


    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        // 统一：读取 1 字节的操作码（Op）
        if (reader.AvailableBytes <= 0)
        {
            reader.Recycle();
            return;
        }

        var totalBytes = reader.AvailableBytes;
        var opByte = reader.GetByte();
        var payloadBytes = Math.Max(0, totalBytes - 1);
        var op = (Op)opByte;
        NetDiagnostics.Instance.RecordInbound(op, payloadBytes);
        //  Debug.Log($"[RECV OP] {(byte)op}, avail={reader.AvailableBytes}");

        if (RpcRegistry.TryHandle(op, new RpcContext(NetService.Instance, peer), reader))
        {
            reader.Recycle();
            return;
        }

        switch (op)
        {
            default:
                // 有未知 opcode 时给出警告，便于排查（比如双端没一起更新）
                Debug.LogWarning($"Unknown opcode: {(byte)op}");
                break;

            case Op.GRENADE_THROW_REQUEST:
                if (IsServer) COOPManager.GrenadeM.HandleGrenadeThrowRequest(peer, reader);
                break;
            case Op.GRENADE_SPAWN:
                if (!IsServer) COOPManager.GrenadeM.HandleGrenadeSpawn(reader);
                break;
            case Op.GRENADE_EXPLODE:
                if (!IsServer) COOPManager.GrenadeM.HandleGrenadeExplode(reader);
                break;

            case Op.ENV_HURT_REQUEST:
                if (IsServer) COOPManager.HurtM.Server_HandleEnvHurtRequest(peer, reader);
                break;
            case Op.ENV_HURT_EVENT:
                if (!IsServer) COOPManager.destructible.Client_ApplyDestructibleHurt(reader);
                break;
            case Op.ENV_DEAD_EVENT:
                if (!IsServer) COOPManager.destructible.Client_ApplyDestructibleDead(reader);
                break;

            case Op.SCENE_READY:
                {
                    var id = reader.GetString(); // 发送者 id（EndPoint）
                    var sid = reader.GetString(); // SceneId（string）
                    var pos = reader.GetVector3(); // 初始位置
                    var rot = reader.GetQuaternion();
                    var face = reader.GetString();

                    if (IsServer) SceneNet.Instance.Server_HandleSceneReady(peer, id, sid, pos, rot, face);
                    // 客户端若收到这条（主机广播），实际创建工作由 REMOTE_CREATE 完成，这里不处理
                    break;
                }

            case Op.LOOT_REQ_OPEN:
                {
                    if (IsServer) LootManager.Instance.Server_HandleLootOpenRequest(peer, reader);
                    break;
                }


            case Op.LOOT_STATE:
                {
                    if (IsServer) break;
                    COOPManager.LootNet.Client_ApplyLootboxState(reader);

                    break;
                }
            case Op.LOOT_REQ_PUT:
                {
                    if (!IsServer) break;
                    COOPManager.LootNet.Server_HandleLootPutRequest(peer, reader);
                    break;
                }
            case Op.LOOT_REQ_TAKE:
                {
                    if (!IsServer) break;
                    COOPManager.LootNet.Server_HandleLootTakeRequest(peer, reader);
                    break;
                }
            case Op.LOOT_PUT_OK:
                {
                    if (IsServer) break;
                    COOPManager.LootNet.Client_OnLootPutOk(reader);
                    break;
                }
            case Op.LOOT_TAKE_OK:
                {
                    if (IsServer) break;
                    COOPManager.LootNet.Client_OnLootTakeOk(reader);
                    break;
                }

            case Op.LOOT_DENY:
                {
                    if (IsServer) break;
                    var reason = reader.GetString();
                    Debug.LogWarning($"[LOOT] 请求被拒绝：{reason}");

                    // no_inv 不要立刻重试，避免请求风暴
                    if (reason == "no_inv")
                        break;

                    // 其它可恢复类错误（如 rm_fail/bad_snapshot）再温和地刷新一次
                    var lv = LootView.Instance;
                    var inv = lv ? lv.TargetInventory : null;
                    if (inv) COOPManager.LootNet.Client_RequestLootState(inv);
                    break;
                }


            case Op.LOOT_REQ_SPLIT:
                {
                    if (!IsServer) break;
                    COOPManager.LootNet.Server_HandleLootSplitRequest(peer, reader);
                    break;
                }

            case Op.REMOTE_DESPAWN:
                {
                    if (IsServer) break; // 只客户端处理
                    var id = reader.GetString();
                    if (clientRemoteCharacters.TryGetValue(id, out var go) && go)
                        Destroy(go);
                    clientRemoteCharacters.Remove(id);
                    break;
                }


            case Op.DOOR_REQ_SET:
                {
                    if (IsServer) COOPManager.Door.Server_HandleDoorSetRequest(peer, reader);
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
                    if (IsServer) COOPManager.LootNet.Server_HandleLootSlotUnplugRequest(peer, reader);
                    break;
                }
            case Op.LOOT_REQ_SLOT_PLUG:
                {
                    if (IsServer) COOPManager.LootNet.Server_HandleLootSlotPlugRequest(peer, reader);
                    break;
                }


            case Op.SCENE_GATE_READY:
                {
                    if (IsServer)
                    {
                        var pid = reader.GetString();
                        var sid = reader.GetString();

                        // 若主机还没确定 gate 的 sid，就用第一次 READY 的 sid
                        if (string.IsNullOrEmpty(SceneNet.Instance._srvGateSid))
                            SceneNet.Instance._srvGateSid = sid;

                        if (sid == SceneNet.Instance._srvGateSid) SceneNet.Instance._srvGateReadyPids.Add(pid);
                    }

                    break;
                }

            case Op.SCENE_GATE_RELEASE:
                {
                    if (!IsServer)
                    {
                        var sid = reader.GetString();
                        // 允许首次对齐或服务端/客户端估算不一致的情况
                        if (string.IsNullOrEmpty(SceneNet.Instance._cliGateSid) || sid == SceneNet.Instance._cliGateSid)
                        {
                            SceneNet.Instance._cliGateSid = sid;
                            SceneNet.Instance._cliSceneGateReleased = true;
                        }
                        else
                        {
                            Debug.LogWarning($"[GATE] release sid mismatch: srv={sid}, cli={SceneNet.Instance._cliGateSid} — accepting");
                            SceneNet.Instance._cliGateSid = sid; // 对齐后仍放行
                            SceneNet.Instance._cliSceneGateReleased = true;
                        }
                    }

                    break;
                }


        }

        reader.Recycle();
    }

    private void OnSceneLoaded_IndexDestructibles(Scene s, LoadSceneMode m)
    {
        if (!networkStarted) return;
        COOPManager.destructible.BuildDestructibleIndex();
        COOPManager.ExplosiveBarrels.BuildIndex();

        HealthTool._cliHookedSelf = false;

        if (!IsServer)
        {
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
        COOPManager.ExplosiveBarrels.BuildIndex();
      
    }

    private CharacterMainControl ResolveAttacker(string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
            return CharacterMainControl.Main;

        if (NetService.Instance != null && NetService.Instance.IsSelfId(playerId))
            return CharacterMainControl.Main;

        if (IsServer && playerStatuses != null)
        {
            foreach (var kv in playerStatuses)
            {
                if (kv.Value != null && kv.Value.EndPoint == playerId)
                {
                    if (remoteCharacters.TryGetValue(kv.Key, out var proxy) && proxy)
                        return proxy.GetComponent<CharacterMainControl>();
                    break;
                }
            }
        }

        return null;
    }

    public struct Pending
    {
        public Inventory inv;
        public int srcPos;
        public int count;
    }

    public void AddPhantomMapMarker(GameObject phantom, string playerName)
    {
        try
        {
            var pointOfInterest = phantom.AddComponent<Duckov.MiniMaps.SimplePointOfInterest>();

            PhantomPlayerNames[phantom] = playerName;

            Debug.Log($"[联机幻影] 已为幻影 {playerName} 添加地图标记");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[联机幻影] 添加地图标记失败: {ex.Message}");
        }
    }
}