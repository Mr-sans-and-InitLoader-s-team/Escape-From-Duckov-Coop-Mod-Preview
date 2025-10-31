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

﻿using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod
{
    public class Send_LoaclPlayerStatus : MonoBehaviour
    {
        public static Send_LoaclPlayerStatus Instance;

        private NetService Service => NetService.Instance;
        private bool IsServer => Service != null && Service.IsServer;
        private NetManager netManager => Service?.netManager;
        private NetDataWriter writer => Service?.writer;
        private NetPeer connectedPeer => Service?.connectedPeer;
        private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
        private bool networkStarted => Service != null && Service.networkStarted;
        private Dictionary<string, PlayerStatus> playerStatuses => Service?.playerStatuses;
        public void Init()
        {
            Instance = this;
        }
        public void SendPlayerStatusUpdate()
        {
            if (!IsServer) return;

            if (writer == null)
            {
                Debug.LogWarning("[SendPlayerStatusUpdate] writer is null");
                return;
            }

            var statuses = new List<PlayerStatus> { localPlayerStatus };
            foreach (var kvp in playerStatuses) statuses.Add(kvp.Value);

            writer.Reset();
            writer.Put((byte)Op.PLAYER_STATUS_UPDATE);     // opcode
            writer.Put(statuses.Count);

            foreach (var st in statuses)
            {
                writer.Put(st.EndPoint);
                writer.Put(st.PlayerName);
                writer.Put(st.Latency);
                writer.Put(st.IsInGame);
                writer.PutVector3(st.Position);
                writer.PutQuaternion(st.Rotation);

                string sid = st.SceneId;
                writer.Put(sid ?? string.Empty);

                writer.Put(st.CustomFaceJson ?? "");

                var equipmentList = st == localPlayerStatus ? LoaclPlayerManager.Instance.GetLocalEquipment() : (st.EquipmentList ?? new List<EquipmentSyncData>());
                writer.Put(equipmentList.Count);
                foreach (var e in equipmentList) e.Serialize(writer);

                var weaponList = st == localPlayerStatus ? LoaclPlayerManager.Instance.GetLocalWeapons() : (st.WeaponList ?? new List<WeaponSyncData>());
                writer.Put(weaponList.Count);
                foreach (var w in weaponList) w.Serialize(writer);
            }

            if (netManager != null)
            {
                netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.IsConnected)
                {
                    hybrid.BroadcastData(writer.Data, writer.Length, DeliveryMethod.ReliableOrdered);
                }
            }
        }


        public void SendPositionUpdate()
        {
            if (localPlayerStatus == null || !networkStarted) return;

            if (writer == null)
            {
                Debug.LogWarning("[SendPositionUpdate] writer is null");
                return;
            }

            var main = CharacterMainControl.Main;
            if (!main) return;

            var tr = main.transform;
            var mr = main.modelRoot ? main.modelRoot.transform : null;

            Vector3 pos = tr.position;
            Vector3 fwd = mr ? mr.forward : tr.forward;
            if (fwd.sqrMagnitude < 1e-12f) fwd = Vector3.forward;

            // 诊断：追踪客户端发送位置
            if (!IsServer && Time.frameCount % 120 == 0) // 每2秒记录一次
            {
                Debug.Log("[客户端发包] SendPositionUpdate pos=" + pos);
            }

            writer.Reset();
            writer.Put((byte)Op.POSITION_UPDATE);
            writer.Put(localPlayerStatus.EndPoint);

            // 统一：量化坐标 + 方向
            NetPack.PutV3cm(writer, pos);
            NetPack.PutDir(writer, fwd);

            if (IsServer)
            {
                if (netManager != null)
                {
                    netManager.SendToAll(writer, DeliveryMethod.Unreliable);
                }
                else
                {
                    var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                    if (hybrid != null && hybrid.IsConnected)
                    {
                        hybrid.BroadcastData(writer.Data, writer.Length, DeliveryMethod.Unreliable);
                    }
                }
            }
            else
            {
                if (connectedPeer != null)
                {
                    connectedPeer.Send(writer, DeliveryMethod.Unreliable);
                }
                else
                {
                    var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                    if (hybrid != null && hybrid.IsConnected)
                    {
                        hybrid.SendData(writer.Data, writer.Length, DeliveryMethod.Unreliable);
                    }
                }
            }
        }

        public void SendEquipmentUpdate(EquipmentSyncData equipmentData)
        {
            if (localPlayerStatus == null || !networkStarted) return;

            if (writer == null)
            {
                Debug.LogWarning("[SendEquipmentUpdate] writer is null");
                return;
            }

            writer.Reset();
            writer.Put((byte)Op.EQUIPMENT_UPDATE);
            writer.Put(localPlayerStatus.EndPoint);
            writer.Put(equipmentData.SlotHash);
            writer.Put(equipmentData.ItemId ?? "");

            if (IsServer)
            {
                if (netManager != null)
                {
                    netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
                }
                else
                {
                    var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                    if (hybrid != null && hybrid.IsConnected)
                    {
                        hybrid.BroadcastData(writer.Data, writer.Length, DeliveryMethod.ReliableOrdered);
                    }
                }
            }
            else
            {
                if (connectedPeer != null)
                {
                    connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
                }
                else
                {
                    var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                    if (hybrid != null && hybrid.IsConnected)
                    {
                        hybrid.SendData(writer.Data, writer.Length, DeliveryMethod.ReliableOrdered);
                    }
                }
            }
        }


        public void SendWeaponUpdate(WeaponSyncData weaponSyncData)
        {
            if (localPlayerStatus == null || !networkStarted) return;

            if (writer == null)
            {
                Debug.LogWarning("[SendWeaponUpdate] writer is null");
                return;
            }

            writer.Reset();
            writer.Put((byte)Op.PLAYERWEAPON_UPDATE);    // opcode
            writer.Put(localPlayerStatus.EndPoint);
            writer.Put(weaponSyncData.SlotHash);
            writer.Put(weaponSyncData.ItemId ?? "");

            if (IsServer)
            {
                if (netManager != null)
                {
                    netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
                }
                else
                {
                    var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                    if (hybrid != null && hybrid.IsConnected)
                    {
                        hybrid.BroadcastData(writer.Data, writer.Length, DeliveryMethod.ReliableOrdered);
                    }
                }
            }
            else
            {
                if (connectedPeer != null)
                {
                    connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
                }
                else
                {
                    var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                    if (hybrid != null && hybrid.IsConnected)
                    {
                        hybrid.SendData(writer.Data, writer.Length, DeliveryMethod.ReliableOrdered);
                    }
                }
            }
        }

        public void SendAnimationStatus()
        {
            if (!networkStarted) return;

            if (writer == null)
            {
                Debug.LogWarning("[SendAnimationStatus] writer is null");
                return;
            }

            var mainControl = CharacterMainControl.Main;
            if (mainControl == null) return;

            var model = mainControl.modelRoot.Find("0_CharacterModel_Custom_Template(Clone)");
            if (model == null) return;

            var animCtrl = model.GetComponent<CharacterAnimationControl_MagicBlend>();
            if (animCtrl == null || animCtrl.animator == null) return;

            var anim = animCtrl.animator;
            var state = anim.GetCurrentAnimatorStateInfo(0);
            int stateHash = state.shortNameHash;
            float normTime = state.normalizedTime;
            
            // 诊断：追踪客户端发送动画
            if (!IsServer && Time.frameCount % 120 == 0) // 每2秒记录一次
            {
                Debug.Log("[客户端发包] SendAnimationStatus");
            }

            writer.Reset();
            writer.Put((byte)Op.ANIM_SYNC);                      // opcode

            if (IsServer)
            {
                // 主机广播：带 playerId
                writer.Put(localPlayerStatus.EndPoint);
                writer.Put(anim.GetFloat("MoveSpeed"));
                writer.Put(anim.GetFloat("MoveDirX"));
                writer.Put(anim.GetFloat("MoveDirY"));
                writer.Put(anim.GetBool("Dashing"));
                writer.Put(anim.GetBool("Attack"));
                writer.Put(anim.GetInteger("HandState"));
                writer.Put(anim.GetBool("GunReady"));
                writer.Put(stateHash);
                writer.Put(normTime);
                
                if (netManager != null)
                {
                    netManager.SendToAll(writer, DeliveryMethod.Sequenced);
                }
                else
                {
                    var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                    if (hybrid != null && hybrid.IsConnected)
                    {
                        hybrid.BroadcastData(writer.Data, writer.Length, DeliveryMethod.Sequenced);
                    }
                }
            }
            else
            {
                // 客户端 -> 主机：不带 playerId
                writer.Put(anim.GetFloat("MoveSpeed"));
                writer.Put(anim.GetFloat("MoveDirX"));
                writer.Put(anim.GetFloat("MoveDirY"));
                writer.Put(anim.GetBool("Dashing"));
                writer.Put(anim.GetBool("Attack"));
                writer.Put(anim.GetInteger("HandState"));
                writer.Put(anim.GetBool("GunReady"));
                writer.Put(stateHash);
                writer.Put(normTime);
                
                if (connectedPeer != null)
                {
                    connectedPeer.Send(writer, DeliveryMethod.Sequenced);
                }
                else
                {
                    var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                    if (hybrid != null && hybrid.IsConnected)
                    {
                        hybrid.SendData(writer.Data, writer.Length, DeliveryMethod.Sequenced);
                    }
                }
            }
        }


        public void Net_ReportPlayerDeadTree(CharacterMainControl who)
        {
            // 仅客户端上报；主机不需要发
            if (!networkStarted || IsServer || who == null) return;

            if (writer == null)
            {
                Debug.LogWarning("[Net_ReportPlayerDeadTree] writer is null");
                return;
            }

            var item = who.CharacterItem;            // 本机一定能拿到
            if (item == null) return;

            // 尸体位置/朝向尽量贴近角色模型
            var pos = who.transform.position;
            var rot = (who.characterModel ? who.characterModel.transform.rotation : who.transform.rotation);

            // 组包并发送
            writer.Reset();
            writer.Put((byte)Op.PLAYER_DEAD_TREE);
            writer.PutV3cm(pos);
            writer.PutQuaternion(rot);

            // 把整棵物品"快照"写进包里
            ItemTool.WriteItemSnapshot(writer, item);

            if (connectedPeer != null)
            {
                connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.IsConnected)
                {
                    hybrid.SendData(writer.Data, writer.Length, DeliveryMethod.ReliableOrdered);
                }
            }
        }








    }
}
