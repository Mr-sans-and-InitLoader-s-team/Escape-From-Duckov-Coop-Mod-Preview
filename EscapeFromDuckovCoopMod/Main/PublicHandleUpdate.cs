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

﻿using Cysharp.Threading.Tasks;
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
    public class PublicHandleUpdate
    {
        private NetService Service => NetService.Instance;

        private bool IsServer => Service != null && Service.IsServer;
        private NetManager netManager => Service?.netManager;
        private NetDataWriter writer => Service?.writer;
        private NetPeer connectedPeer => Service?.connectedPeer;
        private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
        private bool networkStarted => Service != null && Service.networkStarted;
        private Dictionary<string, GameObject> remoteCharacters => Service?.remoteCharacters;
        private Dictionary<string, PlayerStatus> playerStatuses => Service?.playerStatuses;
        private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;
        public void HandleEquipmentUpdate(NetPeer sender, NetPacketReader reader)
        {
            string endPoint = reader.GetString();
            int slotHash = reader.GetInt();
            string itemId = reader.GetString();

           COOPManager.HostPlayer_Apply.ApplyEquipmentUpdate(sender.EndPoint.ToString(), slotHash, itemId).Forget();

            var w = new NetDataWriter();
            w.Put((byte)Op.EQUIPMENT_UPDATE);
            w.Put(endPoint);
            w.Put(slotHash);
            w.Put(itemId);
            
            if (netManager != null)
            {
                foreach (var p in netManager.ConnectedPeerList)
                {
                    if (p == sender) continue;
                    p.Send(w, DeliveryMethod.ReliableOrdered);
                }
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.CurrentMode == EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.SteamP2P)
                {
                    var steamNet = EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance;
                    if (steamNet != null && steamNet.peerConnections != null)
                    {
                        string senderEndPoint = sender != null ? sender.EndPoint.ToString() : null;
                        foreach (var peer in steamNet.peerConnections.Keys)
                        {
                            if (peer.ToString() == senderEndPoint) continue;
                            steamNet.SendPacket(peer, w.Data, w.Length);
                        }
                    }
                }
            }

        }


        public void HandleWeaponUpdate(NetPeer sender, NetPacketReader reader)
        {
            string endPoint = reader.GetString();
            int slotHash = reader.GetInt();
            string itemId = reader.GetString();

            COOPManager.HostPlayer_Apply.ApplyWeaponUpdate(sender.EndPoint.ToString(), slotHash, itemId).Forget();

            var w = new NetDataWriter();
            w.Put((byte)Op.PLAYERWEAPON_UPDATE);
            w.Put(endPoint);
            w.Put(slotHash);
            w.Put(itemId);
            
            if (netManager != null)
            {
                foreach (var p in netManager.ConnectedPeerList)
                {
                    if (p == sender) continue;
                    p.Send(w, DeliveryMethod.ReliableOrdered);
                }
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.CurrentMode == EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.SteamP2P)
                {
                    var steamNet = EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance;
                    if (steamNet != null && steamNet.peerConnections != null)
                    {
                        string senderEndPoint = sender != null ? sender.EndPoint.ToString() : null;
                        foreach (var peer in steamNet.peerConnections.Keys)
                        {
                            if (peer.ToString() == senderEndPoint) continue;
                            steamNet.SendPacket(peer, w.Data, w.Length);
                        }
                    }
                }
            }

        }

        public void HandleClientAnimationStatus(NetPeer sender, NetPacketReader reader)
        {
            float moveSpeed = reader.GetFloat();
            float moveDirX = reader.GetFloat();
            float moveDirY = reader.GetFloat();
            bool isDashing = reader.GetBool();
            bool isAttacking = reader.GetBool();
            int handState = reader.GetInt();
            bool gunReady = reader.GetBool();
            int stateHash = reader.GetInt();
            float normTime = reader.GetFloat();

            string senderEndPoint = sender != null ? sender.EndPoint.ToString() : null;
            
            HandleRemoteAnimationStatus(senderEndPoint, moveSpeed, moveDirX, moveDirY, isDashing, isAttacking, handState, gunReady, stateHash, normTime);

            string playerId = playerStatuses.TryGetValue(senderEndPoint, out var st) && !string.IsNullOrEmpty(st.EndPoint)
                ? st.EndPoint
                : senderEndPoint;

            var w = new NetDataWriter();
            w.Put((byte)Op.ANIM_SYNC);
            w.Put(playerId);
            w.Put(moveSpeed);
            w.Put(moveDirX);
            w.Put(moveDirY);
            w.Put(isDashing);
            w.Put(isAttacking);
            w.Put(handState);
            w.Put(gunReady);
            w.Put(stateHash);
            w.Put(normTime);
            
            if (netManager != null)
            {
                foreach (var p in netManager.ConnectedPeerList)
                {
                    if (p == sender) continue;
                    p.Send(w, DeliveryMethod.Sequenced);
                }
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.CurrentMode == EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.SteamP2P)
                {
                    var steamNet = EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance;
                    if (steamNet != null && steamNet.peerConnections != null)
                    {
                        foreach (var peer in steamNet.peerConnections.Keys)
                        {
                            if (peer.ToString() == senderEndPoint) continue;
                            steamNet.SendPacket(peer, w.Data, w.Length);
                        }
                    }
                }
            }

        }

        void HandleRemoteAnimationStatus(string endPoint, float moveSpeed, float moveDirX, float moveDirY,
                                  bool isDashing, bool isAttacking, int handState, bool gunReady,
                                  int stateHash, float normTime)
        {
            if (string.IsNullOrEmpty(endPoint) || !remoteCharacters.TryGetValue(endPoint, out var remoteObj) || remoteObj == null) return;

            var ai = AnimInterpUtil.Attach(remoteObj);
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

        public void HandlePositionUpdate(NetPeer sender, NetPacketReader reader)
        {
            string endPoint = reader.GetString();
            Vector3 position = reader.GetV3cm(); // ← 统一
            Vector3 dir = reader.GetDir();
            Quaternion rotation = Quaternion.LookRotation(dir, Vector3.up);

            foreach (var p in netManager.ConnectedPeerList)
            {
                if (p == sender) continue;
                var w = new NetDataWriter();
                w.Put((byte)Op.POSITION_UPDATE);
                w.Put(endPoint);
                NetPack.PutV3cm(w, position);     // ← 统一
                NetPack.PutDir(w, dir);
                p.Send(w, DeliveryMethod.Unreliable);
            }
        }


        public void HandlePositionUpdate_Q(NetPeer peer, string endPoint, Vector3 position, Quaternion rotation)
        {
            if (string.IsNullOrEmpty(endPoint) || playerStatuses == null) return;
            
            if (!playerStatuses.TryGetValue(endPoint, out var st)) return;
            
            st.Position = position;
            st.Rotation = rotation;

            if (remoteCharacters != null && remoteCharacters.TryGetValue(endPoint, out var go) && go != null)
            {
                var ni = NetInterpUtil.Attach(go);
                ni?.Push(position, rotation);
            }

            if (writer == null) return;
            
            writer.Reset();
            writer.Put((byte)Op.POSITION_UPDATE);
            writer.Put(st.EndPoint ?? endPoint);
            writer.PutV3cm(position);
            Vector3 fwd = rotation * Vector3.forward;
            writer.PutDir(fwd);
            
            if (netManager != null)
            {
                foreach (var p in netManager.ConnectedPeerList)
                {
                    if (p == peer) continue;
                    p.Send(writer, DeliveryMethod.Unreliable);
                }
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.CurrentMode == EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.SteamP2P)
                {
                    var steamNet = EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance;
                    if (steamNet != null && steamNet.peerConnections != null)
                    {
                        string senderEndPoint = peer != null ? peer.EndPoint.ToString() : null;
                        foreach (var peerKey in steamNet.peerConnections.Keys)
                        {
                            if (peerKey.ToString() == senderEndPoint) continue;
                            steamNet.SendPacket(peerKey, writer.Data, writer.Length);
                        }
                    }
                }
            }
        }



 








    }
}
