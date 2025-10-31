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

﻿using Duckov.UI;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace EscapeFromDuckovCoopMod
{
    public static class SceneM
    {
        private static NetService Service => NetService.Instance;
        private static bool IsServer => Service != null && Service.IsServer;
        private static NetManager netManager => Service?.netManager;
        private static NetDataWriter writer => Service?.writer;
        private static NetPeer connectedPeer => Service?.connectedPeer;
        private static PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
        private static bool networkStarted => Service != null && Service.networkStarted;
        private static Dictionary<string, GameObject> remoteCharacters => Service?.remoteCharacters;
        private static Dictionary<string, PlayerStatus> playerStatuses => Service?.playerStatuses;
        private static Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;
        public static readonly Dictionary<string, string> _srvPeerScene = new Dictionary<string, string>();


        // —— 仅统计“本地图”的存活玩家；本机已死时就看同图是否还有活人 —— 
        public static bool AllPlayersDead()
        {
            // 自己的 SceneId（拿不到就 Compute 一次）懂了吗sans看到这
            string mySceneId = localPlayerStatus != null ? localPlayerStatus.SceneId : null;
            if (string.IsNullOrEmpty(mySceneId))
               LoaclPlayerManager.Instance.ComputeIsInGame(out mySceneId);

            // 拿不到 SceneId 的极端情况：沿用旧逻辑（不按同图过滤），避免误杀
            if (string.IsNullOrEmpty(mySceneId))
            {
                int alive = 0;
                if (LoaclPlayerManager.Instance.IsAlive(CharacterMainControl.Main)) alive++;
                if (IsServer)
                    foreach (var kv in remoteCharacters) { var cmc = kv.Value ? kv.Value.GetComponent<CharacterMainControl>() : null; if (LoaclPlayerManager.Instance.IsAlive(cmc)) alive++; }
                else
                    foreach (var kv in clientRemoteCharacters) { var cmc = kv.Value ? kv.Value.GetComponent<CharacterMainControl>() : null; if (LoaclPlayerManager.Instance.IsAlive(cmc)) alive++; }
                return alive == 0;
            }

            int aliveSameScene = 0;

            // 本机（通常观战时本机已死，这里自然为 0）
            if (LoaclPlayerManager.Instance.IsAlive(CharacterMainControl.Main)) aliveSameScene++;

            if (IsServer)
            {
                foreach (var kv in remoteCharacters)
                {
                    var cmc = kv.Value ? kv.Value.GetComponent<CharacterMainControl>() : null;
                    if (!LoaclPlayerManager.Instance.IsAlive(cmc)) continue;

                    string peerScene = null;
                    if (!_srvPeerScene.TryGetValue(kv.Key, out peerScene) && playerStatuses.TryGetValue(kv.Key, out var st))
                        peerScene = st?.SceneId;

                    if (Spectator.AreSameMap(mySceneId, peerScene)) aliveSameScene++;
                }
            }
            else
            {
                foreach (var kv in clientRemoteCharacters)
                {
                    var cmc = kv.Value ? kv.Value.GetComponent<CharacterMainControl>() : null;
                    if (!LoaclPlayerManager.Instance.IsAlive(cmc)) continue;

                    string peerScene = NetService.Instance.clientPlayerStatuses.TryGetValue(kv.Key, out var st) ? st?.SceneId : null;
                    if (Spectator.AreSameMap(mySceneId, peerScene)) aliveSameScene++;
                }
            }

            bool none = (aliveSameScene <= 0);
            if (none)
                Debug.Log("[SPECTATE] 本地图无人存活 → 退出观战并触发结算");
            return none;
        }

        //传送机投票
        public static void Call_NotifyEntryClicked_ByInvoke(
     MapSelectionView view,
     MapSelectionEntry entry,
     PointerEventData evt // 可传 null（多数情况下安全）
 )
        {
            var mi = typeof(MapSelectionView).GetMethod(
                "NotifyEntryClicked",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(MapSelectionEntry), typeof(PointerEventData) },
                modifiers: null
            );
            if (mi == null)
                throw new MissingMethodException("MapSelectionView.NotifyEntryClicked(MapSelectionEntry, PointerEventData) not found.");

            mi.Invoke(view, new object[] { entry, evt });
        }


        public static IEnumerable<NetPeer> Server_EnumPeersInSameSceneAsHost()
        {
            string hostSceneId = localPlayerStatus != null ? localPlayerStatus.SceneId : null;
            if (string.IsNullOrEmpty(hostSceneId))
                LoaclPlayerManager.Instance.ComputeIsInGame(out hostSceneId);
            if (string.IsNullOrEmpty(hostSceneId)) yield break;

            foreach (var p in netManager.ConnectedPeerList)
            {
                string endPoint = p.EndPoint.ToString();
                string peerScene = null;
                if (!_srvPeerScene.TryGetValue(endPoint, out peerScene) && playerStatuses.TryGetValue(endPoint, out var st))
                    peerScene = st.SceneId;

                if (!string.IsNullOrEmpty(peerScene) && peerScene == hostSceneId)
                    yield return p;
            }
        }




    }
}
