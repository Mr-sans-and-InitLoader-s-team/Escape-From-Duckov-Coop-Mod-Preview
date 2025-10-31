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
using HarmonyLib;
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
    public class Spectator:MonoBehaviour
    {
        private NetService Service => NetService.Instance;
        public static Spectator Instance;

        private bool IsServer => Service != null && Service.IsServer;
        private NetManager netManager => Service?.netManager;
        private NetDataWriter writer => Service?.writer;
        private NetPeer connectedPeer => Service?.connectedPeer;
        private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
        private bool networkStarted => Service != null && Service.networkStarted;
        private Dictionary<string, GameObject> remoteCharacters => Service?.remoteCharacters;
        private Dictionary<string, PlayerStatus> playerStatuses => Service?.playerStatuses;
        private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;
        public bool _spectatorEndOnVotePending = false;

        public bool _skipSpectatorForNextClosure = false;

        public bool _spectatorActive = false;
        public List<CharacterMainControl> _spectateList = new List<CharacterMainControl>();
        public int _spectateIdx = -1;
        public float _spectateNextSwitchTime = 0f;
        public global::DamageInfo _lastDeathInfo;

        public void Init()
        {
            Instance = this;
        }

        public bool TryEnterSpectatorOnDeath(global::DamageInfo dmgInfo)
        {
            var main = CharacterMainControl.Main;
            if (!LevelManager.LevelInited || main == null) return false;

            BuildSpectateList(exclude: main);
            Debug.Log("观战: " + _spectateList.Count);

            if (_spectateList.Count <= 0) return false;     // 没人可观战 -> 让结算继续

            _lastDeathInfo = dmgInfo;
            _spectatorActive = true;
            _spectateIdx = 0;

            if (GameCamera.Instance) GameCamera.Instance.SetTarget(_spectateList[_spectateIdx]);

            if (SceneNet.Instance.sceneVoteActive)
                _spectatorEndOnVotePending = true;

            return true; // 告诉前缀：拦截结算，启用观战
        }

        public void BuildSpectateList(CharacterMainControl exclude)
        {
            _spectateList.Clear();

            string mySceneId = localPlayerStatus != null ? localPlayerStatus.SceneId : null;
            if (string.IsNullOrEmpty(mySceneId))
                LoaclPlayerManager.Instance.ComputeIsInGame(out mySceneId);
            var myK = CanonicalizeSceneId(mySceneId);

            int cand = 0, kept = 0;

            if (IsServer)
            {
                foreach (var kv in remoteCharacters)
                {
                    var go = kv.Value;
                    var cmc = go ? go.GetComponent<CharacterMainControl>() : null;
                    if (!LoaclPlayerManager.Instance.IsAlive(cmc) || cmc == exclude) continue;
                    cand++;

                    string peerScene = null;
                    if (!SceneM._srvPeerScene.TryGetValue(kv.Key, out peerScene) && playerStatuses.TryGetValue(kv.Key, out var st))
                        peerScene = st?.SceneId;

                    if (AreSameMap(mySceneId, peerScene))
                    {
                        _spectateList.Add(cmc);
                        kept++;
                    }
                }
            }
            else
            {
                foreach (var kv in clientRemoteCharacters)
                {
                    var go = kv.Value;
                    var cmc = go ? go.GetComponent<CharacterMainControl>() : null;
                    if (!LoaclPlayerManager.Instance.IsAlive(cmc) || cmc == exclude) continue;
                    cand++;

                    //  先从 clientPlayerStatuses 拿 SceneId
                    string peerScene = null;
                    if (NetService.Instance.clientPlayerStatuses.TryGetValue(kv.Key, out var st))
                        peerScene = st?.SceneId;

                    //  兜底：再从 _cliLastSceneIdByPlayer 回忆一次
                    if (string.IsNullOrEmpty(peerScene))
                        SceneNet.Instance._cliLastSceneIdByPlayer.TryGetValue(kv.Key, out peerScene);

                    if (AreSameMap(mySceneId, peerScene))
                    {
                        _spectateList.Add(cmc);
                        kept++;
                    }
                }
            }

            Debug.Log("[SPECTATE] 候选=" + cand + ", 同图保留=" + kept + ", mySceneId=" + mySceneId + " (canon=" + myK + ")");
        }

        //说实话这个方法没多大用   //说实话这个方法没多大用   //说实话这个方法没多大用   //说实话这个方法没多大用
        public static string CanonicalizeSceneId(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            var s = id.Trim().ToLowerInvariant();

            // 反复剔除常见后缀
            string[] suffixes = { "_main", "_gameplay", "_core", "_scene", "_lod0", "_lod", "_client", "_server" };
            bool removed;
            do
            {
                removed = false;
                foreach (var suf in suffixes)
                {
                    if (s.EndsWith(suf))
                    {
                        s = s.Substring(0, s.Length - suf.Length);
                        removed = true;
                    }
                }
            } while (removed);

            while (s.Contains("__")) s = s.Replace("__", "_");

            var parts = s.Split('_');
            if (parts.Length >= 2 && parts[0] == "level")
                s = parts[0] + "_" + parts[1];

            if (s == "base" || s.StartsWith("base_")) s = "base";
            return s;
        }

        public static bool AreSameMap(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return a == b;
            return CanonicalizeSceneId(a) == CanonicalizeSceneId(b);
        }

        // —— 用已有字典反查该 CMC 是否属于“本地图”的远端玩家 —— 
        public bool IsInSameScene(CharacterMainControl cmc)
        {
            if (!cmc) return false;
            string mySceneId = localPlayerStatus != null ? localPlayerStatus.SceneId : null;
            if (string.IsNullOrEmpty(mySceneId)) return true; // 降级：无ID时不做过滤

            if (IsServer)
            {
                foreach (var kv in remoteCharacters)
                {
                    if (kv.Value == null) continue;
                    var v = kv.Value.GetComponent<CharacterMainControl>();
                    if (v == cmc)
                    {
                        if (playerStatuses.TryGetValue(kv.Key, out var st) && st != null)
                            return st.SceneId == mySceneId;
                        return false;
                    }
                }
            }
            else
            {
                foreach (var kv in clientRemoteCharacters)
                {
                    if (kv.Value == null) continue;
                    var v = kv.Value.GetComponent<CharacterMainControl>();
                    if (v == cmc)
                    {
                        if (NetService.Instance.clientPlayerStatuses.TryGetValue(kv.Key, out var st) && st != null)
                            return st.SceneId == mySceneId;
                        return false;
                    }
                }
            }
            return false;
        }

        public void SpectateNext()
        {
            if (_spectateList.Count == 0) return;
            _spectateIdx = (_spectateIdx + 1) % _spectateList.Count;
            if (GameCamera.Instance) GameCamera.Instance.SetTarget(_spectateList[_spectateIdx]);
        }

        public void SpectatePrev()
        {
            if (_spectateList.Count == 0) return;
            _spectateIdx = (_spectateIdx - 1 + _spectateList.Count) % _spectateList.Count;
            if (GameCamera.Instance) GameCamera.Instance.SetTarget(_spectateList[_spectateIdx]);
        }

        public void EndSpectatorAndShowClosure()
        {
            _spectatorEndOnVotePending = false;

            if (!_spectatorActive) return;
            _spectatorActive = false;
            _skipSpectatorForNextClosure = true;

            try
            {
                var t = AccessTools.TypeByName("Duckov.UI.ClosureView");
                var mi = AccessTools.Method(t, "ShowAndReturnTask", new System.Type[] { typeof(global::DamageInfo), typeof(float) });
                if (mi != null)
                {
                    ((UniTask)mi.Invoke(null, new object[] { _lastDeathInfo, 0.5f })).Forget();
                }
            }
            catch { }
        }





    }
}
