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

﻿using Duckov.Modding;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod
{
    public class ModBehaviour:Duckov.Modding.ModBehaviour
    {
        public Harmony Harmony;
        public void OnEnable()
        {
            Harmony = new Harmony("DETF_COOP");
            Harmony.PatchAll();

            var go = new GameObject("COOP_MOD_1");
            DontDestroyOnLoad(go);

            go.AddComponent<NetService>();
            
            var steamGo = new GameObject("STEAM_NETWORKING_SOCKETS");
            DontDestroyOnLoad(steamGo);
            var steamNetSockets = steamGo.AddComponent<EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager>();
            var steamTransport = steamGo.AddComponent<EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkTransport>();
            steamGo.AddComponent<EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService>();
            
            if (steamNetSockets != null)
            {
                steamNetSockets.Initialize();
            }
            
            if (steamTransport != null)
            {
                steamTransport.Initialize();
            }
            
            var p2pGo = new GameObject("STEAM_P2P_VIRTUAL_NETWORK");
            DontDestroyOnLoad(p2pGo);
            var p2pLoader = p2pGo.AddComponent<EscapeFromDuckovCoopMod.Net.Steam.SteamP2PLoader>();
            p2pLoader.Init();
            Debug.Log("[Mod] Steam P2P虚拟网络系统已初始化");
            
            COOPManager.InitManager();
            go.AddComponent<EscapeFromDuckovCoopMod.ModBehaviourF>();
            Loader();
        }

        public void Loader()
        {

            var go = new GameObject("COOP_MOD_");
            DontDestroyOnLoad(go);

            go.AddComponent<AIRequest>();
            go.AddComponent<Send_ClientStatus>();
            go.AddComponent<HealthM>();
            go.AddComponent<LoaclPlayerManager>();
            go.AddComponent<Send_LoaclPlayerStatus>();
            go.AddComponent<Spectator>();
            go.AddComponent<DeadLootBox>();
            go.AddComponent<LootManager>();
            go.AddComponent<SceneNet>();
            go.AddComponent<ModUI>();
            CoopTool.Init();

            DeferredInit();
        }

        private void DeferredInit()
        {

            SafeInit<SceneNet>(sn => sn.Init());
            SafeInit<LootManager>(lm => lm.Init());
            SafeInit<LoaclPlayerManager>(lpm => lpm.Init());
            SafeInit<HealthM>(hm => hm.Init());
            SafeInit<Send_LoaclPlayerStatus>(s => s.Init());
            SafeInit<Spectator>(s => s.Init());
            SafeInit<ModUI>(ui => ui.Init());
            SafeInit<AIRequest>(a => a.Init());
            SafeInit<Send_ClientStatus>(s => s.Init());
            SafeInit<DeadLootBox>(s => s.Init());
        }

        private void SafeInit<T>(System.Action<T> init) where T : Component
        {
            var c = FindObjectOfType<T>();
            if (c == null) return;
            try { init(c); } catch { }
        }









    }
}
