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
using ItemStatsSystem;
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
    public class GrenadeM
    {
        private NetService Service => NetService.Instance;

        private bool IsServer => Service != null && Service.IsServer;
        private NetManager netManager => Service?.netManager;
        private NetDataWriter writer => Service?.writer;
        private NetPeer connectedPeer => Service?.connectedPeer;
        private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
        private bool networkStarted => Service != null && Service.networkStarted;
        // ---------------- Grenade caches ----------------
        public readonly Dictionary<int, Grenade> prefabByTypeId = new Dictionary<int, Grenade>();
        private readonly Dictionary<uint, Grenade> serverGrenades = new Dictionary<uint, Grenade>();
        private readonly Dictionary<uint, GameObject> clientGrenades = new Dictionary<uint, GameObject>();
        private uint nextGrenadeId = 1;

        public struct PendingSpawn
        {
            public uint id; public int typeId;
            public Vector3 start, vel; public bool create; public float shake, dmg;
            public bool delayOnHit; public float delay; public bool isMine; public float mineRange;
            public float expireAt;
        }

        private readonly List<PendingSpawn> pending = new List<PendingSpawn>();
        private float pendingTick;

        public static void AddNetGrenadeTag(GameObject go, uint id)
        {
            if (!go) return;
            var tag = go.GetComponent<NetGrenadeTag>() ?? go.AddComponent<NetGrenadeTag>();
            tag.id = id;
        }


        public void HandleGrenadeExplode(NetPacketReader r)
        {
            uint id = r.GetUInt();
            Vector3 pos = r.GetV3cm();
            float dmg = r.GetFloat();
            float shake = r.GetFloat();
            if (clientGrenades.TryGetValue(id, out var go) && go)
            {
                go.SendMessage("Explode", SendMessageOptions.DontRequireReceiver);
               GameObject.Destroy(go, 0.1f);
                clientGrenades.Remove(id);
            }
        }

        // 客户端：前缀调用
        public void Net_OnClientThrow(
            Skill_Grenade skill, int typeId, string prefabType, string prefabName,
            Vector3 startPoint, Vector3 velocity,
            bool createExplosion, float shake, float damageRange,
            bool delayFromCollide, float delayTime, bool isLandmine, float landmineRange)
        {
            if (IsServer || connectedPeer == null) return;
            writer.Reset();
            writer.Put((byte)Op.GRENADE_THROW_REQUEST);
            writer.Put("local"); // 你的本地ID，随意
            writer.Put(typeId);
            writer.Put(prefabType ?? string.Empty);
            writer.Put(prefabName ?? string.Empty);
            writer.PutV3cm(startPoint);
            writer.PutV3cm(velocity);
            writer.Put(createExplosion);
            writer.Put(shake);
            writer.Put(damageRange);
            writer.Put(delayFromCollide);
            writer.Put(delayTime);
            writer.Put(isLandmine);
            writer.Put(landmineRange);
            
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

        // 客户端：生成视觉手雷
        // 客户端：收到主机广播的手雷生成
        public void HandleGrenadeSpawn(NetPacketReader r)
        {
            uint id = r.GetUInt();
            int typeId = r.GetInt();

            _ = r.GetString(); // prefabType (ignored)
            _ = r.GetString(); // prefabName (ignored)

            Vector3 start = r.GetV3cm();
            Vector3 vel = r.GetV3cm();
            bool create = r.GetBool();
            float shake = r.GetFloat();
            float dmg = r.GetFloat();
            bool delayOnHit = r.GetBool();
            float delay = r.GetFloat();
            bool isMine = r.GetBool();
            float mineRange = r.GetFloat();

            // 快路径：typeId 命中缓存，直接生成
            if (prefabByTypeId.TryGetValue(typeId, out var prefab) && prefab)
            {
               CoopTool.CacheGrenadePrefab(typeId, prefab);

                var g = GameObject.Instantiate(prefab, start, Quaternion.identity);
                g.createExplosion = create;
                g.explosionShakeStrength = shake;
                g.damageRange = dmg;
                g.delayFromCollide = delayOnHit;
                g.delayTime = delay;
                g.isLandmine = isMine;
                g.landmineTriggerRange = mineRange;
                g.SetWeaponIdInfo(typeId);
                g.Launch(start, vel, null, true);
                AddNetGrenadeTag(g.gameObject, id);

                clientGrenades[id] = g.gameObject;
                return;
            }

            // 慢路径：立即异步精确解析（只按 typeId，不进 pending）
            ResolveAndSpawnClientAsync(
                id, typeId, start, vel,
                create, shake, dmg, delayOnHit, delay, isMine, mineRange
            ).Forget();
        }

        // 客户端：只按 typeId 精确解析（Item → Skill_Grenade → grenadePfb）并生成
        public async Cysharp.Threading.Tasks.UniTask ResolveAndSpawnClientAsync(
            uint id, int typeId, Vector3 start, Vector3 vel,
            bool create, float shake, float dmg, bool delayOnHit, float delay,
            bool isMine, float mineRange)
        {
            var prefab = await EscapeFromDuckovCoopMod.COOPManager.GetGrenadePrefabByItemIdAsync(typeId);
            if (!prefab)
            {
                UnityEngine.Debug.LogError("[CLIENT] grenade prefab exact resolve failed: typeId=" + typeId);
                return;
            }

            CoopTool.CacheGrenadePrefab(typeId, prefab);

            var g = GameObject.Instantiate(prefab, start, Quaternion.identity);
            g.createExplosion = create;
            g.explosionShakeStrength = shake;
            g.damageRange = dmg;
            g.delayFromCollide = delayOnHit;
            g.delayTime = delay;
            g.isLandmine = isMine;
            g.landmineTriggerRange = mineRange;
            g.SetWeaponIdInfo(typeId);
            g.Launch(start, vel, null, true);
            AddNetGrenadeTag(g.gameObject, id);

            clientGrenades[id] = g.gameObject;
        }

        // 只按 typeId 解析：从 itemId → Item → Skill_Grenade.grenadePfb
        public async Cysharp.Threading.Tasks.UniTask ResolveAndSpawnClientAsync(PendingSpawn p)
        {
            var prefab = await EscapeFromDuckovCoopMod.COOPManager.GetGrenadePrefabByItemIdAsync(p.typeId);
            if (!prefab)
            {
                UnityEngine.Debug.LogError("[CLIENT] grenade prefab exact resolve failed: typeId=" + p.typeId);
                return;
            }

            // 回灌缓存（只按 typeId）
            CoopTool.CacheGrenadePrefab(p.typeId, prefab);

            // 实例化 + 参数回放 + 启动
            var g = GameObject.Instantiate(prefab, p.start, Quaternion.identity);
            g.createExplosion = p.create;
            g.explosionShakeStrength = p.shake;
            g.damageRange = p.dmg;
            g.delayFromCollide = p.delayOnHit;
            g.delayTime = p.delay;
            g.isLandmine = p.isMine;
            g.landmineTriggerRange = p.mineRange;
            g.SetWeaponIdInfo(p.typeId);

            g.Launch(p.start, p.vel, null, true);

            clientGrenades[p.id] = g.gameObject;
        }

        // 客户端端：统一处理待决的投掷物生成（只看 typeId，不再用名字）
        public void ProcessPendingGrenades()
        {
            if (!networkStarted || IsServer) return;

            pendingTick += Time.unscaledDeltaTime;
            if (pendingTick < 0.2f) return;
            pendingTick = 0f;

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];

                // 过期就丢弃
                if (Time.unscaledTime > p.expireAt)
                {
                    UnityEngine.Debug.LogError("[CLIENT] grenade prefab resolve timeout: typeId=" + p.typeId);
                    pending.RemoveAt(i);
                    continue;
                }

                // 快路径：本地已缓存（只按 typeId）
                if (prefabByTypeId.TryGetValue(p.typeId, out var prefab) && prefab)
                {
                    CoopTool.CacheGrenadePrefab(p.typeId, prefab);

                    var g = GameObject.Instantiate(prefab, p.start, Quaternion.identity);
                    g.createExplosion = p.create;
                    g.explosionShakeStrength = p.shake;
                    g.damageRange = p.dmg;
                    g.delayFromCollide = p.delayOnHit;
                    g.delayTime = p.delay;
                    g.isLandmine = p.isMine;
                    g.landmineTriggerRange = p.mineRange;
                    g.SetWeaponIdInfo(p.typeId);
                    g.Launch(p.start, p.vel, null, true);
                    AddNetGrenadeTag(g.gameObject, p.id);

                    clientGrenades[p.id] = g.gameObject;
                    pending.RemoveAt(i);
                    continue;
                }

                // 慢路径：未命中缓存 → 异步精确解析（只按 typeId）
                ResolveAndSpawnClientAsync(p).Forget();
                pending.RemoveAt(i);
            }
        }


        // 主机：处理请求
        public void HandleGrenadeThrowRequest(NetPeer peer, NetPacketReader r)
        {
            string shooterId = r.GetString();
            int typeId = r.GetInt();
            string prefabType = r.GetString();   // 仍读取但不使用
            string prefabName = r.GetString();   // 仍读取但不使用
            Vector3 start = r.GetV3cm();
            Vector3 vel = r.GetV3cm();
            bool create = r.GetBool();
            float shake = r.GetFloat();
            float dmg = r.GetFloat();
            bool delayOnHit = r.GetBool();
            float delay = r.GetFloat();
            bool isMine = r.GetBool();
            float mineRange = r.GetFloat();

            HandleGrenadeThrowRequestAsync(peer, typeId, start, vel,
                create, shake, dmg, delayOnHit, delay, isMine, mineRange).Forget();
        }

        // 服务器端：接到客户端投掷请求的处理 —— 不信任客户端数值，全按服务器默认模板来
        // 服务器端：接到客户端投掷请求 -> 用服务器模板灌入 Grenade
        private async Cysharp.Threading.Tasks.UniTask HandleGrenadeThrowRequestAsync(
            NetPeer peer, int typeId, Vector3 start, Vector3 vel,
            bool _create, float _shake, float _dmg, bool _delayOnHit, float _delay, bool _isMine, float _mineRange)
        {
            // 解析 prefab（只按 typeId）
            Grenade prefab = null;
            if (!prefabByTypeId.TryGetValue(typeId, out prefab) || !prefab)
                prefab = await EscapeFromDuckovCoopMod.COOPManager.GetGrenadePrefabByItemIdAsync(typeId);

            if (!prefab)
            {
                // 兜底：让客户端自己解析（仍读空字符串）
                uint fid = nextGrenadeId++;
                Server_BroadcastGrenadeSpawn(fid, typeId, string.Empty, string.Empty, start, vel,
                    _create, _shake, _dmg, _delayOnHit, _delay, _isMine, _mineRange);
                return;
            }

            CoopTool.CacheGrenadePrefab(typeId, prefab);

            // 读取服务器模板（含整包 DamageInfo）
            var tpl = await ReadGrenadeTemplateAsync(typeId);

            // 找到这位 peer 对应的服务器角色（owner）
            var fromChar = CoopTool.TryGetRemoteCharacterForPeer(peer);
            // 若还没有映射，为了验证也可临时用主机自己： if (fromChar == null) fromChar = CharacterMainControl.Main;
            var g = GameObject.Instantiate(prefab, start, Quaternion.identity);
            g.createExplosion = tpl.create;
            g.explosionShakeStrength = tpl.shake;
            g.damageRange = tpl.effectRange;
            g.delayFromCollide = tpl.delayFromCollide;
            g.delayTime = tpl.delay;
            g.isLandmine = tpl.isMine;
            g.landmineTriggerRange = tpl.mineRange;

            var di = tpl.di;
            try { di.fromCharacter = fromChar; } catch { }
            try { di.fromWeaponItemID = typeId; } catch { }
            g.damageInfo = di;

            g.SetWeaponIdInfo(typeId);     // 无所谓冗余，多一道保险
            g.Launch(start, vel, fromChar, true);
        }

        // 服务端广播
        private void Server_BroadcastGrenadeSpawn(uint id, Grenade g, int typeId, string prefabType, string prefabName, Vector3 start, Vector3 vel)
        {
            writer.Reset();
            writer.Put((byte)Op.GRENADE_SPAWN);
            writer.Put(id);
            writer.Put(typeId);
            writer.Put(prefabType ?? string.Empty);
            writer.Put(prefabName ?? string.Empty);
            writer.PutV3cm(start);
            writer.PutV3cm(vel);
            writer.Put(g.createExplosion);
            writer.Put(g.explosionShakeStrength);
            writer.Put(g.damageRange);
            writer.Put(g.delayFromCollide);
            writer.Put(g.delayTime);
            writer.Put(g.isLandmine);
            writer.Put(g.landmineTriggerRange);
            CoopTool.BroadcastReliable(writer);
        }
        private void Server_BroadcastGrenadeSpawn(uint id, int typeId, string prefabType, string prefabName, Vector3 start, Vector3 vel,
            bool create, float shake, float dmg, bool delayOnHit, float delay, bool isMine, float mineRange)
        {
            writer.Reset();
            writer.Put((byte)Op.GRENADE_SPAWN);
            writer.Put(id);
            writer.Put(typeId);
            writer.Put(prefabType ?? string.Empty);
            writer.Put(prefabName ?? string.Empty);
            writer.PutV3cm(start);
            writer.PutV3cm(vel);
            writer.Put(create);
            writer.Put(shake);
            writer.Put(dmg);
            writer.Put(delayOnHit);
            writer.Put(delay);
            writer.Put(isMine);
            writer.Put(mineRange);
            CoopTool.BroadcastReliable(writer);
        }
        private void Server_BroadcastGrenadeExplode(uint id, Grenade g, Vector3 pos)
        {
            writer.Reset();
            writer.Put((byte)Op.GRENADE_EXPLODE);
            writer.Put(id);
            writer.PutV3cm(pos);
            writer.Put(g.damageRange);
            writer.Put(g.explosionShakeStrength);
            CoopTool.BroadcastReliable(writer);
        }

        public void Server_OnGrenadeLaunched(Grenade g, Vector3 start, Vector3 vel, int typeId /*, CharacterMainControl owner 可选 */)
        {
            // 兜底：发现字段异常（被 prefab 默认 0 覆盖）就再按服务器默认灌一遍
            if (g.damageRange <= 0f)
            {
                ReadGrenadeTemplateAsync(typeId).ContinueWith(defs =>
                {
                    g.damageInfo = defs.di;
                    g.createExplosion = defs.create;
                    g.explosionShakeStrength = defs.shake;
                    g.damageRange = defs.effectRange;
                    g.delayFromCollide = defs.delayFromCollide;
                    g.delayTime = defs.delay;
                    g.isLandmine = defs.isMine;
                    g.landmineTriggerRange = defs.mineRange;

                    var di = g.damageInfo;
                    try { di.fromWeaponItemID = typeId; } catch { }
                    g.damageInfo = di;
                }).Forget();
            }

            uint id = 0; foreach (var kv in serverGrenades) if (kv.Value == g) { id = kv.Key; break; }
            if (id == 0) { id = nextGrenadeId++; serverGrenades[id] = g; }
            const string prefabType = ""; const string prefabName = "";
            Server_BroadcastGrenadeSpawn(id, g, typeId, prefabType, prefabName, start, vel);
        }


        public void Server_OnGrenadeExploded(Grenade g)
        {
            uint id = 0; foreach (var kv in serverGrenades) if (kv.Value == g) { id = kv.Key; break; }
            if (id == 0) return;
            Server_BroadcastGrenadeExplode(id, g, g.transform.position);
        }


        // 服务器：根据 itemId 读取 Skill_Grenade 的“整包模板”
        // 返回：di（整包 DamageInfo）+ 其它 Grenade 字段（OnRelease 里会赋的）
        private async Cysharp.Threading.Tasks.UniTask<(global::DamageInfo di, bool create, float shake, float effectRange, bool delayFromCollide, float delay, bool isMine, float mineRange)>
            ReadGrenadeTemplateAsync(int typeId)
        {
            Item item = null;
            try
            {
                item = await EscapeFromDuckovCoopMod.COOPManager.GetItemAsync(typeId);
                var skill = item ? item.GetComponent<Skill_Grenade>() : null;

                // 安全默认
                global::DamageInfo di = default;
                bool create = true;
                float shake = 1f;
                float effectRange = 3f;
                bool delayFromCollide = false;
                float delay = 0f;
                bool isMine = false;
                float mineRange = 0f;

                if (skill != null)
                {
                    di = skill.damageInfo;
                    create = skill.createExplosion;
                    shake = skill.explosionShakeStrength;
                    delayFromCollide = skill.delayFromCollide;
                    delay = skill.delay;
                    isMine = skill.isLandmine;
                    mineRange = skill.landmineTriggerRange;

                    // effectRange 在 skillContext 里
                    try
                    {
                        var ctx = skill.SkillContext;
                        //if (ctx != null)
                        {
                            var fEff = AccessTools.Field(ctx.GetType(), "effectRange");
                            if (fEff != null) effectRange = (float)fEff.GetValue(ctx);
                        }
                    }
                    catch { }
                }

                try { di.fromWeaponItemID = typeId; } catch { }

                return (di, create, shake, effectRange, delayFromCollide, delay, isMine, mineRange);
            }
            finally
            {
                if (item && item.gameObject) UnityEngine.Object.Destroy(item.gameObject);
            }
        }











    }
}
