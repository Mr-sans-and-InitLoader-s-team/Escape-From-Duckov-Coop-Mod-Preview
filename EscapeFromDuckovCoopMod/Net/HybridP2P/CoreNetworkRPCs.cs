using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;
using Duckov.UI;

namespace EscapeFromDuckovCoopMod.Net.HybridP2P
{
    public static class CoreNetworkRPCs
    {
        public static void RegisterAllRPCs()
        {
            Debug.Log("[CoreNetworkRPCs] RegisterAllRPCs called");
            
            var manager = HybridRPCManager.Instance;
            if (manager == null)
            {
                Debug.LogError("[CoreNetworkRPCs] HybridRPCManager.Instance is null! Retrying in 1 second...");
                UnityEngine.Object.FindObjectOfType<MonoBehaviour>()?.StartCoroutine(RetryRegisterRPCs());
                return;
            }

            try
            {
                Debug.Log("[CoreNetworkRPCs] Starting RPC registration");
                
                // 健康同步 RPCs
                manager.RegisterRPC("PlayerHealthReport", OnRPC_PlayerHealthReport);
                manager.RegisterRPC("PlayerHealthBroadcast", OnRPC_PlayerHealthBroadcast);
                
                // 位置同步 RPCs
                manager.RegisterRPC("PlayerPositionUpdate", OnRPC_PlayerPositionUpdate);
                manager.RegisterRPC("PlayerPositionBroadcast", OnRPC_PlayerPositionBroadcast);
                
                // 玩家状态同步 RPCs
                manager.RegisterRPC("PlayerStatusFullSync", OnRPC_PlayerStatusFullSync);
                manager.RegisterRPC("ClientStatusReport", OnRPC_ClientStatusReport);
                
                manager.RegisterRPC("WeaponEquipSync", OnRPC_WeaponEquipSync);
                manager.RegisterRPC("EquipmentSync", OnRPC_EquipmentSync);
                
                manager.RegisterRPC("DoorSetRequest", OnRPC_DoorSetRequest);
                manager.RegisterRPC("DoorStateSync", OnRPC_DoorStateSync);
                manager.RegisterRPC("EnvHurtRequest", OnRPC_EnvHurtRequest);
                manager.RegisterRPC("EnvHurtEvent", OnRPC_EnvHurtEvent);
                manager.RegisterRPC("EnvDeadEvent", OnRPC_EnvDeadEvent);
                manager.RegisterRPC("ItemDropRequest", OnRPC_ItemDropRequest);
                manager.RegisterRPC("ItemSpawn", OnRPC_ItemSpawn);
                manager.RegisterRPC("ItemPickupRequest", OnRPC_ItemPickupRequest);
                manager.RegisterRPC("ItemDespawn", OnRPC_ItemDespawn);
                manager.RegisterRPC("FireRequest", OnRPC_FireRequest);
                manager.RegisterRPC("FireEvent", OnRPC_FireEvent);
                manager.RegisterRPC("GrenadeThrowRequest", OnRPC_GrenadeThrowRequest);
                manager.RegisterRPC("GrenadeSpawn", OnRPC_GrenadeSpawn);
                manager.RegisterRPC("GrenadeExplode", OnRPC_GrenadeExplode);
                manager.RegisterRPC("MeleeAttackRequest", OnRPC_MeleeAttackRequest);
                manager.RegisterRPC("MeleeAttackSwing", OnRPC_MeleeAttackSwing);
                manager.RegisterRPC("MeleeHitReport", OnRPC_MeleeHitReport);
                
                manager.RegisterRPC("AnimationSyncRequest", OnRPC_AnimationSyncRequest);
                manager.RegisterRPC("AnimationSync", OnRPC_AnimationSync);
                
                manager.RegisterRPC("PlayerDeadTree", OnRPC_PlayerDeadTree);
                manager.RegisterRPC("RemoteDespawn", OnRPC_RemoteDespawn);
                
                manager.RegisterRPC("PlayerBuffSelfApply", OnRPC_PlayerBuffSelfApply);
                manager.RegisterRPC("HostBuffProxyApply", OnRPC_HostBuffProxyApply);
                
                manager.RegisterRPC("PlayerHurtEvent", OnRPC_PlayerHurtEvent);
                
                manager.RegisterRPC("EnvSyncRequest", OnRPC_EnvSyncRequest);
                manager.RegisterRPC("EnvSyncState", OnRPC_EnvSyncState);
                
                manager.RegisterRPC("SceneVoteStart", OnRPC_SceneVoteStart);
                manager.RegisterRPC("SceneVoteReq", OnRPC_SceneVoteReq);
                manager.RegisterRPC("SceneReadySet", OnRPC_SceneReadySet);
                manager.RegisterRPC("SceneBeginLoad", OnRPC_SceneBeginLoad);
                manager.RegisterRPC("SceneCancel", OnRPC_SceneCancel);
                manager.RegisterRPC("SceneReady", OnRPC_SceneReady);
                manager.RegisterRPC("SceneGateReady", OnRPC_SceneGateReady);
                manager.RegisterRPC("SceneGateRelease", OnRPC_SceneGateRelease);
                
                manager.RegisterRPC("LootReqOpen", OnRPC_LootReqOpen);
                manager.RegisterRPC("LootState", OnRPC_LootState);
                manager.RegisterRPC("LootReqPut", OnRPC_LootReqPut);
                manager.RegisterRPC("LootReqTake", OnRPC_LootReqTake);
                manager.RegisterRPC("LootPutOk", OnRPC_LootPutOk);
                manager.RegisterRPC("LootTakeOk", OnRPC_LootTakeOk);
                manager.RegisterRPC("LootDeny", OnRPC_LootDeny);
                manager.RegisterRPC("LootReqSplit", OnRPC_LootReqSplit);
                manager.RegisterRPC("LootReqSlotUnplug", OnRPC_LootReqSlotUnplug);
                manager.RegisterRPC("LootReqSlotPlug", OnRPC_LootReqSlotPlug);
                
                manager.RegisterRPC("AiSeedSnapshot", OnRPC_AiSeedSnapshot);
                manager.RegisterRPC("AiLoadoutSnapshot", OnRPC_AiLoadoutSnapshot);
                manager.RegisterRPC("AiTransformSnapshot", OnRPC_AiTransformSnapshot);
                manager.RegisterRPC("AiAnimSnapshot", OnRPC_AiAnimSnapshot);
                manager.RegisterRPC("AiAttackSwing", OnRPC_AiAttackSwing);
                manager.RegisterRPC("AiHealthSync", OnRPC_AiHealthSync);
                manager.RegisterRPC("AiHealthReport", OnRPC_AiHealthReport);
                manager.RegisterRPC("DeadLootSpawn", OnRPC_DeadLootSpawn);
                manager.RegisterRPC("AiNameIcon", OnRPC_AiNameIcon);
                manager.RegisterRPC("AiSeedPatch", OnRPC_AiSeedPatch);
                
                Debug.Log("[CoreNetworkRPCs] All core RPCs registered (64 total)");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CoreNetworkRPCs] Failed to register RPCs: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static System.Collections.IEnumerator RetryRegisterRPCs()
        {
            yield return new UnityEngine.WaitForSeconds(1f);
            Debug.Log("[CoreNetworkRPCs] Retrying RPC registration");
            RegisterAllRPCs();
        }

        #region Health Sync RPCs

        private static void OnRPC_PlayerHealthReport(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;

            if (!Net.Core.NetworkMessageValidator.ValidateHealthReport(reader, out var max, out var cur, out var sequence))
            {
                return;
            }

            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);

            if (peer == null)
            {
                Debug.LogWarning($"[RPC_PlayerHealthReport] Cannot find peer for connection {senderConnectionId}");
                return;
            }

            var endPoint = peer.EndPoint?.ToString() ?? "unknown";
            var validator = HybridP2PValidator.Instance;
            if (validator != null && !validator.ValidateHealthUpdate(endPoint, max, cur))
            {
                Debug.LogWarning($"[RPC_PlayerHealthReport] Health validation failed for {endPoint}");
                return;
            }

            HealthM.Instance?.Server_ProcessClientHealthReport(peer, max, cur, sequence);
        }

        private static void OnRPC_PlayerHealthBroadcast(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            if (!Net.Core.NetworkMessageValidator.ValidateAuthHealthRemote(reader, out var playerId, out var max, out var cur, out var sequence))
            {
                return;
            }

            if (NetService.Instance.IsSelfId(playerId))
                return;

            var snapshot = new HealthTool.HealthSnapshot(max, cur);
            var healthM = HealthM.Instance;

            if (healthM == null)
            {
                CoopTool._cliPendingRemoteHp[playerId] = (snapshot, sequence);
                return;
            }

            if (snapshot.Max <= 0f)
            {
                CoopTool._cliPendingRemoteHp[playerId] = (snapshot, sequence);
                return;
            }

            if (ModBehaviourF.Instance.clientRemoteCharacters.TryGetValue(playerId, out var go) && go != null)
            {
                healthM.Client_ApplyRemoteSnapshot(playerId, go, snapshot, sequence);
            }
            else
            {
                CoopTool._cliPendingRemoteHp[playerId] = (snapshot, sequence);
            }
        }

        #endregion

        #region Position Sync RPCs

        private static void OnRPC_PlayerPositionUpdate(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;

            if (!Net.Core.NetworkMessageValidator.ValidatePositionUpdate(reader, out var endPoint))
            {
                return;
            }

            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer == null) return;

            var posS = new UnityEngine.Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            var dirS = new UnityEngine.Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            var rotS = UnityEngine.Quaternion.LookRotation(dirS, UnityEngine.Vector3.up);

            if (!Net.Core.NetworkMessageValidator.IsValidVector(posS, "Position") ||
                !Net.Core.NetworkMessageValidator.IsValidQuaternion(rotS, "Rotation"))
            {
                return;
            }

            COOPManager.PublicHandleUpdate.HandlePositionUpdate_Q(peer, endPoint, posS, rotS);
        }

        private static void OnRPC_PlayerPositionBroadcast(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            if (!Net.Core.NetworkMessageValidator.ValidatePositionUpdate(reader, out var endPoint))
            {
                return;
            }

            if (NetService.Instance.IsSelfId(endPoint))
                return;

            var posS = new UnityEngine.Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            var dirS = new UnityEngine.Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            var rotS = UnityEngine.Quaternion.LookRotation(dirS, UnityEngine.Vector3.up);

            if (!Net.Core.NetworkMessageValidator.IsValidVector(posS, "Position") ||
                !Net.Core.NetworkMessageValidator.IsValidQuaternion(rotS, "Rotation"))
            {
                return;
            }

            if (!ModBehaviourF.Instance.clientRemoteCharacters.TryGetValue(endPoint, out var go) || go == null)
            {
                return;
            }

            var ni = NetInterpUtil.Attach(go);
            ni?.Push(posS, rotS);
        }

        #endregion

        #region Player Status Sync RPCs

        private static void OnRPC_PlayerStatusFullSync(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            var playerCount = reader.GetInt();
            var mod = ModBehaviourF.Instance;
            mod.clientPlayerStatuses.Clear();

            for (var i = 0; i < playerCount; i++)
            {
                var endPoint = reader.GetString();
                var playerName = reader.GetString();
                var latency = reader.GetInt();
                var isInGame = reader.GetBool();
                var position = new UnityEngine.Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
                var rotation = new UnityEngine.Quaternion(reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
                var sceneId = reader.GetString();
                var customFaceJson = reader.GetString();

                var equipmentCount = reader.GetInt();
                var equipmentList = new System.Collections.Generic.List<EquipmentSyncData>();
                for (var j = 0; j < equipmentCount; j++)
                {
                    var eq = EquipmentSyncData.Deserialize(reader);
                    equipmentList.Add(eq);
                }

                var weaponCount = reader.GetInt();
                var weaponList = new System.Collections.Generic.List<WeaponSyncData>();
                for (var k = 0; k < weaponCount; k++)
                {
                    var w = WeaponSyncData.Deserialize(reader);
                    weaponList.Add(w);
                }

                var natType = (NATType)reader.GetByte();
                var useRelay = reader.GetBool();

                var status = new PlayerStatus
                {
                    EndPoint = endPoint,
                    PlayerName = playerName,
                    Latency = latency,
                    IsInGame = isInGame,
                    Position = position,
                    Rotation = rotation,
                    SceneId = sceneId,
                    CustomFaceJson = customFaceJson,
                    EquipmentList = equipmentList,
                    WeaponList = weaponList,
                    NATType = natType,
                    UseRelay = useRelay
                };

                mod.clientPlayerStatuses[endPoint] = status;
            }
        }

        private static void OnRPC_ClientStatusReport(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;

            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer == null) return;

            COOPManager.ClientHandle.HandleClientStatusUpdate(peer, reader);
        }

        #endregion

        #region Weapon/Equipment Sync RPCs

        private static void OnRPC_WeaponEquipSync(long senderConnectionId, NetDataReader reader)
        {
            var endPoint = reader.GetString();
            var slotHash = reader.GetInt();
            var itemId = reader.GetString();

            if (ModBehaviourF.Instance.IsServer)
            {
                var rpcManager = HybridRPCManager.Instance;
                rpcManager?.CallRPC("WeaponEquipSync", RPCTarget.AllClients, 0, w =>
                {
                    w.Put(endPoint);
                    w.Put(slotHash);
                    w.Put(itemId);
                }, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                if (NetService.Instance.IsSelfId(endPoint)) return;
                COOPManager.ClientPlayer_Apply.ApplyWeaponUpdate_Client(endPoint, slotHash, itemId).Forget();
            }
        }

        private static void OnRPC_EquipmentSync(long senderConnectionId, NetDataReader reader)
        {
            var endPoint = reader.GetString();
            var slotHash = reader.GetInt();
            var itemId = reader.GetString();

            if (ModBehaviourF.Instance.IsServer)
            {
                var rpcManager = HybridRPCManager.Instance;
                rpcManager?.CallRPC("EquipmentSync", RPCTarget.AllClients, 0, w =>
                {
                    w.Put(endPoint);
                    w.Put(slotHash);
                    w.Put(itemId);
                }, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                if (NetService.Instance.IsSelfId(endPoint)) return;
                COOPManager.ClientPlayer_Apply.ApplyEquipmentUpdate_Client(endPoint, slotHash, itemId).Forget();
            }
        }

        #endregion

        #region Scene Event RPCs

        private static void OnRPC_DoorSetRequest(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;

            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer == null) return;

            COOPManager.Door.Server_HandleDoorSetRequest(peer, reader);
        }

        private static void OnRPC_DoorStateSync(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            var k = reader.GetInt();
            var cl = reader.GetBool();
            COOPManager.Door.Client_ApplyDoorState(k, cl);
        }

        private static void OnRPC_EnvHurtRequest(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;

            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer == null) return;

            COOPManager.HurtM.Server_HandleEnvHurtRequest(peer, reader);
        }

        private static void OnRPC_EnvHurtEvent(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            COOPManager.destructible.Client_ApplyDestructibleHurt(reader);
        }

        private static void OnRPC_EnvDeadEvent(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            COOPManager.destructible.Client_ApplyDestructibleDead(reader);
        }

        private static void OnRPC_ItemDropRequest(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;

            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer == null) return;

            COOPManager.ItemHandle.HandleItemDropRequest(peer, reader);
        }

        private static void OnRPC_ItemSpawn(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            COOPManager.ItemHandle.HandleItemSpawn(reader);
        }

        private static void OnRPC_ItemPickupRequest(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;

            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer == null) return;

            COOPManager.ItemHandle.HandleItemPickupRequest(peer, reader);
        }

        private static void OnRPC_ItemDespawn(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            COOPManager.ItemHandle.HandleItemDespawn(reader);
        }

        private static void OnRPC_FireRequest(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;

            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer == null) return;

            COOPManager.WeaponHandle.HandleFireRequest(peer, reader);
        }

        private static void OnRPC_FireEvent(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            COOPManager.WeaponHandle.HandleFireEvent(reader);
        }

        private static void OnRPC_GrenadeThrowRequest(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;

            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer == null) return;

            COOPManager.GrenadeM.HandleGrenadeThrowRequest(peer, reader);
        }

        private static void OnRPC_GrenadeSpawn(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            COOPManager.GrenadeM.HandleGrenadeSpawn(reader);
        }

        private static void OnRPC_GrenadeExplode(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            COOPManager.GrenadeM.HandleGrenadeExplode(reader);
        }

        private static void OnRPC_MeleeAttackRequest(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;

            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer == null) return;

            COOPManager.WeaponHandle.HandleMeleeAttackRequest(peer, reader);
        }

        private static void OnRPC_MeleeAttackSwing(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            var shooter = reader.GetString();
            var delay = reader.GetFloat();

            var mod = ModBehaviourF.Instance;
            if (!NetService.Instance.IsSelfId(shooter) && mod.clientRemoteCharacters.TryGetValue(shooter, out var who) && who)
            {
                var anim = who.GetComponentInChildren<CharacterAnimationControl_MagicBlend>(true);
                if (anim != null) anim.OnAttack();

                var cmc = who.GetComponent<CharacterMainControl>();
                var model = cmc ? cmc.characterModel : null;
                if (model) MeleeFx.SpawnSlashFx(model);
            }
            else if (shooter.StartsWith("AI:"))
            {
                if (int.TryParse(shooter.Substring(3), out var aiId) && AITool.aiById.TryGetValue(aiId, out var cmc) && cmc)
                {
                    var anim = cmc.GetComponentInChildren<CharacterAnimationControl_MagicBlend>(true);
                    if (anim != null) anim.OnAttack();

                    var model = cmc.characterModel;
                    if (model) MeleeFx.SpawnSlashFx(model);
                }
            }
        }

        private static void OnRPC_MeleeHitReport(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;

            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer == null) return;

            COOPManager.WeaponHandle.HandleMeleeHitReport(peer, reader);
        }

        #endregion
        
        #region Animation Sync RPCs

        private static void OnRPC_AnimationSyncRequest(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;

            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer == null) return;

            COOPManager.PublicHandleUpdate.HandleClientAnimationStatus(peer, reader);
        }

        private static void OnRPC_AnimationSync(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            var playerId = reader.GetString();
            if (NetService.Instance.IsSelfId(playerId)) return;

            var moveSpeed = reader.GetFloat();
            var moveDirX = reader.GetFloat();
            var moveDirY = reader.GetFloat();
            var isDashing = reader.GetBool();
            var isAttacking = reader.GetBool();
            var handState = reader.GetInt();
            var gunReady = reader.GetBool();
            var stateHash = reader.GetInt();
            var normTime = reader.GetFloat();

            if (ModBehaviourF.Instance.clientRemoteCharacters.TryGetValue(playerId, out var obj) && obj != null)
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

        #endregion
        
        #region Player Dead and Despawn RPCs

        private static void OnRPC_PlayerDeadTree(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;

            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer == null) return;

            var posX = reader.GetFloat();
            var posY = reader.GetFloat();
            var posZ = reader.GetFloat();
            var pos = new UnityEngine.Vector3(posX, posY, posZ);
            
            var rotX = reader.GetFloat();
            var rotY = reader.GetFloat();
            var rotZ = reader.GetFloat();
            var rotW = reader.GetFloat();
            var rot = new UnityEngine.Quaternion(rotX, rotY, rotZ, rotW);

            var snap = ItemTool.ReadItemSnapshot(reader);
            var tmpRoot = ItemTool.BuildItemFromSnapshot(snap);
            if (!tmpRoot)
            {
                Debug.LogWarning("[PlayerDeadTree] BuildItemFromSnapshot failed.");
                return;
            }

            var deadPfb = LootManager.Instance.ResolveDeadLootPrefabOnServer();
            var box = InteractableLootbox.CreateFromItem(tmpRoot, pos + UnityEngine.Vector3.up * 0.10f, rot, true, deadPfb);
            if (box) DeadLootBox.Instance.Server_OnDeadLootboxSpawned(box, null);

            if (ModBehaviourF.Instance.remoteCharacters.TryGetValue(peer, out var proxy) && proxy)
            {
                UnityEngine.Object.Destroy(proxy);
                ModBehaviourF.Instance.remoteCharacters.Remove(peer);
            }

            if (ModBehaviourF.Instance.playerStatuses.TryGetValue(peer, out var st) && !string.IsNullOrEmpty(st.EndPoint))
            {
                manager.CallRPC("RemoteDespawn", RPCTarget.AllClients, 0, w =>
                {
                    w.Put(st.EndPoint);
                }, DeliveryMethod.ReliableOrdered);
            }
        }

        private static void OnRPC_RemoteDespawn(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            var id = reader.GetString();
            if (ModBehaviourF.Instance.clientRemoteCharacters.TryGetValue(id, out var go) && go)
                UnityEngine.Object.Destroy(go);
            ModBehaviourF.Instance.clientRemoteCharacters.Remove(id);
        }

        #endregion
        
        #region Buff System RPCs

        private static void OnRPC_PlayerBuffSelfApply(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            var weaponTypeId = reader.GetInt();
            var buffId = reader.GetInt();
            COOPManager.Buff.ApplyBuffToSelf_Client(weaponTypeId, buffId).Forget();
        }

        private static void OnRPC_HostBuffProxyApply(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            var hostId = reader.GetString();
            var weaponTypeId = reader.GetInt();
            var buffId = reader.GetInt();
            COOPManager.Buff.ApplyBuffProxy_Client(hostId, weaponTypeId, buffId).Forget();
        }

        #endregion
        
        #region Player Hurt Event RPC

        private static void OnRPC_PlayerHurtEvent(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            var dmg = reader.GetFloat();
            var ap = reader.GetFloat();
            var cdf = reader.GetFloat();
            var cr = reader.GetFloat();
            var crit = reader.GetInt();
            var hitX = reader.GetFloat();
            var hitY = reader.GetFloat();
            var hitZ = reader.GetFloat();
            var hit = new UnityEngine.Vector3(hitX, hitY, hitZ);
            var nrmX = reader.GetFloat();
            var nrmY = reader.GetFloat();
            var nrmZ = reader.GetFloat();
            var nrm = new UnityEngine.Vector3(nrmX, nrmY, nrmZ);
            var wid = reader.GetInt();
            var bleed = reader.GetFloat();
            var boom = reader.GetBool();

            var main = LevelManager.Instance ? LevelManager.Instance.MainCharacter : null;
            if (!main || main.Health == null) return;

            var health = main.Health;

            var di = new DamageInfo(main)
            {
                damageValue = dmg,
                armorPiercing = ap,
                critDamageFactor = cdf,
                critRate = cr,
                crit = crit,
                damagePoint = hit,
                damageNormal = nrm,
                fromWeaponItemID = wid,
                bleedChance = bleed,
                isExplosion = boom
            };

            health.Hurt(di);
            HealthM.Instance.Client_SendSelfHealth(health, true);
        }

        #endregion
        
        #region Environment Sync RPCs

        private static void OnRPC_EnvSyncRequest(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;

            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer == null) return;

            COOPManager.Weather.Server_BroadcastEnvSync(peer);
        }

        private static void OnRPC_EnvSyncState(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

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

            var vis = new System.Collections.Generic.Dictionary<int, bool>(lootCount);
            for (var i = 0; i < lootCount; ++i)
            {
                var k = 0;
                var on = false;
                try
                {
                    k = reader.GetInt();
                }
                catch
                {
                }

                try
                {
                    on = reader.GetBool();
                }
                catch
                {
                }

                vis[k] = on;
            }

            LootNet.Client_ApplyLootVisibility(vis);

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
                catch
                {
                }

                try
                {
                    cl = reader.GetBool();
                }
                catch
                {
                }

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
                catch
                {
                }

                if (did != 0) COOPManager.destructible.Client_ApplyDestructibleDead_Snapshot(did);
            }

            COOPManager.Weather.Client_ApplyEnvSync(day, sec, scale, seed, forceW, forceWVal, curWeather, stormLv);
        }

        #endregion

        #region Scene Voting/Gate RPCs

        private static void OnRPC_SceneVoteStart(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;
            SceneNet.Instance.Client_OnSceneVoteStart(reader);
        }

        private static void OnRPC_SceneVoteReq(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;
            
            var targetId = reader.GetString();
            var flags = reader.GetByte();
            bool hasCurtain, useLoc, notifyEvac, saveToFile;
            PackFlag.UnpackFlags(flags, out hasCurtain, out useLoc, out notifyEvac, out saveToFile);

            string curtainGuid = null;
            if (hasCurtain) SceneNet.TryGetString(reader, out curtainGuid);
            if (!SceneNet.TryGetString(reader, out var locName)) locName = string.Empty;

            if (Spectator.Instance._spectatorActive) Spectator.Instance._spectatorEndOnVotePending = true;
            SceneNet.Instance.Host_BeginSceneVote_Simple(targetId, curtainGuid, notifyEvac, saveToFile, useLoc, locName);
        }

        private static void OnRPC_SceneReadySet(long senderConnectionId, NetDataReader reader)
        {
            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            
            if (ModBehaviourF.Instance.IsServer)
            {
                if (reader.AvailableBytes == 1)
                {
                    var ready = reader.GetBool();
                    SceneNet.Instance.Server_OnSceneReadySet(peer, ready);
                }
                else if (reader.AvailableBytes > 1)
                {
                    var pid = reader.GetString();
                    var rdy = reader.GetBool();
                    
                    if (SceneNet.Instance.sceneReady.ContainsKey(pid))
                    {
                        SceneNet.Instance.sceneReady[pid] = rdy;
                        MModUI.Instance?.UpdateVotePanel();
                    }
                }
            }
            else
            {
                var pid = reader.GetString();
                var rdy = reader.GetBool();

                if (!SceneNet.Instance.sceneReady.ContainsKey(pid) && SceneNet.Instance.sceneParticipantIds.Contains(pid))
                    SceneNet.Instance.sceneReady[pid] = false;

                if (SceneNet.Instance.sceneReady.ContainsKey(pid))
                {
                    SceneNet.Instance.sceneReady[pid] = rdy;
                    MModUI.Instance?.UpdateVotePanel();
                }
            }
        }

        private static void OnRPC_SceneBeginLoad(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;
            
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

        private static void OnRPC_SceneCancel(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer)
            {
                SceneNet.Instance.Client_OnVoteCancelled();
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

        private static void OnRPC_SceneReady(long senderConnectionId, NetDataReader reader)
        {
            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            
            var id = reader.GetString();
            var sid = reader.GetString();
            var pos = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            var rot = new Quaternion(reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            var face = reader.GetString();

            if (ModBehaviourF.Instance.IsServer) SceneNet.Instance.Server_HandleSceneReady(peer, id, sid, pos, rot, face);
        }

        private static void OnRPC_SceneGateReady(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;
            
            var pid = reader.GetString();
            var sid = reader.GetString();

            if (string.IsNullOrEmpty(SceneNet.Instance._srvGateSid))
                SceneNet.Instance._srvGateSid = sid;

            if (sid == SceneNet.Instance._srvGateSid) SceneNet.Instance._srvGateReadyPids.Add(pid);
        }

        private static void OnRPC_SceneGateRelease(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;
            
            var sid = reader.GetString();
            if (string.IsNullOrEmpty(SceneNet.Instance._cliGateSid) || sid == SceneNet.Instance._cliGateSid)
            {
                SceneNet.Instance._cliGateSid = sid;
                SceneNet.Instance._cliSceneGateReleased = true;
                HealthM.Instance.Client_ReportSelfHealth_IfReadyOnce();
            }
            else
            {
                SceneNet.Instance._cliGateSid = sid;
                SceneNet.Instance._cliSceneGateReleased = true;
                HealthM.Instance.Client_ReportSelfHealth_IfReadyOnce();
            }
        }

        #endregion

        #region Loot System RPCs

        private static void OnRPC_LootReqOpen(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;
            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer != null) LootManager.Instance.Server_HandleLootOpenRequest(peer, reader);
        }

        private static void OnRPC_LootState(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;
            COOPManager.LootNet.Client_ApplyLootboxState(reader);
        }

        private static void OnRPC_LootReqPut(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;
            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer != null) COOPManager.LootNet.Server_HandleLootPutRequest(peer, reader);
        }

        private static void OnRPC_LootReqTake(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;
            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer != null) COOPManager.LootNet.Server_HandleLootTakeRequest(peer, reader);
        }

        private static void OnRPC_LootPutOk(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;
            COOPManager.LootNet.Client_OnLootPutOk(reader);
        }

        private static void OnRPC_LootTakeOk(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;
            COOPManager.LootNet.Client_OnLootTakeOk(reader);
        }

        private static void OnRPC_LootDeny(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;
            var reason = reader.GetString();
            
            if (reason == "no_inv") return;

            var lv = LootView.Instance;
            var inv = lv ? lv.TargetInventory : null;
            if (inv) COOPManager.LootNet.Client_RequestLootState(inv);
        }

        private static void OnRPC_LootReqSplit(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;
            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer != null) COOPManager.LootNet.Server_HandleLootSplitRequest(peer, reader);
        }

        private static void OnRPC_LootReqSlotUnplug(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;
            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer != null) COOPManager.LootNet.Server_HandleLootSlotUnplugRequest(peer, reader);
        }

        private static void OnRPC_LootReqSlotPlug(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;
            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer != null) COOPManager.LootNet.Server_HandleLootSlotPlugRequest(peer, reader);
        }

        #endregion

        #region AI Sync RPCs

        private static void OnRPC_AiSeedSnapshot(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;
            COOPManager.AIHandle.HandleAiSeedSnapshot(reader);
        }

        private static void OnRPC_AiLoadoutSnapshot(long senderConnectionId, NetDataReader reader)
        {
            var mod = ModBehaviourF.Instance;
            var ver = reader.GetByte();
            var aiId = reader.GetInt();

            var ne = reader.GetInt();
            var equips = new System.Collections.Generic.List<(int slot, int tid)>(ne);
            for (var i = 0; i < ne; ++i)
            {
                var sh = reader.GetInt();
                var tid = reader.GetInt();
                equips.Add((sh, tid));
            }

            var nw = reader.GetInt();
            var weapons = new System.Collections.Generic.List<(int slot, int tid)>(nw);
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
            if (ver >= 4) showName = reader.GetBool();

            string displayName = null;
            if (ver >= 5)
            {
                var hasName = reader.GetBool();
                if (hasName) displayName = reader.GetString();
            }

            if (mod.IsServer) return;

            if (AITool.aiById.TryGetValue(aiId, out var cmc) && cmc)
                COOPManager.AIHandle.Client_ApplyAiLoadout(aiId, equips, weapons, faceJson, modelName, iconType, showName, displayName).Forget();
            else
                COOPManager.AIHandle.pendingAiLoadouts[aiId] = (equips, weapons, faceJson, modelName, iconType, showName, displayName);
        }

        private static void OnRPC_AiTransformSnapshot(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;
            var mod = ModBehaviourF.Instance;
            var n = reader.GetInt();

            if (!AITool._aiSceneReady)
            {
                for (var i = 0; i < n; ++i)
                {
                    var aiId = reader.GetInt();
                    var p = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
                    var f = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
                    if (mod._pendingAiTrans.Count < 512) mod._pendingAiTrans.Enqueue((aiId, p, f));
                }
                return;
            }

            for (var i = 0; i < n; i++)
            {
                var aiId = reader.GetInt();
                var p = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
                var f = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
                AITool.ApplyAiTransform(aiId, p, f);
            }
        }

        private static void OnRPC_AiAnimSnapshot(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;
            var mod = ModBehaviourF.Instance;
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
                    mod._pendingAiAnims[id] = st;
            }
        }

        private static void OnRPC_AiAttackSwing(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;
            var id = reader.GetInt();
            if (AITool.aiById.TryGetValue(id, out var cmc) && cmc)
            {
                var anim = cmc.GetComponent<CharacterAnimationControl_MagicBlend>();
                if (anim != null) anim.OnAttack();
                var model = cmc.characterModel;
                if (model) MeleeFx.SpawnSlashFx(model);
            }
        }

        private static void OnRPC_AiHealthSync(long senderConnectionId, NetDataReader reader)
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

        private static void OnRPC_AiHealthReport(long senderConnectionId, NetDataReader reader)
        {
            if (!ModBehaviourF.Instance.IsServer) return;
            var manager = HybridRPCManager.Instance;
            var peer = manager?.GetPeerByConnectionId(senderConnectionId);
            if (peer != null) COOPManager.AIHealth.HandleAiHealthReport(peer, reader);
        }

        private static void OnRPC_DeadLootSpawn(long senderConnectionId, NetDataReader reader)
        {
            var scene = reader.GetInt();
            var aiId = reader.GetInt();
            var lootUid = reader.GetInt();
            var pos = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            var rot = new Quaternion(reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex != scene) return;

            DeadLootBox.Instance.SpawnDeadLootboxAt(aiId, lootUid, pos, rot);
        }

        private static void OnRPC_AiNameIcon(long senderConnectionId, NetDataReader reader)
        {
            if (ModBehaviourF.Instance.IsServer) return;

            var aiId = reader.GetInt();
            var iconType = reader.GetInt();
            var showName = reader.GetBool();
            string displayName = null;
            var hasName = reader.GetBool();
            if (hasName) displayName = reader.GetString();

            if (AITool.aiById.TryGetValue(aiId, out var cmc) && cmc)
                AIName.RefreshNameIconWithRetries(cmc, iconType, showName, displayName).Forget();
        }

        private static void OnRPC_AiSeedPatch(long senderConnectionId, NetDataReader reader)
        {
            COOPManager.AIHandle.HandleAiSeedPatch(reader);
        }

        #endregion
    }
}

