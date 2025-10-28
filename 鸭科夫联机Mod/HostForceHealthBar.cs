﻿// Escape-From-Duckov-Coop-Mod-Preview
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

using Duckov.UI;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace 鸭科夫联机Mod
{
    // Host-only 强制血条组件
    public sealed class HostForceHealthBar : MonoBehaviour
    {
        private Health _h;
        private float _deadline;
        private int _tries;

        private void OnEnable()
        {
            // 仅主机才需要；客户端已有 AutoRequestHealthBar
            var m = ModBehaviour.Instance;
            if (m == null || !m.networkStarted || !m.IsServer) { enabled = false; return; }
           
            _h = GetComponentInChildren<Health>(true);
            _deadline = Time.time + 5f;   // 最多尝试 5 秒
            _tries = 0;
           
        }

        private void Update()
        {
            if (!_h || Time.time > _deadline) { enabled = false; return; }

            // 每帧抢条子
            try { _h.showHealthBar = true; } catch { }
            try { _h.RequestHealthBar(); } catch { }

            // 一旦拿到 HealthBar 就停
            try
            {
                var miGet = AccessTools.DeclaredMethod(typeof(HealthBarManager), "GetActiveHealthBar", new[] { typeof(global::Health) });
                var hb = miGet?.Invoke(HealthBarManager.Instance, new object[] { _h }) as Duckov.UI.HealthBar;
                if (hb != null) { enabled = false; return; }
            }
            catch { }

            _tries++;
        }
    }

}
