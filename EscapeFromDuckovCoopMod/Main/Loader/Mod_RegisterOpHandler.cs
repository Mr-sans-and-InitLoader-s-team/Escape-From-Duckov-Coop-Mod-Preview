using Duckov.UI;
using EscapeFromDuckovCoopMod.Net;
using EscapeFromDuckovCoopMod.Utils;
using EscapeFromDuckovCoopMod.Utils.NetHelper;
using UnityEngine.SceneManagement;
using static EscapeFromDuckovCoopMod.LootNet;

namespace EscapeFromDuckovCoopMod;

public partial class ModBehaviourF
{
    /// <summary>
    /// 注册 OP 处理器<br/>
    /// 若是使用Unity API的操作请注册到主线程<br/>
    /// 在注册到后台线程前请先经过详细考虑和测试!!!
    /// </summary>
    private void RegisterOpHandlers()
    {
        var consumer = NetMessageConsumer.Instance;

        // ===== 主机 -> 客户端：下发全量玩家状态 =====
        consumer.RegisterMainThreadHandler(Op.PLAYER_STATUS_UPDATE, HandlePlayerStatusUpdate);

        // ===== 客户端 -> 主机：上报自身状态 =====
        consumer.RegisterMainThreadHandler(Op.CLIENT_STATUS_UPDATE, HandleClientStatusUpdate);

        // ===== 位置信息（量化版本）=====
        consumer.RegisterMainThreadHandler(Op.POSITION_UPDATE, HandlePositionUpdate);

        // ===== 动画同步 =====
        consumer.RegisterMainThreadHandler(Op.ANIM_SYNC, HandleAnimSync);

        // ===== 装备更新 =====
        consumer.RegisterMainThreadHandler(Op.EQUIPMENT_UPDATE, HandleEquipmentUpdate);

        // ===== 武器更新 =====
        consumer.RegisterMainThreadHandler(Op.PLAYERWEAPON_UPDATE, HandleWeaponUpdate);

        // ===== 开火请求/事件 =====
        consumer.RegisterMainThreadHandler(Op.FIRE_REQUEST, HandleFireRequest);
        consumer.RegisterMainThreadHandler(Op.FIRE_EVENT, HandleFireEvent);

        // ===== JSON 消息 =====
        consumer.RegisterMainThreadHandler(Op.JSON, HandleJsonMessage);

        // ===== 手榴弹相关 =====
        consumer.RegisterMainThreadHandler(Op.GRENADE_THROW_REQUEST, HandleGrenadeThrowRequest);
        consumer.RegisterMainThreadHandler(Op.GRENADE_SPAWN, HandleGrenadeSpawn);
        consumer.RegisterMainThreadHandler(Op.GRENADE_EXPLODE, HandleGrenadeExplode);

        // ===== 物品掉落/拾取 =====
        consumer.RegisterMainThreadHandler(Op.ITEM_DROP_REQUEST, HandleItemDropRequest);
        consumer.RegisterMainThreadHandler(Op.ITEM_SPAWN, HandleItemSpawn);
        consumer.RegisterMainThreadHandler(Op.ITEM_PICKUP_REQUEST, HandleItemPickupRequest);
        consumer.RegisterMainThreadHandler(Op.ITEM_DESPAWN, HandleItemDespawn);

        // ===== 近战攻击 =====
        consumer.RegisterMainThreadHandler(Op.MELEE_ATTACK_REQUEST, HandleMeleeAttackRequest);
        consumer.RegisterMainThreadHandler(Op.MELEE_ATTACK_SWING, HandleMeleeAttackSwing);
        consumer.RegisterMainThreadHandler(Op.MELEE_HIT_REPORT, HandleMeleeHitReport);

        // ===== 环境伤害/破坏 =====
        consumer.RegisterMainThreadHandler(Op.ENV_HURT_REQUEST, HandleEnvHurtRequest);
        consumer.RegisterMainThreadHandler(Op.ENV_HURT_EVENT, HandleEnvHurtEvent);
        consumer.RegisterMainThreadHandler(Op.ENV_DEAD_EVENT, HandleEnvDeadEvent);

        // ===== 玩家血量同步 =====
        consumer.RegisterMainThreadHandler(Op.PLAYER_HEALTH_REPORT, HandlePlayerHealthReport);
        consumer.RegisterMainThreadHandler(Op.AUTH_HEALTH_SELF, HandleAuthHealthSelf);
        consumer.RegisterMainThreadHandler(Op.AUTH_HEALTH_REMOTE, HandleAuthHealthRemote);
        consumer.RegisterMainThreadHandler(Op.PLAYER_HURT_EVENT, HandlePlayerHurtEvent);

        // ===== Buff 相关 =====
        consumer.RegisterMainThreadHandler(Op.PLAYER_BUFF_SELF_APPLY, HandlePlayerBuffSelfApply);
        consumer.RegisterMainThreadHandler(Op.HOST_BUFF_PROXY_APPLY, HandleHostBuffProxyApply);

        // ===== 场景投票/切换 =====
        consumer.RegisterMainThreadHandler(Op.SCENE_VOTE_START, HandleSceneVoteStart);
        consumer.RegisterMainThreadHandler(Op.SCENE_VOTE_REQ, HandleSceneVoteReq);
        consumer.RegisterMainThreadHandler(Op.SCENE_READY_SET, HandleSceneReadySet);
        consumer.RegisterMainThreadHandler(Op.SCENE_BEGIN_LOAD, HandleSceneBeginLoad);
        consumer.RegisterMainThreadHandler(Op.SCENE_CANCEL, HandleSceneCancel);
        consumer.RegisterMainThreadHandler(Op.SCENE_READY, HandleSceneReady);
        consumer.RegisterMainThreadHandler(Op.SCENE_GATE_READY, HandleSceneGateReady);
        consumer.RegisterMainThreadHandler(Op.SCENE_GATE_RELEASE, HandleSceneGateRelease);

        // ===== 玩家外观 =====
        consumer.RegisterMainThreadHandler(Op.PLAYER_APPEARANCE, HandlePlayerAppearance);

        // ===== 环境同步 =====
        consumer.RegisterMainThreadHandler(Op.ENV_SYNC_REQUEST, HandleEnvSyncRequest);
        consumer.RegisterMainThreadHandler(Op.ENV_SYNC_STATE, HandleEnvSyncState);

        // ===== 战利品箱相关 =====
        consumer.RegisterMainThreadHandler(Op.LOOT_REQ_OPEN, HandleLootReqOpen);
        consumer.RegisterMainThreadHandler(Op.LOOT_STATE, HandleLootState);
        consumer.RegisterMainThreadHandler(Op.LOOT_REQ_PUT, HandleLootReqPut);
        consumer.RegisterMainThreadHandler(Op.LOOT_REQ_TAKE, HandleLootReqTake);
        consumer.RegisterMainThreadHandler(Op.LOOT_PUT_OK, HandleLootPutOk);
        consumer.RegisterMainThreadHandler(Op.LOOT_TAKE_OK, HandleLootTakeOk);
        consumer.RegisterMainThreadHandler(Op.LOOT_DENY, HandleLootDeny);
        consumer.RegisterMainThreadHandler(Op.LOOT_REQ_SPLIT, HandleLootReqSplit);
        consumer.RegisterMainThreadHandler(Op.LOOT_REQ_SLOT_UNPLUG, HandleLootReqSlotUnplug);
        consumer.RegisterMainThreadHandler(Op.LOOT_REQ_SLOT_PLUG, HandleLootReqSlotPlug);

        // ===== AI 相关 =====
        consumer.RegisterMainThreadHandler(Op.AI_SEED_SNAPSHOT, HandleAiSeedSnapshot);
        consumer.RegisterMainThreadHandler(Op.AI_LOADOUT_SNAPSHOT, HandleAiLoadoutSnapshot);
        consumer.RegisterMainThreadHandler(Op.AI_TRANSFORM_SNAPSHOT, HandleAiTransformSnapshot);
        consumer.RegisterMainThreadHandler(Op.AI_ANIM_SNAPSHOT, HandleAiAnimSnapshot);
        consumer.RegisterMainThreadHandler(Op.AI_ATTACK_SWING, HandleAiAttackSwing);
        consumer.RegisterMainThreadHandler(Op.AI_HEALTH_SYNC, HandleAiHealthSync);
        consumer.RegisterMainThreadHandler(Op.AI_HEALTH_REPORT, HandleAiHealthReport);
        consumer.RegisterMainThreadHandler(Op.AI_NAME_ICON, HandleAiNameIcon);
        consumer.RegisterMainThreadHandler(Op.AI_SEED_PATCH, HandleAiSeedPatch);

        // ===== 死亡掉落 =====
        consumer.RegisterMainThreadHandler(Op.DEAD_LOOT_SPAWN, HandleDeadLootSpawn);
        consumer.RegisterMainThreadHandler(Op.PLAYER_DEAD_TREE, HandlePlayerDeadTree);

        // ===== 音频事件 =====
        consumer.RegisterMainThreadHandler(Op.AUDIO_EVENT, HandleAudioEvent);

        // ===== 门状态 =====
        consumer.RegisterMainThreadHandler(Op.DOOR_REQ_SET, HandleDoorReqSet);
        consumer.RegisterMainThreadHandler(Op.DOOR_STATE, HandleDoorState);

        // ===== 远程角色销毁 =====
        consumer.RegisterMainThreadHandler(Op.REMOTE_DESPAWN, HandleRemoteDespawn);
    }

    #region 消息处理器实现

    private void HandlePlayerStatusUpdate(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
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

                var equipmentCount = reader.GetInt();
                var equipmentList = new List<EquipmentSyncData>();
                for (var j = 0; j < equipmentCount; j++)
                    equipmentList.Add(EquipmentSyncData.Deserialize(reader));

                var weaponCount = reader.GetInt();
                var weaponList = new List<WeaponSyncData>();
                for (var j = 0; j < weaponCount; j++)
                    weaponList.Add(WeaponSyncData.Deserialize(reader));

                if (NetService.Instance.IsSelfId(endPoint))
                {
                    if (localPlayerStatus != null)
                        localPlayerStatus.Latency = latency;
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
                st.EquipmentList = equipmentList;
                st.WeaponList = weaponList;

                if (!string.IsNullOrEmpty(sceneId))
                {
                    st.SceneId = sceneId;
                    SceneNet.Instance._cliLastSceneIdByPlayer[endPoint] = sceneId;
                }

                if (clientRemoteCharacters.TryGetValue(st.EndPoint, out var existing) && existing != null)
                    CustomFace.Client_ApplyFaceIfAvailable(st.EndPoint, existing, st.CustomFaceJson);

                if (isInGame)
                {
                    if (!clientRemoteCharacters.ContainsKey(endPoint) || clientRemoteCharacters[endPoint] == null)
                    {
                        var faceJson = st.CustomFaceJson ?? string.Empty;
                        CreateRemoteCharacter.CreateRemoteCharacterForClient(endPoint, position, rotation, faceJson).Forget();
                    }
                    else
                    {
                        var go = clientRemoteCharacters[endPoint];
                        var ni = NetInterpUtil.Attach(go);
                        ni?.Push(st.Position, st.Rotation);
                    }

                    foreach (var e in equipmentList)
                        COOPManager.ClientPlayer_Apply.ApplyEquipmentUpdate_Client(endPoint, e.SlotHash, e.ItemId).Forget();
                    foreach (var w in weaponList)
                        COOPManager.ClientPlayer_Apply.ApplyWeaponUpdate_Client(endPoint, w.SlotHash, w.ItemId).Forget();
                }
            }
        }
    }

    private void HandleClientStatusUpdate(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            COOPManager.ClientHandle.HandleClientStatusUpdate(peer, reader);
    }

    private void HandlePositionUpdate(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
        {
            var endPointC = reader.GetString();
            var posS = reader.GetV3cm();
            var dirS = reader.GetDir();
            var rotS = Quaternion.LookRotation(dirS, Vector3.up);
            COOPManager.PublicHandleUpdate.HandlePositionUpdate_Q(peer, endPointC, posS, rotS);
        }
        else
        {
            var endPointS = reader.GetString();
            var posS = reader.GetV3cm();
            var dirS = reader.GetDir();
            var rotS = Quaternion.LookRotation(dirS, Vector3.up);

            if (NetService.Instance.IsSelfId(endPointS))
                return;

            if (float.IsNaN(posS.x) || float.IsNaN(posS.y) || float.IsNaN(posS.z) ||
                float.IsInfinity(posS.x) || float.IsInfinity(posS.y) || float.IsInfinity(posS.z))
                return;

            if (!clientPlayerStatuses.TryGetValue(endPointS, out var st))
                st = clientPlayerStatuses[endPointS] = new PlayerStatus { EndPoint = endPointS, IsInGame = true };

            st.Position = posS;
            st.Rotation = rotS;

            if (clientRemoteCharacters.TryGetValue(endPointS, out var go) && go != null)
            {
                var ni = NetInterpUtil.Attach(go);
                ni?.Push(st.Position, st.Rotation);

                var cmc = go.GetComponentInChildren<CharacterMainControl>(true);
                if (cmc && cmc.modelRoot)
                {
                    var e = st.Rotation.eulerAngles;
                    cmc.modelRoot.transform.rotation = Quaternion.Euler(0f, e.y, 0f);
                }
            }
            else
            {
                CreateRemoteCharacter.CreateRemoteCharacterForClient(endPointS, posS, rotS, st.CustomFaceJson).Forget();
            }
        }
    }

    private void HandleAnimSync(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
        {
            COOPManager.PublicHandleUpdate.HandleClientAnimationStatus(peer, reader);
        }
        else
        {
            var playerId = reader.GetString();
            if (NetService.Instance.IsSelfId(playerId))
                return;

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
                ai?.Push(new AnimSample
                {
                    speed = moveSpeed,
                    dirX = moveDirX,
                    dirY = moveDirY,
                    dashing = isDashing,
                    attack = isAttacking,
                    hand = handState,
                    gunReady = gunReady,
                    stateHash = stateHash,
                    normTime = normTime
                });
            }
        }
    }

    private void HandleEquipmentUpdate(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
        {
            COOPManager.PublicHandleUpdate.HandleEquipmentUpdate(peer, reader);
        }
        else
        {
            var endPoint = reader.GetString();
            if (NetService.Instance.IsSelfId(endPoint))
                return;
            var slotHash = reader.GetInt();
            var itemId = reader.GetString();
            COOPManager.ClientPlayer_Apply.ApplyEquipmentUpdate_Client(endPoint, slotHash, itemId).Forget();
        }
    }

    private void HandleWeaponUpdate(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
        {
            COOPManager.PublicHandleUpdate.HandleWeaponUpdate(peer, reader);
        }
        else
        {
            var endPoint = reader.GetString();
            if (NetService.Instance.IsSelfId(endPoint))
                return;
            var slotHash = reader.GetInt();
            var itemId = reader.GetString();
            COOPManager.ClientPlayer_Apply.ApplyWeaponUpdate_Client(endPoint, slotHash, itemId).Forget();
        }
    }

    private void HandleFireRequest(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            COOPManager.WeaponHandle.HandleFireRequest(peer, reader);
    }

    private void HandleFireEvent(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
            COOPManager.WeaponHandle.HandleFireEvent(reader);
    }

    private void HandleJsonMessage(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        JsonMessageRouter.HandleJsonMessage(reader, peer);
    }

    private void HandleGrenadeThrowRequest(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            COOPManager.GrenadeM.HandleGrenadeThrowRequest(peer, reader);
    }

    private void HandleGrenadeSpawn(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
            COOPManager.GrenadeM.HandleGrenadeSpawn(reader);
    }

    private void HandleGrenadeExplode(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
            COOPManager.GrenadeM.HandleGrenadeExplode(reader);
    }

    private void HandleItemDropRequest(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            COOPManager.ItemHandle.HandleItemDropRequest(peer, reader);
    }

    private void HandleItemSpawn(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
            COOPManager.ItemHandle.HandleItemSpawn(reader);
    }

    private void HandleItemPickupRequest(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            COOPManager.ItemHandle.HandleItemPickupRequest(peer, reader);
    }

    private void HandleItemDespawn(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
            COOPManager.ItemHandle.HandleItemDespawn(reader);
    }

    private void HandleMeleeAttackRequest(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            COOPManager.WeaponHandle.HandleMeleeAttackRequest(peer, reader);
    }

    private void HandleMeleeAttackSwing(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
        {
            var shooter = reader.GetString();
            var delay = reader.GetFloat();

            if (!NetService.Instance.IsSelfId(shooter) && clientRemoteCharacters.TryGetValue(shooter, out var who) && who)
            {
                var anim = who.GetComponentInChildren<CharacterAnimationControl_MagicBlend>(true);
                if (anim != null)
                    anim.OnAttack();

                var cmc = who.GetComponent<CharacterMainControl>();
                var model = cmc ? cmc.characterModel : null;
                if (model)
                    MeleeFx.SpawnSlashFx(model);
            }
            else if (shooter.StartsWith("AI:"))
            {
                if (int.TryParse(shooter.Substring(3), out var aiId) && AITool.aiById.TryGetValue(aiId, out var cmc) && cmc)
                {
                    var anim = cmc.GetComponentInChildren<CharacterAnimationControl_MagicBlend>(true);
                    if (anim != null)
                        anim.OnAttack();

                    var model = cmc.characterModel;
                    if (model)
                        MeleeFx.SpawnSlashFx(model);
                }
            }
        }
    }

    private void HandleMeleeHitReport(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            COOPManager.WeaponHandle.HandleMeleeHitReport(peer, reader);
    }

    private void HandleEnvHurtRequest(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            COOPManager.HurtM.Server_HandleEnvHurtRequest(peer, reader);
    }

    private void HandleEnvHurtEvent(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
            COOPManager.destructible.Client_ApplyDestructibleHurt(reader);
    }

    private void HandleEnvDeadEvent(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
            COOPManager.destructible.Client_ApplyDestructibleDead(reader);
    }

    private void HandlePlayerHealthReport(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
        {
            var max = reader.GetFloat();
            var cur = reader.GetFloat();
            var playerId = Service.GetPlayerId(peer);

            if (max <= 0f)
            {
                Debug.LogWarning($"[HP_RECEIVE] ⚠️ 收到无效血量，缓存: 玩家={playerId}, max={max}, cur={cur}");
                HealthTool._srvPendingHp[peer] = (max, cur);
                return;
            }

            if (remoteCharacters != null && remoteCharacters.TryGetValue(peer, out var go) && go)
            {
                HealthM.Instance.ApplyHealthAndEnsureBar(go, max, cur);
                var h = go.GetComponentInChildren<Health>(true);
                if (h)
                    HealthM.Instance.Server_OnHealthChanged(peer, h);
            }
            else
            {
                Debug.LogWarning($"[HP_RECEIVE] ⚠️ 远程角色未创建，缓存血量: 玩家={playerId}");
                HealthTool._srvPendingHp[peer] = (max, cur);
            }
        }
    }

    private void HandleAuthHealthSelf(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        var max = reader.GetFloat();
        var cur = reader.GetFloat();

        if (max <= 0f)
        {
            CoopTool._cliSelfHpMax = max;
            CoopTool._cliSelfHpCur = cur;
            CoopTool._cliSelfHpPending = true;
            return;
        }

        var shouldApply = true;
        try
        {
            var main = CharacterMainControl.Main;
            var selfH = main ? main.Health : null;
            if (selfH)
            {
                var localCur = selfH.CurrentHealth;
                if (Time.time - HealthTool._cliLastSelfHurtAt <= SELF_ACCEPT_WINDOW)
                    if (cur > localCur + 0.0001f)
                    {
                        Debug.Log($"[HP][SelfEcho] drop stale echo in window: local={localCur:F3} srv={cur:F3}");
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
                            try { h.autoInit = false; } catch { }
                            HealthTool.BindHealthToCharacter(h, cmc);
                            HealthM.Instance.ForceSetHealth(h, max, cur);
                        }
                    }
                    CoopTool._cliSelfHpPending = false;
                }
            }
        }
        finally
        {
            HealthM.Instance._cliApplyingSelfSnap = false;
        }
    }

    private void HandleAuthHealthRemote(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
        {
            var playerId = reader.GetString();
            var max = reader.GetFloat();
            var cur = reader.GetFloat();

            if (max <= 0f)
            {
                CoopTool._cliPendingRemoteHp[playerId] = (max, cur);
                return;
            }

            if (clientRemoteCharacters != null && clientRemoteCharacters.TryGetValue(playerId, out var go) && go)
                HealthM.Instance.ApplyHealthAndEnsureBar(go, max, cur);
            else
                CoopTool._cliPendingRemoteHp[playerId] = (max, cur);
        }
    }

    private void HandlePlayerBuffSelfApply(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
            COOPManager.Buff.HandlePlayerBuffSelfApply(reader);
    }

    private void HandleHostBuffProxyApply(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
            COOPManager.Buff.HandleBuffProxyApply(reader);
    }

    private void HandleSceneVoteStart(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
        {
            SceneNet.Instance.Client_OnSceneVoteStart(reader);
            if (Spectator.Instance._spectatorActive)
                Spectator.Instance._spectatorEndOnVotePending = true;
        }
    }

    private void HandleSceneVoteReq(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
        {
            var targetId = reader.GetString();
            var flags = reader.GetByte();
            bool hasCurtain, useLoc, notifyEvac, saveToFile;
            PackFlag.UnpackFlags(flags, out hasCurtain, out useLoc, out notifyEvac, out saveToFile);

            string curtainGuid = null;
            if (hasCurtain)
                SceneNet.TryGetString(reader, out curtainGuid);
            if (!SceneNet.TryGetString(reader, out var locName))
                locName = string.Empty;

            if (Spectator.Instance._spectatorActive)
                Spectator.Instance._spectatorEndOnVotePending = true;

            SceneNet.Instance.Host_BeginSceneVote_Simple(targetId, curtainGuid, notifyEvac, saveToFile, useLoc, locName);
        }
    }

    private void HandleSceneReadySet(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
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

            if (!SceneNet.Instance.sceneReady.ContainsKey(localPid) && SceneNet.Instance.sceneParticipantIds.Contains(localPid))
                SceneNet.Instance.sceneReady[localPid] = false;

            if (SceneNet.Instance.sceneReady.ContainsKey(localPid))
            {
                SceneNet.Instance.sceneReady[localPid] = rdy;
                Debug.Log($"[SCENE] READY_SET -> {localPid} (srv='{pid}') = {rdy}");
            }
            else
            {
                Debug.LogWarning($"[SCENE] READY_SET for unknown pid '{pid}'. participants=[{string.Join(",", SceneNet.Instance.sceneParticipantIds)}]");
            }
        }
    }

    private void HandleSceneBeginLoad(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
        {
            if (Utils.AsyncMessageQueue.Instance != null)
            {
                Utils.AsyncMessageQueue.Instance.EnableBulkMode();
                Debug.Log("[SCENE_LOAD] ✅ 客户端：启用异步消息队列批量模式，准备接收场景数据");
            }

            if (Spectator.Instance._spectatorActive && Spectator.Instance._spectatorEndOnVotePending)
            {
                Spectator.Instance._spectatorEndOnVotePending = false;
                SceneNet.Instance.sceneVoteActive = false;
                SceneNet.Instance.sceneReady.Clear();
                SceneNet.Instance.localReady = false;
                Spectator.Instance.EndSpectatorAndShowClosure();
                return;
            }

            SceneNet.Instance.Client_OnBeginSceneLoad(reader);
        }
    }

    private void HandleSceneCancel(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
        {
            SceneNet.Instance.Client_OnVoteCancelled();
            Debug.Log("[COOP] 收到服务器取消投票通知");
        }
        else
        {
            SceneNet.Instance.sceneVoteActive = false;
            SceneNet.Instance.sceneReady.Clear();
            SceneNet.Instance.localReady = false;
            EscapeFromDuckovCoopMod.Utils.SceneTriggerResetter.ResetAllSceneTriggers();
        }

        if (Spectator.Instance._spectatorActive && Spectator.Instance._spectatorEndOnVotePending)
        {
            Spectator.Instance._spectatorEndOnVotePending = false;
            Spectator.Instance.EndSpectatorAndShowClosure();
        }
    }

    private void HandleSceneReady(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        var id = reader.GetString();
        var sid = reader.GetString();
        var pos = reader.GetVector3();
        var rot = reader.GetQuaternion();

        if (IsServer)
            SceneNet.Instance.Server_HandleSceneReady(peer, id, sid, pos, rot);
    }

    private void HandlePlayerAppearance(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        var playerId = reader.GetString();
        var faceJson = reader.GetString();

        if (IsServer)
        {
            if (peer != null && playerStatuses.TryGetValue(peer, out var status))
            {
                status.CustomFaceJson = faceJson;

                var w = new NetDataWriter();
                w.Put((byte)Op.PLAYER_APPEARANCE);
                w.Put(playerId);
                w.Put(faceJson);
                netManager?.SendSmartExcept(w, Op.PLAYER_APPEARANCE, peer);
            }
        }
        else
        {
            if (clientPlayerStatuses.TryGetValue(playerId, out var status))
            {
                status.CustomFaceJson = faceJson;
            }
            else
            {
                CustomFace._cliPendingFace[playerId] = faceJson;
            }

            if (clientRemoteCharacters.TryGetValue(playerId, out var go) && go != null)
            {
                CustomFace.Client_ApplyFaceIfAvailable(playerId, go, faceJson);
            }
        }
    }

    private void HandleEnvSyncRequest(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            COOPManager.Weather.Server_BroadcastEnvSync(peer);
    }

    private void HandleEnvSyncState(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
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
            try { lootCount = reader.GetInt(); } catch { lootCount = 0; }

            var vis = new Dictionary<int, bool>(lootCount);
            for (var i = 0; i < lootCount; ++i)
            {
                var k = 0;
                var on = false;
                try { k = reader.GetInt(); } catch { }
                try { on = reader.GetBool(); } catch { }
                vis[k] = on;
            }

            Client_ApplyLootVisibility(vis);

            var doorCount = 0;
            try { doorCount = reader.GetInt(); } catch { doorCount = 0; }

            for (var i = 0; i < doorCount; ++i)
            {
                var dk = 0;
                var cl = false;
                try { dk = reader.GetInt(); } catch { }
                try { cl = reader.GetBool(); } catch { }
                COOPManager.Door.Client_ApplyDoorState(dk, cl);
            }

            var deadCount = 0;
            try { deadCount = reader.GetInt(); } catch { deadCount = 0; }

            for (var i = 0; i < deadCount; ++i)
            {
                uint did = 0;
                try { did = reader.GetUInt(); } catch { }
                if (did != 0)
                    COOPManager.destructible.Client_ApplyDestructibleDead_Snapshot(did);
            }

            COOPManager.Weather.Client_ApplyEnvSync(day, sec, scale, seed, forceW, forceWVal, curWeather, stormLv);
        }
    }

    private void HandleLootReqOpen(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            LootManager.Instance.Server_HandleLootOpenRequest(peer, reader);
    }

    private void HandleLootState(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            return;

        //if (Utils.AsyncMessageQueue.Instance != null)
        //{
        //    Utils.AsyncMessageQueue.Instance.EnqueueMessage(
        //        (LiteNetLib.Utils.NetDataReader r) => COOPManager.LootNet.Client_ApplyLootboxState(r),
        //        reader
        //    );
        //}
        //else
        //{
        COOPManager.LootNet.Client_ApplyLootboxState(reader);
        //}
    }

    private void HandleLootReqPut(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
            return;
        COOPManager.LootNet.Server_HandleLootPutRequest(peer, reader);
    }

    private void HandleLootReqTake(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
            return;
        COOPManager.LootNet.Server_HandleLootTakeRequest(peer, reader);
    }

    private void HandleLootPutOk(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            return;
        COOPManager.LootNet.Client_OnLootPutOk(reader);
    }

    private void HandleLootTakeOk(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            return;
        COOPManager.LootNet.Client_OnLootTakeOk(reader);
    }

    private void HandleLootDeny(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            return;
        var reason = reader.GetString();
        Debug.LogWarning($"[LOOT] 请求被拒绝：{reason}");

        if (reason == "no_inv")
            return;

        var lv = LootView.Instance;
        var inv = lv ? lv.TargetInventory : null;
        if (inv)
            COOPManager.LootNet.Client_RequestLootState(inv);
    }

    private void HandleAiSeedSnapshot(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
            COOPManager.AIHandle.HandleAiSeedSnapshot(reader);
    }

    private void HandleAiLoadoutSnapshot(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
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
            return;

        COOPManager.AIHandle.Client_OnAiLoadoutReceived();

        if (LogAiLoadoutDebug)
            Debug.Log($"[AI-RECV] ver={ver} aiId={aiId} model='{modelName}' icon={iconType} showName={showName} faceLen={(faceJson != null ? faceJson.Length : 0)}");

        if (AITool.aiById.TryGetValue(aiId, out var cmc) && cmc)
            COOPManager.AIHandle.Client_ApplyAiLoadout(aiId, equips, weapons, faceJson, modelName, iconType, showName, displayName).Forget();
        else
            COOPManager.AIHandle.pendingAiLoadouts[aiId] = (equips, weapons, faceJson, modelName, iconType, showName, displayName);
    }

    private void HandleAiTransformSnapshot(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            return;
        var n = reader.GetInt();

        if (!AITool._aiSceneReady)
        {
            for (var i = 0; i < n; ++i)
            {
                var aiId = reader.GetInt();
                var p = reader.GetV3cm();
                var f = reader.GetDir();
                if (_pendingAiTrans.Count < 512)
                    _pendingAiTrans.Enqueue((aiId, p, f));
            }
            return;
        }

        for (var i = 0; i < n; i++)
        {
            var aiId = reader.GetInt();
            var p = reader.GetV3cm();
            var f = reader.GetDir();
            AITool.ApplyAiTransform(aiId, p, f);
        }
    }

    private void HandleAiAnimSnapshot(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
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
                    dashing = reader.GetBool()
                };
                if (!AITool.Client_ApplyAiAnim(id, st))
                    _pendingAiAnims[id] = st;
            }
        }
    }

    private void HandleAiAttackSwing(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
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
    }

    private void HandleAiHealthSync(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        var id = reader.GetInt();
        float max = 0f, cur = 0f;

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
    }

    private void HandleAiHealthReport(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            COOPManager.AIHealth.HandleAiHealthReport(peer, reader);
    }

    private void HandleDeadLootSpawn(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        var scene = reader.GetInt();
        var aiId = reader.GetInt();
        var lootUid = reader.GetInt();
        var pos = reader.GetV3cm();
        var rot = reader.GetQuaternion();

        if (SceneManager.GetActiveScene().buildIndex != scene)
            return;

        DeadLootBox.Instance.SpawnDeadLootboxAt(aiId, lootUid, pos, rot);
    }

    private void HandleAiNameIcon(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            return;

        var aiId = reader.GetInt();
        var iconType = reader.GetInt();
        var showName = reader.GetBool();
        string displayName = null;
        var hasName = reader.GetBool();
        if (hasName)
            displayName = reader.GetString();

        if (AITool.aiById.TryGetValue(aiId, out var cmc) && cmc)
            AIName.RefreshNameIconWithRetries(cmc, iconType, showName, displayName).Forget();
        else
            Debug.LogWarning("[AI_icon_Name 10s] cmc is null!");
    }

    private void HandlePlayerDeadTree(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
            return;

        var pos = reader.GetV3cm();
        var rot = reader.GetQuaternion();

        var snap = ItemTool.ReadItemSnapshot(reader);
        var tmpRoot = ItemTool.BuildItemFromSnapshot(snap);
        if (!tmpRoot)
        {
            Debug.LogWarning("[LOOT] PLAYER_DEAD_TREE BuildItemFromSnapshot failed.");
            return;
        }

        var deadPfb = LootManager.Instance.ResolveDeadLootPrefabOnServer();
        var box = InteractableLootbox.CreateFromItem(tmpRoot, pos + Vector3.up * 0.10f, rot, true, deadPfb);
        if (box)
            DeadLootBox.Instance.Server_OnDeadLootboxSpawned(box, null);

        if (remoteCharacters.TryGetValue(peer, out var proxy) && proxy)
        {
            Destroy(proxy);
            remoteCharacters.Remove(peer);
        }

        if (playerStatuses.TryGetValue(peer, out var st) && !string.IsNullOrEmpty(st.EndPoint))
        {
            var w2 = writer;
            w2.Reset();
            w2.Put((byte)Op.REMOTE_DESPAWN);
            w2.Put(st.EndPoint);
            netManager.SendSmart(w2, Op.REMOTE_DESPAWN);
        }

        if (tmpRoot && tmpRoot.gameObject)
            Destroy(tmpRoot.gameObject);
    }

    private void HandleLootReqSplit(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
            return;
        COOPManager.LootNet.Server_HandleLootSplitRequest(peer, reader);
    }

    private void HandleRemoteDespawn(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            return;
        var id = reader.GetString();
        if (clientRemoteCharacters.TryGetValue(id, out var go) && go)
            Destroy(go);
        clientRemoteCharacters.Remove(id);
    }

    private void HandleAiSeedPatch(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        COOPManager.AIHandle.HandleAiSeedPatch(reader);
    }

    private void HandleAudioEvent(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
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
    }

    private void HandleDoorReqSet(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            COOPManager.Door.Server_HandleDoorSetRequest(peer, reader);
    }

    private void HandleDoorState(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
        {
            var k = reader.GetInt();
            var cl = reader.GetBool();
            COOPManager.Door.Client_ApplyDoorState(k, cl);
        }
    }

    private void HandleLootReqSlotUnplug(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            COOPManager.LootNet.Server_HandleLootSlotUnplugRequest(peer, reader);
    }

    private void HandleLootReqSlotPlug(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
            COOPManager.LootNet.Server_HandleLootSlotPlugRequest(peer, reader);
    }

    private void HandleSceneGateReady(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (IsServer)
        {
            var pid = reader.GetString();
            var sid = reader.GetString();

            var peerEndpoint = peer != null && peer.EndPoint != null ? peer.EndPoint.ToString() : "Unknown";
            Debug.Log($"[GATE] 收到客户端举手：客户端报告的pid={pid}, peer实际地址={peerEndpoint}, sid={sid}, 当前门状态: {(SceneNet.Instance._srvSceneGateOpen ? "已开门" : "未开门")}");

            if (string.IsNullOrEmpty(SceneNet.Instance._srvGateSid))
                SceneNet.Instance._srvGateSid = sid;

            if (sid == SceneNet.Instance._srvGateSid)
            {
                if (peer != null && playerStatuses.TryGetValue(peer, out var status) && status != null)
                {
                    SceneNet.Instance._srvGateReadyPids.Add(status.EndPoint);
                    Debug.Log($"[GATE] 记录举手客户端：{status.EndPoint} (客户端报告={pid})，当前已举手: {SceneNet.Instance._srvGateReadyPids.Count} 人");

                    if (SceneNet.Instance._srvSceneGateOpen)
                    {
                        var w = new NetDataWriter();
                        w.Put((byte)Op.SCENE_GATE_RELEASE);
                        w.Put(sid ?? "");
                        peer.SendSmart(w, Op.SCENE_GATE_RELEASE);
                        Debug.Log($"[GATE] 迟到放行：{status.EndPoint}");

                        StartCoroutine(SendLootFullSyncDelayed(peer));
                    }
                }
                else
                {
                    Debug.LogWarning($"[GATE] 无法找到客户端的 playerStatus，peer={peerEndpoint}");
                }
            }
            else
            {
                Debug.LogWarning($"[GATE] sid 不匹配：客户端={sid}, 主机={SceneNet.Instance._srvGateSid}");
            }
        }
    }

    private void HandleSceneGateRelease(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
        {
            var sid = reader.GetString();
            if (string.IsNullOrEmpty(SceneNet.Instance._cliGateSid) || sid == SceneNet.Instance._cliGateSid)
            {
                SceneNet.Instance._cliGateSid = sid;
                SceneNet.Instance._cliSceneGateReleased = true;
                Debug.Log($"[GATE] ✅ 客户端收到主机放行：sid={sid}");
                HealthM.Instance.Client_ReportSelfHealth_IfReadyOnce();
            }
            else
            {
                Debug.LogWarning($"[GATE] release sid mismatch: srv={sid}, cli={SceneNet.Instance._cliGateSid} — accepting");
                SceneNet.Instance._cliGateSid = sid;
                SceneNet.Instance._cliSceneGateReleased = true;
                HealthM.Instance.Client_ReportSelfHealth_IfReadyOnce();
            }
        }
    }

    private void HandlePlayerHurtEvent(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (!IsServer)
            HealthM.Instance.Client_ApplySelfHurtFromServer(reader);
    }

    #endregion
}