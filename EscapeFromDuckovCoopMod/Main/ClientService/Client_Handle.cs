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
using static EscapeFromDuckovCoopMod.ModBehaviourF;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod
{
    public class Client_Handle
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
        public void HandleClientStatusUpdate(NetPeer peer, NetPacketReader reader)
        {
            string endPoint = reader.GetString();
            string playerName = reader.GetString();
            bool isInGame = reader.GetBool();
            Vector3 position = reader.GetVector3();
            Quaternion rotation = reader.GetQuaternion();
            string sceneId = reader.GetString();
            string customFaceJson = reader.GetString();

            int equipmentCount = reader.GetInt();
            var equipmentList = new List<EquipmentSyncData>();
            for (int i = 0; i < equipmentCount; i++)
                equipmentList.Add(EquipmentSyncData.Deserialize(reader));

            int weaponCount = reader.GetInt();
            var weaponList = new List<WeaponSyncData>();
            for (int i = 0; i < weaponCount; i++)
                weaponList.Add(WeaponSyncData.Deserialize(reader));

            if (!playerStatuses.ContainsKey(endPoint))
                playerStatuses[endPoint] = new PlayerStatus();

            var st = playerStatuses[endPoint];
            st.EndPoint = endPoint;
            st.PlayerName = playerName;
            st.Latency = peer.Ping;
            st.IsInGame = isInGame;
            st.LastIsInGame = isInGame;
            st.Position = position;
            st.Rotation = rotation;
            if (!string.IsNullOrEmpty(customFaceJson))
                st.CustomFaceJson = customFaceJson;
            st.EquipmentList = equipmentList;
            st.WeaponList = weaponList;
            st.SceneId = sceneId;

            if (isInGame && !remoteCharacters.ContainsKey(endPoint))
            {
               CreateRemoteCharacter.CreateRemoteCharacterAsync(endPoint, position, rotation, customFaceJson).Forget();
                foreach (var e in equipmentList) COOPManager.HostPlayer_Apply.ApplyEquipmentUpdate(endPoint, e.SlotHash, e.ItemId).Forget();
                foreach (var w in weaponList) COOPManager.HostPlayer_Apply.ApplyWeaponUpdate(endPoint, w.SlotHash, w.ItemId).Forget();
            }
            else if (isInGame)
            {
                if (remoteCharacters.TryGetValue(endPoint, out var go) && go != null)
                {
                    go.transform.position = position;
                    go.GetComponentInChildren<CharacterMainControl>().modelRoot.transform.rotation = rotation;
                }
                foreach (var e in equipmentList) COOPManager.HostPlayer_Apply.ApplyEquipmentUpdate(endPoint, e.SlotHash, e.ItemId).Forget();
                foreach (var w in weaponList) COOPManager.HostPlayer_Apply.ApplyWeaponUpdate(endPoint, w.SlotHash, w.ItemId).Forget();
            }

            playerStatuses[endPoint] = st;

            Send_LoaclPlayerStatus.Instance.SendPlayerStatusUpdate();

        }










    }
}
