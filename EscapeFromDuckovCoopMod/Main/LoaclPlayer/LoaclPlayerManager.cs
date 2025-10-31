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

﻿using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod
{
    public class LoaclPlayerManager : MonoBehaviour
    {
        public static LoaclPlayerManager Instance;

        private NetService Service => NetService.Instance;
        private bool IsServer => Service != null && Service.IsServer;
        private NetManager netManager => Service?.netManager;
        private NetDataWriter writer => Service?.writer;
        private NetPeer connectedPeer => Service?.connectedPeer;
        //public PlayerStatus LocalPlayerStatus => Service?.localPlayerStatus;
        private bool networkStarted => Service != null && Service.networkStarted;
        private int port => Service?.port ?? 0;
        private Dictionary<string, GameObject> remoteCharacters => Service?.remoteCharacters;
        private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;
        // weaponTypeId(= Item.TypeID) -> projectile prefab
        public readonly Dictionary<int, Projectile> _projCacheByWeaponType = new Dictionary<int, Projectile>();
        // 缓存：武器TypeID -> 枪口火Prefab（可能为null）
        public readonly Dictionary<int, GameObject> _muzzleFxCacheByWeaponType = new Dictionary<int, GameObject>();

        public readonly Dictionary<string, (ItemAgent_Gun gun, Transform muzzle)> _gunCacheByShooter = new Dictionary<string, (ItemAgent_Gun gun, Transform muzzle)>();

        // —— 外观缓存（避免发空&避免被空覆盖）——
        public string _lastGoodFaceJson = null;

        private bool _cliSelfDeathFired = false;

        // 是否已经上报过“本轮生命”的尸体/战利品（= 主机已可生成，不要再上报）
        internal bool _cliCorpseTreeReported = false;

        // 正在执行“补发死亡”的 OnDead 触发（仅作上下文标记，便于补丁识别来源）
        internal bool _cliInEnsureSelfDeathEmit = false;

        public void Init()
        {
            Instance = this;
        }
        public void InitializeLocalPlayer()
        {
            var bool1 = ComputeIsInGame(out var ids);
            Service.localPlayerStatus = new PlayerStatus
            {

                EndPoint = IsServer ? ("Host:" + port) : ("Client:" + Guid.NewGuid().ToString().Substring(0, 8)),
                PlayerName = IsServer ? "Host" : "Client",
                Latency = 0,
                IsInGame = bool1,
                LastIsInGame = bool1,
                Position = Vector3.zero,
                Rotation = Quaternion.identity,
                SceneId = ids,
                CustomFaceJson = CustomFace.LoadLocalCustomFaceJson()
            };
        }

        public bool ComputeIsInGame(out string sceneId)
        {
            sceneId = null;

            // 1) LevelManager/主角存在才算“进了关卡”
            var lm = LevelManager.Instance;
            if (lm == null || lm.MainCharacter == null)
                return false;

            // 2) 优先尝试从 MultiSceneCore 的“当前子场景”取 id
            //    注意：不要用 MultiSceneCore.SceneInfo，它是根据 core 自己所在的主场景算的！
            try
            {
                var core = Duckov.Scenes.MultiSceneCore.Instance;
                if (core != null)
                {
                    // 反编译环境下常见：sub scene 的 id 就是 SubSceneEntry.sceneID
                    // 这里用“当前激活子场景”的 BuildIndex 反查 ID，或直接通过 ActiveSubScene 名称兜底。
                    var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                    if (active.IsValid())
                    {
                        // 通过 buildIndex -> SceneInfoCollection 查询 ID（能查到的话）
                        var idFromBuild = SceneInfoCollection.GetSceneID(active.buildIndex);
                        if (!string.IsNullOrEmpty(idFromBuild))
                            sceneId = idFromBuild;
                        else
                            sceneId = active.name; // 查不到就用场景名兜底
                    }
                }
            }
            catch { /* 忽略反射/反编译异常 */ }

            // 3) 如果还是没拿到，尝试识别 Base
            if (string.IsNullOrEmpty(sceneId))
            {
                // Base 作为“家/大厅”，仍视为在游戏里，并归一成固定ID，便于双方比对
                // （常规工程里 Base 的常量是 "Base"）
                sceneId = SceneInfoCollection.BaseSceneID; // "Base"
            }

            // 4) 只要有一个非空 sceneId，就认为“在游戏中”
            return !string.IsNullOrEmpty(sceneId);
        }

        public List<EquipmentSyncData> GetLocalEquipment()
        {
            var equipmentList = new List<EquipmentSyncData>();
            var equipmentController = CharacterMainControl.Main?.EquipmentController;
            if (equipmentController == null) return equipmentList;

            var slotNames = new[] { "armorSlot", "helmatSlot", "faceMaskSlot", "backpackSlot", "headsetSlot" };
            var slotHashes = new[] { CharacterEquipmentController.armorHash, CharacterEquipmentController.helmatHash, CharacterEquipmentController.faceMaskHash, CharacterEquipmentController.backpackHash, CharacterEquipmentController.headsetHash };

            for (int i = 0; i < slotNames.Length; i++)
            {
                try
                {
                    var slotField = Traverse.Create(equipmentController).Field<ItemStatsSystem.Items.Slot>(slotNames[i]);
                    if (slotField.Value == null) continue;

                    var slot = slotField.Value;
                    string itemId = (slot?.Content != null) ? slot.Content.TypeID.ToString() : "";
                    equipmentList.Add(new EquipmentSyncData { SlotHash = slotHashes[i], ItemId = itemId });
                }
                catch (Exception ex)
                {
                    Debug.LogError("获取槽位 " + slotNames[i] + " 时发生错误: " + ex.Message);
                }
            }

            return equipmentList;
        }

        public List<WeaponSyncData> GetLocalWeapons()
        {
            var weaponList = new List<WeaponSyncData>();
            var mainControl = CharacterMainControl.Main;
            if (mainControl == null) return weaponList;

            try
            {
                var rangedWeapon = mainControl.GetGun();
                weaponList.Add(new WeaponSyncData
                {
                    SlotHash = (int)HandheldSocketTypes.normalHandheld,
                    ItemId = rangedWeapon != null ? rangedWeapon.Item.TypeID.ToString() : ""
                });

                var meleeWeapon = mainControl.GetMeleeWeapon();
                weaponList.Add(new WeaponSyncData
                {
                    SlotHash = (int)HandheldSocketTypes.meleeWeapon,
                    ItemId = meleeWeapon != null ? meleeWeapon.Item.TypeID.ToString() : ""
                });
            }
            catch (Exception ex)
            {
                Debug.LogError("获取本地武器数据时发生错误: " + ex.Message);
            }

            return weaponList;
        }

        public void Main_OnHoldAgentChanged(DuckovItemAgent obj)
        {
            if (obj == null) return;

            string itemId = obj.Item?.TypeID.ToString() ?? "";
            HandheldSocketTypes slotHash = obj.handheldSocket;

            // 这里用实际在手里的组件来判定是不是“枪/弓”
            var gunAgent = obj as ItemAgent_Gun;
            if (gunAgent != null)
            {
                int typeId;
                if (int.TryParse(itemId, out typeId))
                {
                    // 从在手的 Agent 读取设置，比从 ItemSetting_XXX 猜更稳（弓也适用）
                    var setting = gunAgent.GunItemSetting; // 弓也会挂在这（反编译看得到）
                    Projectile pfb = (setting != null && setting.bulletPfb != null)
                        ? setting.bulletPfb
                        : Duckov.Utilities.GameplayDataSettings.Prefabs.DefaultBullet;

                    _projCacheByWeaponType[typeId] = pfb;
                    _muzzleFxCacheByWeaponType[typeId] = (setting != null) ? setting.muzzleFxPfb : null;
                }
            }

            // 原有：发送玩家手持武器变更（保持不变）
            var weaponData = new WeaponSyncData
            {
                SlotHash = (int)slotHash,
                ItemId = itemId
            };
            Send_LoaclPlayerStatus.Instance.SendWeaponUpdate(weaponData);
        }

        public void ModBehaviour_onSlotContentChanged(ItemStatsSystem.Items.Slot obj)
        {
            if (!networkStarted || Service.localPlayerStatus == null || !Service.localPlayerStatus.IsInGame) return;
            if (obj == null) return;

            string itemId1 = "";
            if (obj.Content != null) itemId1 = obj.Content.TypeID.ToString();
            //联机项目早期做出来的
            int slotHash1 = obj.GetHashCode();
            if (obj.Key == "Helmat") slotHash1 = 200;
            if (obj.Key == "Armor") slotHash1 = 100;
            if (obj.Key == "FaceMask") slotHash1 = 300;
            if (obj.Key == "Backpack") slotHash1 = 400;
            if (obj.Key == "Head") slotHash1 = 500;

            var equipmentData1 = new EquipmentSyncData { SlotHash = slotHash1, ItemId = itemId1 };
            Send_LoaclPlayerStatus.Instance.SendEquipmentUpdate(equipmentData1);
        }

        public void UpdatePlayerStatuses()
        {
            if (netManager == null || !netManager.IsRunning || Service.localPlayerStatus == null)
                return;
            var bool1 = ComputeIsInGame(out var ids);
            bool currentIsInGame = bool1;
            var levelManager = LevelManager.Instance;

            if (Service.localPlayerStatus.IsInGame != currentIsInGame)
            {
                Service.localPlayerStatus.IsInGame = currentIsInGame;
                Service.localPlayerStatus.LastIsInGame = currentIsInGame;

                if (levelManager != null && levelManager.MainCharacter != null)
                {
                    Service.localPlayerStatus.Position = levelManager.MainCharacter.transform.position;
                    Service.localPlayerStatus.Rotation = levelManager.MainCharacter.modelRoot.transform.rotation;
                    Service.localPlayerStatus.CustomFaceJson = CustomFace.LoadLocalCustomFaceJson();
                }

                if (currentIsInGame && levelManager != null)
                {
                    // 不再二次创建本地主角；只做 Scene 就绪上报，由主机撮合同图远端创建
                    SceneNet.Instance.TrySendSceneReadyOnce();

                }


                if (!IsServer) Send_ClientStatus.Instance.SendClientStatusUpdate();
                else Send_LoaclPlayerStatus.Instance.SendPlayerStatusUpdate();
            }
            else if (currentIsInGame && levelManager != null && levelManager.MainCharacter != null)
            {
                Service.localPlayerStatus.Position = levelManager.MainCharacter.transform.position;
                Service.localPlayerStatus.Rotation = levelManager.MainCharacter.modelRoot.transform.rotation;
            }

            if (currentIsInGame)
            {
                Service.localPlayerStatus.CustomFaceJson = CustomFace.LoadLocalCustomFaceJson();
            }
        }

        public bool IsAlive(CharacterMainControl cmc)
        {
            if (!cmc) return false;
            try { return cmc.Health != null && cmc.Health.CurrentHealth > 0.001f; } catch { return false; }
        }



        public void Client_EnsureSelfDeathEvent(global::Health h, global::CharacterMainControl cmc)
        {
            if (!h || !cmc) return;

            float cur = 1f;
            try { cur = h.CurrentHealth; } catch { }

            // 血量 > 0 ：视为复活/回血，清空所有“本轮死亡”相关标记
            if (cur > 1e-3f)
            {
                _cliSelfDeathFired = false;
                _cliCorpseTreeReported = false;      //下一条命允许重新上报尸体树
                _cliInEnsureSelfDeathEmit = false;   //清上下文
                return;
            }

            // 防重入：本地本轮只补发一次 OnDead
            if (_cliSelfDeathFired) return;

            try
            {
                var di = new global::DamageInfo
                {
                    isFromBuffOrEffect = false,
                    damageValue = 0f,
                    finalDamage = 0f,
                    damagePoint = cmc.transform.position,
                    damageNormal = UnityEngine.Vector3.up,
                    fromCharacter = null
                };

                // 标记：这次 OnDead 来源于“补发”
                _cliInEnsureSelfDeathEmit = true;

                // 关键：补发死亡事件 -> 触发 CharacterMainControl.OnDead(.)
                h.OnDeadEvent?.Invoke(di);

                _cliSelfDeathFired = true;
            }
            finally
            {
                _cliInEnsureSelfDeathEmit = false; // 收尾
            }
        }

        public void UpdateRemoteCharacters()
        {
            if (IsServer)
            {
                foreach (var kvp in remoteCharacters)
                {
                    var go = kvp.Value;
                    if (!go) continue;
                    NetInterpUtil.Attach(go); // 确保有组件；具体位置更新由 NetInterpolator 驱动
                }
            }
            else
            {
                foreach (var kvp in clientRemoteCharacters)
                {
                    var go = kvp.Value;
                    if (!go) continue;
                    NetInterpUtil.Attach(go);
                }
            }
        }



    }


    public class PlayerStatus
    {
        private NetService Service => NetService.Instance;

        public int Latency { get; set; }
        public bool IsInGame { get; set; }
        public string EndPoint { get; set; }
        public string PlayerName { get; set; }
        public bool LastIsInGame { get; set; }
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public string CustomFaceJson { get; set; }
        public List<EquipmentSyncData> EquipmentList { get; set; } = new List<EquipmentSyncData>();
        public List<WeaponSyncData> WeaponList { get; set; } = new List<WeaponSyncData>();

        public string SceneId;


    }


}
