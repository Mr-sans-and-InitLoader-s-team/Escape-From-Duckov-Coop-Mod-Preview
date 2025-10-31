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
using ItemStatsSystem;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static EscapeFromDuckovCoopMod.LootNet;

namespace EscapeFromDuckovCoopMod
{
    public class Host_Handle
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
        public void Server_HandlePlayerDeadTree(Vector3 pos, Quaternion rot, ItemSnapshot snap)
        {
            if (!IsServer) return;

            var tmpRoot = ItemTool.BuildItemFromSnapshot(snap);
            if (!tmpRoot) { Debug.LogWarning("[LOOT] HostDeath BuildItemFromSnapshot failed."); return; }

            var deadPfb = LootManager.Instance.ResolveDeadLootPrefabOnServer();                     // → LootBoxPrefab_Tomb
            var box = InteractableLootbox.CreateFromItem(tmpRoot, pos + Vector3.up * 0.10f, rot, true, deadPfb, false);
            if (box) DeadLootBox.Instance.Server_OnDeadLootboxSpawned(box, null);                   // whoDied=null → aiId=0 → 客户端走“玩家坟碑盒”

            if (tmpRoot && tmpRoot.gameObject) UnityEngine.Object.Destroy(tmpRoot.gameObject);
        }

        //  主机专用入口：本地构造一份与客户端打包一致的“物品树”
        public void Server_HandleHostDeathViaTree(CharacterMainControl who)
        {
            if (!networkStarted || !IsServer || !who) return;
            var item = who.CharacterItem;
            if (!item) return;

            var pos = who.transform.position;
            var rot = (who.characterModel ? who.characterModel.transform.rotation : who.transform.rotation);

            var snap = ItemTool.MakeSnapshot(item);                                     // 本地版“WriteItemSnapshot”
            Server_HandlePlayerDeadTree(pos, rot, snap);
        }







    }
}
