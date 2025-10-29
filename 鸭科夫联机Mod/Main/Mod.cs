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

using Cysharp.Threading.Tasks;
using Duckov;
using Duckov.Scenes;
using Duckov.UI;
using Duckov.Utilities;
using Duckov.Weathers;
using Eflatun.SceneReference;
using HarmonyLib;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Unity.Burst.Intrinsics.X86.Avx;
using Object = UnityEngine.Object;

namespace 鸭科夫联机Mod
{
    public static class NetDataExtensions
    {
        public static void PutVector3(this NetDataWriter writer, Vector3 vector)
        {
            writer.Put(vector.x);
            writer.Put(vector.y);
            writer.Put(vector.z);
        }

        public static Vector3 GetVector3(this NetPacketReader reader)
        {
            return new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Quaternion NormalizeSafe(Quaternion q)
        {
            if (!Finite(q.x) || !Finite(q.y) || !Finite(q.z) || !Finite(q.w))
                return Quaternion.identity;

            // 防 0 四元数
            float mag2 = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (mag2 < 1e-12f) return Quaternion.identity;

            float inv = 1.0f / Mathf.Sqrt(mag2);
            q.x *= inv; q.y *= inv; q.z *= inv; q.w *= inv;
            return q;
        }

        public static void PutQuaternion(this NetDataWriter writer, Quaternion q)
        {
            q = NormalizeSafe(q);
            writer.Put(q.x); writer.Put(q.y); writer.Put(q.z); writer.Put(q.w);
        }

        public static Quaternion GetQuaternion(this NetPacketReader reader)
        {
            var q = new Quaternion(reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            return NormalizeSafe(q);
        }
    }

    public static class LocalHitKillFx
    {
        static System.Reflection.FieldInfo _fiHurtVisual;              // CharacterModel.hurtVisual (private global::HurtVisual)
        static System.Reflection.MethodInfo _miHvOnHurt, _miHvOnDead;  // HurtVisual.OnHurt / OnDead (private)
        static System.Reflection.MethodInfo _miHmOnHit, _miHmOnKill;   // HitMarker.OnHit / OnKill (private)

        static void EnsureHurtVisualBindings(object characterModel, object hv)
        {
            if (_fiHurtVisual == null && characterModel != null)
                _fiHurtVisual = characterModel.GetType()
                    .GetField("hurtVisual", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (hv != null)
            {
                var t = hv.GetType();
                if (_miHvOnHurt == null)
                    _miHvOnHurt = t.GetMethod("OnHurt", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (_miHvOnDead == null)
                    _miHvOnDead = t.GetMethod("OnDead", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            }
        }

        static float _lastBaseDamageForPop = 0f;
        public static void RememberLastBaseDamage(float v)
        {
            if (v > 0.01f) _lastBaseDamageForPop = v;
        }

        static object FindHurtVisualOn(global::CharacterMainControl cmc)
        {
            if (!cmc) return null;
            var model = cmc.characterModel; // 公开字段可直接取到 CharacterModel
            if (model == null) return null;

            object hv = null;
            try
            {
                EnsureHurtVisualBindings(model, null);
                if (_fiHurtVisual != null)
                    hv = _fiHurtVisual.GetValue(model);
            }
            catch { }

            // 兜底场景里找（有些模型可能没填字段）
            if (hv == null)
            {
                try { hv = model.GetComponentInChildren(typeof(global::HurtVisual), true); } catch { }
            }
            return hv;
        }

        static object FindHitMarkerSingleton()
        {
            try { return UnityEngine.Object.FindObjectOfType(typeof(global::HitMarker), true); }
            catch { return null; }
        }

        static void PlayHurtVisual(object hv, global::DamageInfo di, bool predictedDead)
        {
            if (hv == null) return;
            EnsureHurtVisualBindings(null, hv);

            try { _miHvOnHurt?.Invoke(hv, new object[] { di }); } catch { }
            if (predictedDead)
            {
                try { _miHvOnDead?.Invoke(hv, new object[] { di }); } catch { }
            }
        }
        public static void PopDamageText(Vector3 hintPos, global::DamageInfo di)
        {
            try
            {
                if (global::FX.PopText.instance)
                {
                    var look = GameplayDataSettings.UIStyle.GetElementDamagePopTextLook(global::ElementTypes.physics);
                    float size = (di.crit > 0) ? look.critSize : look.normalSize;
                    var sprite = (di.crit > 0) ? GameplayDataSettings.UIStyle.CritPopSprite : null;
                    Debug.Log(di.damageValue +" "+di.finalDamage);
                    float _display = di.damageValue;
                    // 某些路径里 DamageInfo.damageValue 会被归一化为 1；为避免弹字恒为 1.0，做一个兜底：
                    if (_display <= 1.001f && _lastBaseDamageForPop > 0f)
                    {
                        float critMul = (di.crit > 0 && di.critDamageFactor > 0f) ? di.critDamageFactor : 1f;
                        _display = Mathf.Max(_display, _lastBaseDamageForPop * critMul);
                    }
                    string text = (_display > 0f) ? _display.ToString("F1") : "HIT";
                    global::FX.PopText.Pop(text, hintPos, look.color, size, sprite);
                }
            }
            catch { }
        }

        // 只在“本地命中路径”里，必要时把 fromCharacter 强制设为 Main，以满足 HitMarker 的判断
        static void PlayUiHitKill(global::DamageInfo di, bool predictedDead, bool forceLocalMain)
        {
            var hm = FindHitMarkerSingleton();
            if (hm == null) return;

            if (_miHmOnHit == null)
                _miHmOnHit = hm.GetType().GetMethod("OnHit", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_miHmOnKill == null)
                _miHmOnKill = hm.GetType().GetMethod("OnKill", BindingFlags.Instance | BindingFlags.NonPublic);

            if (forceLocalMain)
            {
                try
                {
                    if (di.fromCharacter == null || di.fromCharacter != global::CharacterMainControl.Main)
                        di.fromCharacter = global::CharacterMainControl.Main;
                }
                catch { }
            }

            try { _miHmOnHit?.Invoke(hm, new object[] { di }); } catch { }
            if (predictedDead)
            {
                try { _miHmOnKill?.Invoke(hm, new object[] { di }); } catch { }
            }
        }

        /// <summary>
        /// 客户端本地：玩家 → AI 命中（子弹/爆炸都可）。在“已拦截伤害”的前缀里调用。
        /// </summary>
        public static void ClientPlayForAI(global::CharacterMainControl victim, global::DamageInfo di, bool predictedDead)
        {
            // 1) AI 模型上的受击/死亡可视化（私有 OnHurt/OnDead）
            var hv = FindHurtVisualOn(victim);
            PlayHurtVisual(hv, di, predictedDead);

            // 2) UI 命中/击杀标记（私有 OnHit/OnKill）——只在本地命中路径强制 fromCharacter=Main
            PlayUiHitKill(di, predictedDead, forceLocalMain: true);

            // 3) 伤害数字
            var pos = (di.damagePoint.sqrMagnitude > 1e-6f ? di.damagePoint : victim.transform.position) + Vector3.up * 2f;
            PopDamageText(pos, di);
        }

        /// <summary>
        /// 客户端本地：玩家 → 场景可破坏物（HSB）
        /// </summary>
        public static void ClientPlayForDestructible(global::HealthSimpleBase hs, global::DamageInfo di, bool predictedDead)
        {
            // 复用 UI 命中/击杀标记（为保持手感，障碍物也给个命中标，但仍仅在本地命中时触发）
            PlayUiHitKill(di, predictedDead, forceLocalMain: true);

            // 伤害数字（位置优先用命中点）
            var basePos = hs ? hs.transform.position : Vector3.zero;
            var pos = (di.damagePoint.sqrMagnitude > 1e-6f ? di.damagePoint : basePos) + Vector3.up * 2f;
            PopDamageText(pos, di);
        }
    }

    public static class LootboxDetectUtil
    {
        public static bool IsPrivateInventory(ItemStatsSystem.Inventory inv)
        {
            if (inv == null) return false;
            if (ReferenceEquals(inv, PlayerStorage.Inventory)) return true;  // 仓库
            if (ReferenceEquals(inv, PetProxy.PetInventory)) return true;    // 宠物包
            return false;
        }

        public static bool IsLootboxInventory(Inventory inv)
        {
            if (inv == null) return false;
            // 排除私有库存（仓库/宠物包）
            if (IsPrivateInventory(inv)) return false;

            var dict = InteractableLootbox.Inventories;
            if (dict != null)
            {
                foreach (var kv in dict)
                    if (kv.Value == inv) return true;
            }
            var boxes = Object.FindObjectsOfType<InteractableLootbox>(true);
            foreach (var b in boxes)
                if (b && b.Inventory == inv) return true;

            return false;
        }
    }

    public static class NetPack_Projectile
    {
        public static void PutProjectilePayload(this LiteNetLib.Utils.NetDataWriter w, in ProjectileContext c)
        {
            w.Put(true); // hasPayload
                         // 基础
            w.Put(c.damage); w.Put(c.critRate); w.Put(c.critDamageFactor);
            w.Put(c.armorPiercing); w.Put(c.armorBreak);
            // 元素
            w.Put(c.element_Physics); w.Put(c.element_Fire);
            w.Put(c.element_Poison); w.Put(c.element_Electricity); w.Put(c.element_Space);
            // 爆炸/状态
            w.Put(c.explosionRange); w.Put(c.explosionDamage);
            w.Put(c.buffChance); w.Put(c.bleedChance);
            // 其它
            w.Put(c.penetrate);
            w.Put(c.fromWeaponItemID);
        }

        // 主机/客户端共用：读取 ProjectileContext 关键参数
        public static bool TryGetProjectilePayload(NetPacketReader r, ref ProjectileContext c)
        {
            if (r.AvailableBytes < 1) return false;
            if (!r.GetBool()) return false; // hasPayload
                                            // 14 个 float + 2 个 int = 64 字节
            if (r.AvailableBytes < 64) return false;

            c.damage = r.GetFloat(); c.critRate = r.GetFloat(); c.critDamageFactor = r.GetFloat();
            c.armorPiercing = r.GetFloat(); c.armorBreak = r.GetFloat();

            c.element_Physics = r.GetFloat(); c.element_Fire = r.GetFloat();
            c.element_Poison = r.GetFloat(); c.element_Electricity = r.GetFloat(); c.element_Space = r.GetFloat();

            c.explosionRange = r.GetFloat(); c.explosionDamage = r.GetFloat();
            c.buffChance = r.GetFloat(); c.bleedChance = r.GetFloat();

            c.penetrate = r.GetInt();
            c.fromWeaponItemID = r.GetInt();
            return true;
        }
    }

    public enum EquipKind { None, Armor, Helmat, FaceMask, Backpack, Headset } //废弃

    public enum Op : byte
    {
        PLAYER_STATUS_UPDATE = 1,
        CLIENT_STATUS_UPDATE = 2,
        POSITION_UPDATE = 3,
        ANIM_SYNC = 4,
        EQUIPMENT_UPDATE = 5,
        PLAYERWEAPON_UPDATE = 6,
        FIRE_REQUEST = 7,
        FIRE_EVENT = 8,
        GRENADE_THROW_REQUEST = 9,
        GRENADE_SPAWN = 10,
        GRENADE_EXPLODE = 11,
        ITEM_DROP_REQUEST = 12,
        ITEM_SPAWN = 13,
        ITEM_PICKUP_REQUEST = 14,
        ITEM_DESPAWN = 15,
        PLAYER_HEALTH_REPORT = 16,     // 客户端 -> 主机：上传自己当前(max,curr)                  
        AUTH_HEALTH_SELF = 17,    // 主机 -> 某个客户端：把“你自己本地人物”的(max,cur)设为权威值
        AUTH_HEALTH_REMOTE = 18,  // 主机 -> 所有客户端：某位玩家的(max,cur)用于远端展示（带 playerId）
        SCENE_VOTE_START = 19,  // 主机 -> 全体：开始投票（下发目标 SceneID、Curtain GUID 等）
        SCENE_READY_SET = 20,  // 客户端 -> 主机：我切换准备；主机 -> 全体：某人准备状态改变
        SCENE_BEGIN_LOAD = 21,  // 主机 -> 全体：统一开始加载
        SCENE_CANCEL = 22,  // 主机 -> 全体：取消投票
        SCENE_READY = 23,
        REMOTE_CREATE = 24,
        REMOTE_DESPAWN = 25,

        DOOR_REQ_SET = 206,  // 客户端 -> 主机：请求把某个门设为开/关
        DOOR_STATE = 207,  // 主机 -> 全体：下发某个门的状态（单条更新）

        LOOT_REQ_SLOT_UNPLUG = 208,   // 从某个物品的某个slot里拔出附件
        LOOT_REQ_SLOT_PLUG = 209,     // 往某个物品的某个slot里装入附件（随包带附件snapshot）

        SCENE_VOTE_REQ = 210,   // 客户端 -> 主机：请求发起场景投票

        AI_TRANSFORM_SNAPSHOT = 233,
        AI_SEED_SNAPSHOT = 230,   // 主机 -> 所有客户端：场景种子 + 每个Root的派生种子
        AI_FREEZE_TOGGLE = 231,   // （可选）切换冻结
        AI_LOADOUT_SNAPSHOT = 232,
        AI_ANIM_SNAPSHOT = 234,
        AI_ATTACK_SWING = 235,
        AI_ATTACK_TELL = 236,   // 主机 -> 客户端：AI 攻击前红光预警
        AI_HEALTH_SYNC = 237,
        AI_NAME_ICON = 238,//  主机 -> 客户端：仅图标/名字热修复
        AI_SEED_PATCH = 227,

        DEAD_LOOT_DESPAWN = 247, // 主机 -> 客户端：AI 死亡掉落的箱子被移除（可选，先不强制使用）
        DEAD_LOOT_SPAWN = 248, // 主机 -> 客户端：AI 死亡掉落箱子生成（包含 scene 与变换）

        SCENE_GATE_READY = 228, // 客户端 -> 主机：我已加载完成，正在等待放行
        SCENE_GATE_RELEASE = 229, // 主机 -> 客户端：放行，退出加载界面进入游戏

        PLAYER_DEAD_TREE = 173,
        PLAYER_HURT_EVENT = 170,

        HOST_BUFF_PROXY_APPLY = 172,
        PLAYER_BUFF_SELF_APPLY = 171,
        ENV_HURT_REQUEST = 220,   // 客户端 -> 主机：请求对某个 HealthSimpleBase 结算一次受击
        ENV_HURT_EVENT = 221,   // 主机 -> 全体：某个对象受击（含当前血量与命中点/法线）
        ENV_DEAD_EVENT = 222,   // 主机 -> 全体：某个对象死亡（切换视觉）
        MELEE_ATTACK_REQUEST = 242, // 客户端 -> 主机：近战起手（含命中帧延时+位姿快照），用于播动作/挥空FX
        MELEE_ATTACK_SWING = 243, // 主机 -> 客户端：某玩家开始挥砍（远端播动作/挥空FX）
        MELEE_HIT_REPORT = 244, // 客户端 -> 主机：上报本次近战命中的 DamageInfo 关键字段（逐个 Hurt）
        DISCOVER_REQUEST = 240,
        DISCOVER_RESPONSE = 241,
        ENV_SYNC_REQUEST = 245, // 客户端 -> 主机：请求一份环境快照（新连入或场景就绪时）
        ENV_SYNC_STATE = 246, // 主机 -> 客户端：下发环境状态（周期广播或应答）

        LOOT_REQ_SPLIT = 239,

        LOOT_REQ_OPEN = 250,   // 客户端 -> 主机：请求容器快照
        LOOT_STATE = 251,  // 主机 -> 客户端：下发容器快照（全量）
        LOOT_REQ_PUT = 252,  // 客户端 -> 主机：请求“放入”
        LOOT_REQ_TAKE = 253,  // 客户端 -> 主机：请求“取出”
        LOOT_PUT_OK = 254,  // 主机 -> 发起客户端：确认“放入”成功，附回执 token
        LOOT_TAKE_OK = 255,  // 主机 -> 发起客户端：确认“取出”成功 + 返回 Item 快照
        LOOT_DENY = 249,  // 主机 -> 发起客户端：拒绝（例如并发冲突/格子无物品/容量不足）
    }

    // 量化工具
    public static class NetPack
    {
      
        const float POS_SCALE = 100f;

        public static void PutV3cm(this NetDataWriter w, Vector3 v)
        {
            w.Put((int)Mathf.Round(v.x * POS_SCALE));
            w.Put((int)Mathf.Round(v.y * POS_SCALE));
            w.Put((int)Mathf.Round(v.z * POS_SCALE));
        }
        public static Vector3 GetV3cm(this NetPacketReader r)
        {
            float inv = 1f / POS_SCALE;
            return new Vector3(r.GetInt() * inv, r.GetInt() * inv, r.GetInt() * inv);
        }


        // 方向：yaw/pitch 各 2 字节（yaw:0..360，pitch:-90..90）
        public static void PutDir(this NetDataWriter w, Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-8f) dir = Vector3.forward;
            dir.Normalize();
            float pitch = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg; // -90..90
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;                 // -180..180
            if (yaw < 0) yaw += 360f;

            ushort qYaw = (ushort)Mathf.Clamp(Mathf.RoundToInt(yaw / 360f * 65535f), 0, 65535);
            ushort qPitch = (ushort)Mathf.Clamp(Mathf.RoundToInt((pitch + 90f) / 180f * 65535f), 0, 65535);
            w.Put(qYaw);
            w.Put(qPitch);
        }
        public static Vector3 GetDir(this NetPacketReader r)
        {
            float yaw = r.GetUShort() / 65535f * 360f;          // 0..360
            float pitch = (r.GetUShort() / 65535f) * 180f - 90f;  // -90..90
            float cy = Mathf.Cos(yaw * Mathf.Deg2Rad);
            float sy = Mathf.Sin(yaw * Mathf.Deg2Rad);
            float cp = Mathf.Cos(pitch * Mathf.Deg2Rad);
            float sp = Mathf.Sin(pitch * Mathf.Deg2Rad);
            Vector3 d = new Vector3(sy * cp, sp, cy * cp);
            if (d.sqrMagnitude < 1e-8f) d = Vector3.forward;
            return d;
        }

        // 小范围浮点压缩（可用于 MoveDir/速度等），范围 [-8,8]，分辨率 1/16 可能的sans自己算
        public static void PutSNorm16(this NetDataWriter w, float v)
        {
            int q = Mathf.RoundToInt(Mathf.Clamp(v, -8f, 8f) * 16f);
            w.Put((sbyte)Mathf.Clamp(q, sbyte.MinValue, sbyte.MaxValue));
        }
        public static float GetSNorm16(this NetPacketReader r)
        {
            return r.GetSByte() / 16f;
        }

        public static void PutDamagePayload(this NetDataWriter w,
    float damageValue, float armorPiercing, float critDmgFactor, float critRate, int crit,
    Vector3 damagePoint, Vector3 damageNormal, int fromWeaponItemID, float bleedChance, bool isExplosion,
    float attackRange)
        {
            w.Put(damageValue);
            w.Put(armorPiercing);
            w.Put(critDmgFactor);
            w.Put(critRate);
            w.Put(crit);
            w.PutV3cm(damagePoint);
            w.PutDir(damageNormal.sqrMagnitude < 1e-6f ? Vector3.forward : damageNormal.normalized);
            w.Put(fromWeaponItemID);
            w.Put(bleedChance);
            w.Put(isExplosion);
            w.Put(attackRange);
        }

        public static (float dmg, float ap, float cdf, float cr, int crit, Vector3 point, Vector3 normal, int wid, float bleed, bool boom, float range)
            GetDamagePayload(this NetPacketReader r)
        {
            float dmg = r.GetFloat();
            float ap = r.GetFloat();
            float cdf = r.GetFloat();
            float cr = r.GetFloat();
            int crit = r.GetInt();
            Vector3 p = r.GetV3cm();
            Vector3 n = r.GetDir();
            int wid = r.GetInt();
            float bleed = r.GetFloat();
            bool boom = r.GetBool();
            float rng = r.GetFloat();
            return (dmg, ap, cdf, cr, crit, p, n, wid, bleed, boom, rng);
        }



    }

    static class ServerTuning
    {
        // 远端近战伤害倍率（按需调整）
        public const float RemoteMeleeCharScale = 1.00f;  // 打角色：保持原汁原味
        public const float RemoteMeleeEnvScale = 1.5f;  // 打环境：稍微抬一点

        // 打环境/建筑时，用 null 作为“攻击者”，避免基于攻击者的二次系数让伤害被稀释
        public const bool UseNullAttackerForEnv = true;
    }

    public class EquipmentSyncData
    {
        public int SlotHash;
        public string ItemId;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(SlotHash);
            writer.Put(ItemId ?? "");
        }

        public static EquipmentSyncData Deserialize(NetPacketReader reader)
        {
            return new EquipmentSyncData
            {
                SlotHash = reader.GetInt(),
                ItemId = reader.GetString()
            };
        }
    }

    public class WeaponSyncData
    {
        public int SlotHash;
        public string ItemId;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(SlotHash);
            writer.Put(ItemId ?? "");
        }

        public static WeaponSyncData Deserialize(NetPacketReader reader)
        {
            return new WeaponSyncData
            {
                SlotHash = reader.GetInt(),
                ItemId = reader.GetString()
            };
        }
    }

    public class MeleeFxStamp : MonoBehaviour { public float lastFxTime; }

    public static class MeleeFx
    {
        public static void SpawnSlashFx(CharacterModel ctrl)
        {
            if (!ctrl) return;

            // —— 1) 更稳地获取近战武器 Agent —— 
            ItemAgent_MeleeWeapon melee = null;

            // 优先：从模型常见挂点里找
            Transform[] sockets =
            {
        ctrl.MeleeWeaponSocket,
        // 某些模型可能把近战也挂在左右手
        // 这些字段若不存在/为 null，不会报错
        ctrl.GetType().GetField("RightHandSocket") != null ? (Transform)ctrl.GetType().GetField("RightHandSocket").GetValue(ctrl) : null,
        ctrl.GetType().GetField("LefthandSocket")  != null ? (Transform)ctrl.GetType().GetField("LefthandSocket").GetValue(ctrl)  : null,
            };

            foreach (var s in sockets)
            {
                if (melee) break;
                if (!s) continue;
                melee = s.GetComponentInChildren<ItemAgent_MeleeWeapon>(true);
            }

            // 兜底：从整个人物下搜（可能命中备用/预加载实例，影响极小）
            if (!melee)
                melee = ctrl.GetComponentInChildren<ItemAgent_MeleeWeapon>(true);

            if (!melee || !melee.slashFx) return;

            // —— 2) 去抖，避免同帧重复播 —— 
            var stamp = ctrl.GetComponent<MeleeFxStamp>() ?? ctrl.gameObject.AddComponent<MeleeFxStamp>();
            if (Time.time - stamp.lastFxTime < 0.01f) return; // 去抖
            stamp.lastFxTime = Time.time;

            // —— 3) 按武器定义的延迟 + 合理的前方位置/朝向 —— 
            float delay = Mathf.Max(0f, melee.slashFxDelayTime);

            var t = ctrl.transform;
            float forward = Mathf.Clamp(melee.AttackRange * 0.6f, 0.2f, 2.5f);
            Vector3 pos = t.position + t.forward * forward + Vector3.up * 0.6f;
            Quaternion rot = Quaternion.LookRotation(t.forward, Vector3.up);

            Cysharp.Threading.Tasks.UniTask.Void(async () =>
            {
                try
                {
                    await Cysharp.Threading.Tasks.UniTask.Delay(TimeSpan.FromSeconds(delay));
                    UnityEngine.Object.Instantiate(melee.slashFx, pos, rot);
                }
                catch { }
            });
        }

    }

    // ===== 本人无意在此堆，只是开始想要管理好的，后来懒的开新的类了导致这个类不堪重负维护有一点点小复杂 2025/10/27 =====
    public class ModBehaviour : Duckov.Modding.ModBehaviour, INetEventListener
    {
        public static ModBehaviour Instance; //一切的开始 Hello World!
        public bool IsServer { get; private set; } = false;

        public NetManager netManager;
        public NetDataWriter writer;
        public int port = 9050;
        private List<string> hostList = new List<string>();
        private HashSet<string> hostSet = new HashSet<string>();
        private bool isConnecting = false;
        private string status = "";  // 초기화는 Start에서
        private string manualIP = "127.0.0.1";
        private string manualPort = "9050"; // GTX 5090 我也想要
        public NetPeer connectedPeer;
        public bool networkStarted = false;
        private float broadcastTimer = 0f;
        private float broadcastInterval = 5f;
        private float syncTimer = 0f;
        private float syncInterval = 0.015f; // =========== Mod开发者注意现在是TI版本也就是满血版无同步延迟，0.03 ~33ms ===================
        public Harmony Harmony;

        public bool Pausebool;

        // 服务器：按 NetPeer 管理
        public readonly HashSet<int> _dedupeShotFrame = new HashSet<int>(); // 本帧已发过的标记
        public PlayerStatus localPlayerStatus;
        public readonly Dictionary<NetPeer, PlayerStatus> playerStatuses = new Dictionary<NetPeer, PlayerStatus>();
        public readonly Dictionary<NetPeer, GameObject> remoteCharacters = new Dictionary<NetPeer, GameObject>();

        // 客户端：按 endPoint(玩家ID) 管理
        public readonly Dictionary<string, PlayerStatus> clientPlayerStatuses = new Dictionary<string, PlayerStatus>();
        public readonly Dictionary<string, GameObject> clientRemoteCharacters = new Dictionary<string, GameObject>();

        // weaponTypeId(= Item.TypeID) -> projectile prefab
        private readonly Dictionary<int, Projectile> _projCacheByWeaponType = new Dictionary<int, Projectile>();
        // 缓存：武器TypeID -> 枪口火Prefab（可能为null）
        private readonly Dictionary<int, GameObject> _muzzleFxCacheByWeaponType = new Dictionary<int, GameObject>();
        // 给我一个默认的开火FX，用于弓没有配置 muzzleFxPfb 时兜底（在 Inspector 里拖一个合适的特效）
        public GameObject defaultMuzzleFx;
       
        public readonly HashSet<Projectile> _serverSpawnedFromClient = new HashSet<Projectile>();

        private readonly Dictionary<int, float> _speedCacheByWeaponType = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _distCacheByWeaponType = new Dictionary<int, float>();

        // ---------------- Grenade caches ----------------
        private readonly Dictionary<int, Grenade> prefabByTypeId = new Dictionary<int, Grenade>();
        private readonly Dictionary<uint, Grenade> serverGrenades = new Dictionary<uint, Grenade>();
        private readonly Dictionary<uint, GameObject> clientGrenades = new Dictionary<uint, GameObject>();
        private uint nextGrenadeId = 1;

        private struct PendingSpawn
        {
            public uint id; public int typeId;
            public Vector3 start, vel; public bool create; public float shake, dmg;
            public bool delayOnHit; public float delay; public bool isMine; public float mineRange;
            public float expireAt;
        }

        private readonly List<PendingSpawn> pending = new List<PendingSpawn>();
        private float pendingTick;

        public readonly HashSet<Item> _clientSpawnByServerItems = new HashSet<Item>();     // 客户端：标记“来自主机的生成”，防止 Prefix 误发请求
        public readonly HashSet<Item> _serverSpawnedFromClientItems = new HashSet<Item>(); // 主机：标记“来自客户端请求的生成”，防止 Postfix 二次广播

        public readonly Dictionary<uint, Item> serverDroppedItems = new Dictionary<uint, Item>(); // 主机记录
        public readonly Dictionary<uint, Item> clientDroppedItems = new Dictionary<uint, Item>(); // 客户端记录（可用于拾取等后续）
        public uint nextDropId = 1;

        public uint nextLocalDropToken = 1;                 // 客户端本地 token（用来忽略自己 echo 回来的 SPAWN）
        public readonly HashSet<uint> pendingLocalDropTokens = new HashSet<uint>();
        public readonly Dictionary<uint, Item> pendingTokenItems = new Dictionary<uint, Item>(); // 客户端：本地丢物时记录 token -> item

        // Destructible registry: id -> HealthSimpleBase
        private readonly Dictionary<uint, HealthSimpleBase> _serverDestructibles = new Dictionary<uint, HealthSimpleBase>();
        private readonly Dictionary<uint, HealthSimpleBase> _clientDestructibles = new Dictionary<uint, HealthSimpleBase>();

        private readonly HashSet<uint> _deadDestructibleIds = new HashSet<uint>();
        private readonly Dictionary<string, System.Collections.Generic.List<(int weaponTypeId, int buffId)>> _cliPendingProxyBuffs
    = new Dictionary<string, System.Collections.Generic.List<(int, int)>>();

        // ===============  序列化：物品快照（只同步实例态 + 附件树 + 容器内物品） ===============
        public struct ItemSnapshot
        {
            public int typeId;
            public int stack;
            public float durability;
            public float durabilityLoss;
            public bool inspected;
            public List<(string key, ItemSnapshot child)> slots;     // 附件树
            public List<ItemSnapshot> inventory;                     // 容器内容
        }



        public class PlayerStatus
        {
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

        private Rect mainWindowRect = new Rect(10, 10, 400, 700);
        private Rect playerStatusWindowRect = new Rect(420, 10, 300, 400);
        private bool showPlayerStatusWindow = false;
        private Vector2 playerStatusScrollPos = Vector2.zero;
        private KeyCode toggleWindowKey = KeyCode.P;

        private bool isinit; // 判断玩家装备slot监听初始哈的

        public static CustomFaceSettingData localPlayerCustomFace;

        // 反射字段（Health 反编译字段）研究了20年研究出来的
        static readonly System.Reflection.FieldInfo FI_defaultMax =
            typeof(Health).GetField("defaultMaxHealth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        static readonly System.Reflection.FieldInfo FI_lastMax =
            typeof(Health).GetField("lastMaxHealth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        static readonly System.Reflection.FieldInfo FI__current =
            typeof(Health).GetField("_currentHealth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        static readonly System.Reflection.FieldInfo FI_characterCached =
            typeof(Health).GetField("characterCached", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        static readonly System.Reflection.FieldInfo FI_hasCharacter =
            typeof(Health).GetField("hasCharacter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // 主机端：Health -> 所属 Peer 的映射（host 自己用 null）
        private readonly Dictionary<Health, NetPeer> _srvHealthOwner = new Dictionary<Health, NetPeer>();
        private readonly HashSet<Health> _srvHooked = new HashSet<Health>();

        // 主机端：节流去抖
        private readonly Dictionary<Health, (float max, float cur)> _srvLastSent = new Dictionary<Health, (float max, float cur)>();
        private readonly Dictionary<Health, float> _srvNextSend = new Dictionary<Health, float>();
        private const float SRV_HP_SEND_COOLDOWN = 0.05f; // 20Hz

        // 客户端：本地 SELF 权威包尚未套用时缓存
        private bool _cliSelfHpPending;
        private float _cliSelfHpMax, _cliSelfHpCur;

        // 客户端：远端克隆未生成前收到的远端HP缓存
        private readonly Dictionary<string, (float max, float cur)> _cliPendingRemoteHp = new Dictionary<string, (float max, float cur)>();

        private bool _cliInitHpReported = false;
        private bool isinit2;

        private string _envReqSid;

        public bool allowLocalSceneLoad = false;

        public bool sceneVoteActive = false;
        private string sceneTargetId = null;   // 统一的目标 SceneID
        private string sceneCurtainGuid = null;   // 过场 GUID，可为空
        private bool sceneNotifyEvac = false;
        private bool sceneSaveToFile = true;

        // 所有端都使用主机广播的这份参与者 pid 列表（关键：统一 pid）
        private readonly List<string> sceneParticipantIds = new List<string>();

        // 就绪表（key = 上面那个 pid）
        private readonly Dictionary<string, bool> sceneReady = new Dictionary<string, bool>();

        // 本地提示用
        private bool localReady = false;
        private readonly KeyCode readyKey = KeyCode.J;

        private bool sceneUseLocation = false; 

        private string sceneLocationName = null;

        private readonly Dictionary<string, string> _cliLastSceneIdByPlayer = new Dictionary<string, string>();
        private float _ensureRemoteTick = 0f;
        private const float EnsureRemoteInterval = 1.0f; // 每秒兜底一次，够用又不吵
        private string _sceneReadySidSent;

        private readonly Dictionary<NetPeer, string> _srvPeerScene = new Dictionary<NetPeer, string>();

        private float _envSyncTimer = 0f;
        private const float ENV_SYNC_INTERVAL = 1.0f; // 每 1 秒广播一次；可按需 0.5~2 调
        private bool _envReqOnce = false;

        // ====== Lootbox 同步：运行期标识/状态 ======
        public bool _applyingLootState = false;         // 客户端：应用主机快照时抑制 Prefix
        public bool _serverApplyingLoot = false;        // 主机：处理客户端请求时抑制 Postfix 二次广播

        // 客户端：本地 put 请求的 token -> Item 实例（用于 put 成功后从玩家背包删去这个本地实例）
        public uint _nextLootToken = 1;
        public readonly Dictionary<uint, Item> _cliPendingPut = new Dictionary<uint, Item>();

        public int _clientLootSetupDepth = 0;
        public bool ClientLootSetupActive => networkStarted && !IsServer && _clientLootSetupDepth > 0;

        public readonly Dictionary<int, int> aiRootSeeds = new Dictionary<int, int>(); // rootId -> seed
        public int sceneSeed = 0;
        public bool freezeAI = true;  // 先冻结用来验证一致性

        public readonly Dictionary<int, CharacterMainControl> aiById = new Dictionary<int, CharacterMainControl>();
        // aiId -> 待应用的负载（若实体还未就绪）

        private float _aiTfTimer;
        private const float AI_TF_INTERVAL = 0.05f;

        // 发送去抖：只有发生明显改动才发，避免带宽爆炸
        private readonly Dictionary<int, (Vector3 pos, Vector3 dir)> _lastAiSent = new Dictionary<int, (Vector3 pos, Vector3 dir)>();

        private readonly Dictionary<int, int> _aiSerialPerRoot = new Dictionary<int, int>();

        bool _aiSceneReady;
        readonly Queue<(int id, Vector3 p, Vector3 f)> _pendingAiTrans = new Queue<(int id, Vector3 p, Vector3 f)>();


        // —— AutoBind 限频/范围参数 —— 
        private readonly Dictionary<int, float> _lastAutoBindTryTime = new Dictionary<int, float>();
        private const float AUTOBIND_COOLDOWN = 0.20f; // 200ms：同一 aiId 的重试冷却
        private const float AUTOBIND_RADIUS = 35f;   // 近场搜索半径，可按需要 25~40f
        private const QueryTriggerInteraction AUTOBIND_QTI = QueryTriggerInteraction.Collide;
        private const int AUTOBIND_LAYERMASK = ~0;    // 如有专用 Layer，可替换~~~~~~oi

        private readonly Dictionary<uint, ItemStatsSystem.Item> _cliPendingSlotPlug = new Dictionary<uint, ItemStatsSystem.Item>();

       struct AiAnimState
        {
            public float speed, dirX, dirY;
            public int hand;
            public bool gunReady, dashing;
        }

        // 待绑定时的暂存（客户端）
        private readonly Dictionary<int, AiAnimState> _pendingAiAnims = new Dictionary<int, AiAnimState>();

        // 主机端的节流定时器
        private float _aiAnimTimer = 0f;
        private const float AI_ANIM_INTERVAL = 0.10f; // 10Hz 动画参数广播

        public GameObject aiTelegraphFx;

        private readonly Dictionary<int, float> _cliPendingAiHealth = new Dictionary<int, float>();

        public static bool LogAiHpDebug = false; // 需要时改为 true，打印 [AI-HP] 日志

        private float _aiNameIconTimer = 0f;
        private const float AI_NAMEICON_INTERVAL = 10f;

        const byte AI_LOADOUT_VER = 5;
        public static bool LogAiLoadoutDebug = true;

        // --- 反编译类的私有序列化字段直达句柄---
        static readonly AccessTools.FieldRef<CharacterRandomPreset, bool>
            FR_UsePlayerPreset = AccessTools.FieldRefAccess<CharacterRandomPreset, bool>("usePlayerPreset");
        static readonly AccessTools.FieldRef<CharacterRandomPreset, CustomFacePreset>
            FR_FacePreset = AccessTools.FieldRefAccess<CharacterRandomPreset, CustomFacePreset>("facePreset");
        static readonly AccessTools.FieldRef<CharacterRandomPreset, CharacterModel>
            FR_CharacterModel = AccessTools.FieldRefAccess<CharacterRandomPreset, CharacterModel>("characterModel");
        static readonly AccessTools.FieldRef<CharacterRandomPreset, global::CharacterIconTypes>
            FR_IconType = AccessTools.FieldRefAccess<CharacterRandomPreset, global::CharacterIconTypes>("characterIconType");

        private readonly Dictionary<int, string> _aiFaceJsonById = new Dictionary<int, string>();

        // --- 若实体未就绪的 pending（装备/武器/脸/模型名/图标/显示名标记）AI---
        private readonly Dictionary<int, (
       List<(int slot, int tid)> equips,
       List<(int slot, int tid)> weapons,
       string faceJson,
       string modelName,
       int iconType,
       bool showName,
       string displayName
       )> pendingAiLoadouts
       = new Dictionary<int, (
           List<(int slot, int tid)> equips,
           List<(int slot, int tid)> weapons,
           string faceJson,
           string modelName,
           int iconType,
           bool showName,
           string displayName
       )>();

        private readonly Dictionary<int, (int capacity, List<(int pos, ItemSnapshot snap)>)> _pendingLootStates
        = new Dictionary<int, (int, List<(int, ItemSnapshot)>)>();

        private int _nextLootUid = 1; // 服务器侧自增
                                      // 服务器：uid -> inv
        private readonly Dictionary<int, Inventory> _srvLootByUid = new Dictionary<int, Inventory>();
        // 客户端：uid -> inv
        private readonly Dictionary<int, Inventory> _cliLootByUid = new Dictionary<int, Inventory>();

        // 客户端：快照缓存（快照先到，箱体后到）
        private readonly Dictionary<int, (int capacity, List<(int pos, ItemSnapshot snap)>)> _pendingLootStatesByUid
            = new Dictionary<int, (int, List<(int, ItemSnapshot)>)>();

        private bool _spectatorEndOnVotePending = false;

        internal bool _skipSpectatorForNextClosure = false;

        // 目的地记录：用于 TAKE_OK 回包后“精确落位”
        private struct PendingTakeDest
        {
            // 目的地（背包格或装备槽）
            public ItemStatsSystem.Inventory inv;
            public int pos;
            public ItemStatsSystem.Items.Slot slot;

            // 源信息（从哪个容器的哪个格子拿出来）
            public ItemStatsSystem.Inventory srcLoot;
            public int srcPos;
        }

        // token -> 目的地
        private readonly System.Collections.Generic.Dictionary<uint, PendingTakeDest> _cliPendingTake
            = new System.Collections.Generic.Dictionary<uint, PendingTakeDest>();

        // —— 工具：对外暴露两个只读状态 —— //
        public bool IsClient => networkStarted && !IsServer;

        // 暴露客户端是否正在应用服务器下发的容器快照
        public bool ApplyingLootState => _applyingLootState;

        private readonly Dictionary<ItemStatsSystem.Item, (ItemStatsSystem.Item newItem,
                                                ItemStatsSystem.Inventory destInv, int destPos,
                                                ItemStatsSystem.Items.Slot destSlot)>
  _cliSwapByVictim = new Dictionary<ItemStatsSystem.Item, (ItemStatsSystem.Item, ItemStatsSystem.Inventory, int, ItemStatsSystem.Items.Slot)>();

      
        private bool _cliHookedSelf = false;
        private UnityEngine.Events.UnityAction<Health> _cbSelfHpChanged, _cbSelfMaxChanged;
        private UnityEngine.Events.UnityAction<DamageInfo> _cbSelfHurt, _cbSelfDead;
        private float _cliNextSendHp = 0f;
        private (float max, float cur) _cliLastSentHp = (0f, 0f);

        private readonly Dictionary<NetPeer, (float max, float cur)> _srvPendingHp = new Dictionary<NetPeer, (float max, float cur)>();

        bool _cliApplyingSelfSnap = false;
        float _cliEchoMuteUntil = 0f;
        const float SELF_MUTE_SEC = 0.10f;

        private bool showUI = true;

        public struct Pending
        {
            public Inventory inv;
            public int srcPos;
            public int count;
        }

        public static readonly Dictionary<int, Pending> map = new Dictionary<int, Pending>();

        // —— 外观缓存（避免发空&避免被空覆盖）——
        private string _lastGoodFaceJson = null;

        // 客户端：远端玩家待应用的外观缓存
        private readonly Dictionary<string, string> _cliPendingFace = new Dictionary<string, string>();

        private bool _hasPayloadHint;
        private ProjectileContext _payloadHint;

        // 爆炸参数缓存（主机记住每种武器的爆炸半径/伤害）
        private readonly Dictionary<int, float> _explRangeCacheByWeaponType = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _explDamageCacheByWeaponType = new Dictionary<int, float>();

        private bool _cliSelfDeathFired = false;

        public bool _spectatorActive = false;
        public List<CharacterMainControl> _spectateList = new List<CharacterMainControl>();
        private int _spectateIdx = -1;
        private float _spectateNextSwitchTime = 0f;
        public global::DamageInfo _lastDeathInfo;

        const float SELF_ACCEPT_WINDOW = 0.30f;   // 受击后0.3秒内，只接受更低/相等的权威血量
        float _cliLastSelfHurtAt = -999f;         // 最后本地受击时间
        float _cliLastSelfHpLocal = -1f;          // 受击后本地血量（用于对比回显）

        public const  bool EAGER_BROADCAST_LOOT_STATE_ON_SPAWN = false;

        readonly Dictionary<ItemAgent_Gun, GameObject> _muzzleFxByGun = new Dictionary<ItemAgent_Gun, GameObject>();
        readonly Dictionary<ItemAgent_Gun, ParticleSystem> _shellPsByGun = new Dictionary<ItemAgent_Gun, ParticleSystem>();

        // 反射缓存（避免每枪 Traverse）
        static readonly System.Reflection.MethodInfo MI_StartVisualRecoil =
            HarmonyLib.AccessTools.Method(typeof(ItemAgent_Gun), "StartVisualRecoil");
        static readonly System.Reflection.FieldInfo FI_RecoilBack =
            HarmonyLib.AccessTools.Field(typeof(ItemAgent_Gun), "_recoilBack");
        static readonly System.Reflection.FieldInfo FI_ShellParticle =
            HarmonyLib.AccessTools.Field(typeof(ItemAgent_Gun), "shellParticle");

        static Transform _fallbackMuzzleAnchor;

        readonly Dictionary<string, (ItemAgent_Gun gun, Transform muzzle)> _gunCacheByShooter = new Dictionary<string, (ItemAgent_Gun gun, Transform muzzle)>();

       //全局变量地狱的结束
























        void Awake()
        {
            Debug.Log("ModBehaviour Awake");
            Instance = this;

            // 다국어 시스템 초기화
            Main.Localization.Initialize();
            status = Main.Localization.Get("status_disconnected");
        }

        private void OnEnable()
        {
            Harmony = new Harmony("DETF_COOP");
            Harmony.PatchAll();
            SceneManager.sceneLoaded += OnSceneLoaded_IndexDestructibles;
            LevelManager.OnAfterLevelInitialized += LevelManager_OnAfterLevelInitialized; 
            LevelManager.OnLevelInitialized += OnLevelInitialized_IndexDestructibles;


            SceneManager.sceneLoaded += SceneManager_sceneLoaded;
            LevelManager.OnLevelInitialized += LevelManager_OnLevelInitialized;
        }

        private void LevelManager_OnAfterLevelInitialized()
        {
            Client_ArmSpawnProtection(15f);
            if (IsServer && networkStarted)
                Server_SceneGateAsync().Forget();
        }

        private void LevelManager_OnLevelInitialized()
        {

            ResetAiSerials();
            if(!IsServer) Client_ReportSelfHealth_IfReadyOnce();
            TrySendSceneReadyOnce();
            if (!IsServer) Client_RequestEnvSync();

            if (IsServer) Server_SendAiSeeds();
            Client_ResetNameIconSeal_OnLevelInit();

        }
        //arg!!!!!!!!!!!
        private void SceneManager_sceneLoaded(Scene arg0, LoadSceneMode arg1)
        {
            TrySendSceneReadyOnce();
            if (!IsServer) Client_RequestEnvSync();

        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded_IndexDestructibles;
            LevelManager.OnLevelInitialized -= OnLevelInitialized_IndexDestructibles;
         //   LevelManager.OnAfterLevelInitialized -= _OnAfterLevelInitialized_ServerGate;

            SceneManager.sceneLoaded -= SceneManager_sceneLoaded;
            LevelManager.OnLevelInitialized -= LevelManager_OnLevelInitialized;
        }

        public void StartNetwork(bool isServer)
        {
            StopNetwork();
            freezeAI = !isServer;
            IsServer = isServer;
            writer = new NetDataWriter();
            netManager = new NetManager(this)
            {
                BroadcastReceiveEnabled = true
            };

            if (IsServer)
            {
                bool started = netManager.Start(port);
                if (started) Debug.Log($"服务器启动，监听端口 {port}");
                else Debug.LogError("服务器启动失败，请检查端口是否被占用");
            }
            else
            {
                bool started = netManager.Start();
                if (started)
                {
                    Debug.Log("客户端启动");
                    SendBroadcastDiscovery();
                }
                else Debug.LogError("客户端启动失败");
            }

            networkStarted = true;
            status = Main.Localization.Get("status_network_started");
            hostList.Clear();
            hostSet.Clear();
            isConnecting = false;
            connectedPeer = null;

            playerStatuses.Clear();
            remoteCharacters.Clear();
            clientPlayerStatuses.Clear();
            clientRemoteCharacters.Clear();

            InitializeLocalPlayer();
            if (IsServer)
            {
                ItemAgent_Gun.OnMainCharacterShootEvent -= Host_OnMainCharacterShoot;
                ItemAgent_Gun.OnMainCharacterShootEvent += Host_OnMainCharacterShoot;
            }
        }

        private void InitializeLocalPlayer()
        {
            var bool1 = ComputeIsInGame(out var ids);
            localPlayerStatus = new PlayerStatus
            {

                EndPoint = IsServer ? $"Host:{port}" : $"Client:{Guid.NewGuid().ToString().Substring(0, 8)}",
                PlayerName = IsServer ? "Host" : "Client",
                Latency = 0,
                IsInGame = bool1,
                LastIsInGame = bool1,
                Position = Vector3.zero,
                Rotation = Quaternion.identity,
                SceneId = ids,
                CustomFaceJson = LoadLocalCustomFaceJson()
            };
        }

        private string LoadLocalCustomFaceJson()
        {
            try
            {
                string json = null;

                // 1) 尝试：LevelManager 的保存数据（struct，无需判 null）
                var lm = LevelManager.Instance;
                if (lm != null && lm.CustomFaceManager != null)
                {
                    try
                    {
                        var data1 = lm.CustomFaceManager.LoadMainCharacterSetting(); // struct
                        json = JsonUtility.ToJson(data1);
                    }
                    catch { }
                }

                // 2) 兜底：从运行时模型抓当前脸（ConvertToSaveData）
                if (string.IsNullOrEmpty(json) || json == "{}")
                {
                    try
                    {
                        var main = CharacterMainControl.Main;
                        var model = main != null ? main.characterModel : null;
                        var cf = model != null ? model.CustomFace : null;
                        if (cf != null)
                        {
                            var data2 = cf.ConvertToSaveData(); // struct
                            var j2 = JsonUtility.ToJson(data2);
                            if (!string.IsNullOrEmpty(j2) && j2 != "{}")
                                json = j2;
                        }
                    }
                    catch { }
                }

                // 3) 记住最近一次非空
                if (!string.IsNullOrEmpty(json) && json != "{}")
                    _lastGoodFaceJson = json;

                // 4) 返回永不为空（尽量用缓存兜底）
                return (!string.IsNullOrEmpty(json) && json != "{}") ? json : (_lastGoodFaceJson ?? "");
            }
            catch
            {
                return _lastGoodFaceJson ?? "";
            }
        }

        public void StopNetwork()
        {
            if (netManager != null && netManager.IsRunning)
            {
                netManager.Stop();
                Debug.Log("网络已停止");
            }
            networkStarted = false;
            connectedPeer = null;

            playerStatuses.Clear();
            clientPlayerStatuses.Clear();

            localPlayerStatus = null;

            foreach (var kvp in remoteCharacters)
                if (kvp.Value != null) Destroy(kvp.Value);
            remoteCharacters.Clear();

            foreach (var kvp in clientRemoteCharacters)
                if (kvp.Value != null) Destroy(kvp.Value);
            clientRemoteCharacters.Clear();

            ItemAgent_Gun.OnMainCharacterShootEvent -= Host_OnMainCharacterShoot;
        }

        private bool IsSelfId(string id)
        {
            var mine = localPlayerStatus?.EndPoint;
            return !string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(mine) && id == mine;
        }

        void Update()
        {
            if (CharacterMainControl.Main != null && !isinit)
            {
                isinit = true;
                Traverse.Create(CharacterMainControl.Main.EquipmentController).Field<Slot>("armorSlot").Value.onSlotContentChanged += ModBehaviour_onSlotContentChanged;
                Traverse.Create(CharacterMainControl.Main.EquipmentController).Field<Slot>("helmatSlot").Value.onSlotContentChanged += ModBehaviour_onSlotContentChanged;
                Traverse.Create(CharacterMainControl.Main.EquipmentController).Field<Slot>("faceMaskSlot").Value.onSlotContentChanged += ModBehaviour_onSlotContentChanged;
                Traverse.Create(CharacterMainControl.Main.EquipmentController).Field<Slot>("backpackSlot").Value.onSlotContentChanged += ModBehaviour_onSlotContentChanged;
                Traverse.Create(CharacterMainControl.Main.EquipmentController).Field<Slot>("headsetSlot").Value.onSlotContentChanged += ModBehaviour_onSlotContentChanged;

                CharacterMainControl.Main.OnHoldAgentChanged += Main_OnHoldAgentChanged;
            }

          

            //暂停显示出鼠标
            if (Pausebool)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }

            if (CharacterMainControl.Main == null)
            {
                isinit = false;
            }

            if (Input.GetKeyDown(KeyCode.Home))
            {
                showUI = !showUI;
            }

            if (networkStarted)
            {
                netManager.PollEvents();
                TrySendSceneReadyOnce();
                if (!isinit2)
                {
                    isinit2 = true;
                    if (!IsServer) Client_ReportSelfHealth_IfReadyOnce();
                }

               // if (IsServer) Server_EnsureAllHealthHooks();

                if (!IsServer && !isConnecting)
                {
                    broadcastTimer += Time.deltaTime;
                    if (broadcastTimer >= broadcastInterval)
                    {
                        SendBroadcastDiscovery();
                        broadcastTimer = 0f;
                    }
                }

                syncTimer += Time.deltaTime;
                if (syncTimer >= syncInterval)
                {
                    SendPositionUpdate();
                    SendAnimationStatus();
                    syncTimer = 0f;

                    //if (!IsServer)
                    //{
                    //    if (MultiSceneCore.Instance != null && MultiSceneCore.MainSceneID != "Base")
                    //    {
                    //        if (LevelManager.Instance.MainCharacter != null && LevelManager.Instance.MainCharacter.Health.MaxHealth > 0f)
                    //        {
                    //            // Debug.Log(LevelManager.Instance.MainCharacter.Health.CurrentHealth);
                    //            if (LevelManager.Instance.MainCharacter.Health.CurrentHealth <= 0f && Client_IsSpawnProtected())
                    //            {
                    //                // Debug.Log(LevelManager.Instance.MainCharacter.Health.CurrentHealth);
                    //                Client_EnsureSelfDeathEvent(LevelManager.Instance.MainCharacter.Health, LevelManager.Instance.MainCharacter);
                    //            }
                    //        }
                    //    }
                    //}
                }

                if (!IsServer && !string.IsNullOrEmpty(_sceneReadySidSent) && _envReqSid != _sceneReadySidSent)
                {
                    _envReqSid = _sceneReadySidSent;   // 本场景只请求一次
                    Client_RequestEnvSync();           // 向主机要时间/天气快照
                }

                if (IsServer)
                {
                    _aiNameIconTimer += Time.deltaTime;
                    if (_aiNameIconTimer >= AI_NAMEICON_INTERVAL)
                    {
                        _aiNameIconTimer = 0f;

                        foreach (var kv in aiById)
                        {
                            int id = kv.Key;
                            var cmc = kv.Value;
                            if (!cmc) continue;

                            var pr = cmc.characterPreset;
                            if (!pr) continue;

                            int iconType = 0;
                            bool showName = false;
                            try
                            {
                                iconType = (int)FR_IconType(pr);
                                showName = pr.showName;
                                // 运行期可能刚补上了图标，兜底再查一次
                                if (iconType == 0 && pr.GetCharacterIcon() != null)
                                    iconType = (int)FR_IconType(pr);
                            }
                            catch { }

                            // 只给“有图标 or 需要显示名字”的 AI 发
                            if (iconType != 0 || showName)
                                Server_BroadcastAiNameIcon(id, cmc);
                        }
                    }
                }

                // 主机：周期广播环境快照（不重）
                if (IsServer)
                {
                    _envSyncTimer += Time.deltaTime;
                    if (_envSyncTimer >= ENV_SYNC_INTERVAL)
                    {
                        _envSyncTimer = 0f;
                        Server_BroadcastEnvSync();
                    }

                    _aiAnimTimer += Time.deltaTime;
                    if (_aiAnimTimer >= AI_ANIM_INTERVAL)
                    {
                        _aiAnimTimer = 0f;
                        Server_BroadcastAiAnimations();
                    }

                }

                int burst = 64; // 每帧最多处理这么多条，稳扎稳打
                while (_aiSceneReady && _pendingAiTrans.Count > 0 && burst-- > 0)
                {
                    var (id, p, f) = _pendingAiTrans.Dequeue();
                    ApplyAiTransform(id, p, f);
                }

            }

            if (networkStarted && IsServer)
            {
                _aiTfTimer += Time.deltaTime;
                if (_aiTfTimer >= AI_TF_INTERVAL)
                {
                    _aiTfTimer = 0f;
                    Server_BroadcastAiTransforms();
                }
            }

            UpdatePlayerStatuses();
            UpdateRemoteCharacters();

            if (Input.GetKeyDown(toggleWindowKey))
            {
                showPlayerStatusWindow = !showPlayerStatusWindow;
            }

            ProcessPendingGrenades();

            if (!IsServer)
            {
                if (_cliSelfHpPending && CharacterMainControl.Main != null)
                {
                    ApplyHealthAndEnsureBar(CharacterMainControl.Main.gameObject, _cliSelfHpMax, _cliSelfHpCur);
                    _cliSelfHpPending = false;
                }
            }


            if (IsServer) Server_EnsureAllHealthHooks();
            if (!IsServer) Client_ApplyPendingSelfIfReady();
            if (!IsServer) Client_ReportSelfHealth_IfReadyOnce();

            // 投票期间按 J 切换准备
            if (sceneVoteActive && Input.GetKeyDown(readyKey))
            {
                localReady = !localReady;
                if (IsServer) Server_OnSceneReadySet(null, localReady);  // 主机自己也走同一套
                else Client_SendReadySet(localReady);           // 客户端上报主机
            }

            if (networkStarted)
            {
                TrySendSceneReadyOnce();
                if (_envReqSid != _sceneReadySidSent)
                {
                    _envReqSid = _sceneReadySidSent;
                    Client_RequestEnvSync();
                }

                // 主机：每帧确保给所有 Health 打钩（含新生成/换图后新克隆）
                if (IsServer) Server_EnsureAllHealthHooks();

                // 客户端：本场景里若还没成功上报，就每帧重试直到成功
                if (!IsServer && !_cliInitHpReported) Client_ReportSelfHealth_IfReadyOnce();

                // 客户端：给自己的 Health 持续打钩，变化就上报
                if (!IsServer) Client_HookSelfHealth();
            }

            if (_spectatorActive)
            {
                ClosureView.Instance.gameObject.SetActive(false);
                // 动态剔除“已死/被销毁/不在本地图”的目标
                _spectateList = _spectateList.Where(c =>
                {
                    if (!IsAlive(c)) return false;

                    string mySceneId = localPlayerStatus != null ? localPlayerStatus.SceneId : null;
                    if (string.IsNullOrEmpty(mySceneId))
                        ComputeIsInGame(out mySceneId);

                    // 反查该 CMC 对应的 peer 的 SceneId
                    string peerScene = null;
                    if (IsServer)
                    {
                        foreach (var kv in remoteCharacters)
                            if (kv.Value != null && kv.Value.GetComponent<CharacterMainControl>() == c)
                            { if (!_srvPeerScene.TryGetValue(kv.Key, out peerScene) && playerStatuses.TryGetValue(kv.Key, out var st)) peerScene = st?.SceneId; break; }
                    }
                    else
                    {
                        foreach (var kv in clientRemoteCharacters)
                            if (kv.Value != null && kv.Value.GetComponent<CharacterMainControl>() == c)
                            { if (clientPlayerStatuses.TryGetValue(kv.Key, out var st)) peerScene = st?.SceneId; break; }
                    }

                    return AreSameMap(mySceneId, peerScene);
                }).ToList();


                // 全员阵亡 → 退出观战并弹出结算
                if (_spectateList.Count == 0 || AllPlayersDead())
                {
                    EndSpectatorAndShowClosure();
                    return;
                }

                if (_spectateIdx < 0 || _spectateIdx >= _spectateList.Count)
                    _spectateIdx = 0;

                // 当前目标若死亡，自动跳到下一个
                if (!IsAlive(_spectateList[_spectateIdx]))
                    SpectateNext();

                // 鼠标左/右键切换（加个轻微节流）
                if (Time.unscaledTime >= _spectateNextSwitchTime)
                {
                    if (Input.GetMouseButtonDown(0)) { SpectateNext(); _spectateNextSwitchTime = Time.unscaledTime + 0.15f; }
                    if (Input.GetMouseButtonDown(1)) { SpectatePrev(); _spectateNextSwitchTime = Time.unscaledTime + 0.15f; }
                }
            }




        }

        private void Main_OnHoldAgentChanged(DuckovItemAgent obj)
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
            SendWeaponUpdate(weaponData);
        }



        private void SendAnimationStatus()
        {
            if (!networkStarted) return;

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
                netManager.SendToAll(writer, DeliveryMethod.Sequenced);
            }
            else
            {
                // 客户端 -> 主机：不带 playerId
                if (connectedPeer == null) return;
                writer.Put(anim.GetFloat("MoveSpeed"));
                writer.Put(anim.GetFloat("MoveDirX"));
                writer.Put(anim.GetFloat("MoveDirY"));
                writer.Put(anim.GetBool("Dashing"));
                writer.Put(anim.GetBool("Attack"));
                writer.Put(anim.GetInteger("HandState"));
                writer.Put(anim.GetBool("GunReady"));
                writer.Put(stateHash);
                writer.Put(normTime);
                connectedPeer.Send(writer, DeliveryMethod.Sequenced);
            }
        }



        // 主机接收客户端动画，并转发给其他客户端（携带来源玩家ID）
        void HandleClientAnimationStatus(NetPeer sender, NetPacketReader reader)
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

            // 主机本地（用 NetPeer）
            HandleRemoteAnimationStatus(sender, moveSpeed, moveDirX, moveDirY, isDashing, isAttacking, handState, gunReady, stateHash, normTime);

            string playerId = playerStatuses.TryGetValue(sender, out var st) && !string.IsNullOrEmpty(st.EndPoint)
                ? st.EndPoint
                : sender.EndPoint.ToString();

            foreach (var p in netManager.ConnectedPeerList)
            {
                if (p == sender) continue;
                var w = new NetDataWriter();
                w.Put((byte)Op.ANIM_SYNC);           //  改动：用 opcode
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
                p.Send(w, DeliveryMethod.Sequenced);
            }

        }


        // 主机侧：按 NetPeer 应用动画
        void HandleRemoteAnimationStatus(NetPeer peer, float moveSpeed, float moveDirX, float moveDirY,
                                  bool isDashing, bool isAttacking, int handState, bool gunReady,
                                  int stateHash, float normTime)
        {
            if (!remoteCharacters.TryGetValue(peer, out var remoteObj) || remoteObj == null) return;

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

        static Animator ResolveRemoteAnimator(GameObject remoteObj)
        {
            var cmc = remoteObj.GetComponent<CharacterMainControl>();
            if (cmc == null || cmc.characterModel == null) return null;
            var model = cmc.characterModel;

            var mb = model.GetComponent<CharacterAnimationControl_MagicBlend>();
            if (mb != null && mb.animator != null) return mb.animator;

            var cac = model.GetComponent<CharacterAnimationControl>();
            if (cac != null && cac.animator != null) return cac.animator;

            // 兜底：直接拿模型上的 Animator
            return model.GetComponent<Animator>();
        }


        private void UpdatePlayerStatuses()
        {
            if (netManager == null || !netManager.IsRunning || localPlayerStatus == null)
                return;
            var bool1 = ComputeIsInGame(out var ids);
            bool currentIsInGame = bool1;
            var levelManager = LevelManager.Instance;

            if (localPlayerStatus.IsInGame != currentIsInGame)
            {
                localPlayerStatus.IsInGame = currentIsInGame;
                localPlayerStatus.LastIsInGame = currentIsInGame;

                if (levelManager != null && levelManager.MainCharacter != null)
                {
                    localPlayerStatus.Position = levelManager.MainCharacter.transform.position;
                    localPlayerStatus.Rotation = levelManager.MainCharacter.modelRoot.transform.rotation;
                    localPlayerStatus.CustomFaceJson = LoadLocalCustomFaceJson();
                }

                if (currentIsInGame && levelManager != null)
                {
                    // 不再二次创建本地主角；只做 Scene 就绪上报，由主机撮合同图远端创建
                    TrySendSceneReadyOnce();

                }


                if (!IsServer) SendClientStatusUpdate();
                else SendPlayerStatusUpdate();
            }
            else if (currentIsInGame && levelManager != null && levelManager.MainCharacter != null)
            {
                localPlayerStatus.Position = levelManager.MainCharacter.transform.position;
                localPlayerStatus.Rotation = levelManager.MainCharacter.modelRoot.transform.rotation;
            }

            if (currentIsInGame)
            {
                localPlayerStatus.CustomFaceJson = LoadLocalCustomFaceJson();
            }
        }

        private void UpdateRemoteCharacters()
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


        private async UniTask<GameObject> CreateRemoteCharacterAsync(NetPeer peer, Vector3 position, Quaternion rotation, string customFaceJson)
        {
            if (remoteCharacters.ContainsKey(peer) && remoteCharacters[peer] != null) return null;

            var levelManager = LevelManager.Instance;
            if (levelManager == null || levelManager.MainCharacter == null) return null;

            GameObject instance = Instantiate(CharacterMainControl.Main.gameObject, position, rotation);
            var characterModel = instance.GetComponent<CharacterMainControl>();
         
          //  cInventory = CharacterMainControl.Main.CharacterItem.Inventory;
          //  Traverse.Create(characterModel.CharacterItem).Field<Inventory>("inventory").Value = cInventory;
            
            var cmc = instance.GetComponent<CharacterMainControl>();
            StripAllHandItems(cmc);
            var itemLoaded = await Saves.ItemSavesUtilities.LoadItem(LevelManager.MainCharacterItemSaveKey);
            if (itemLoaded == null)
            {
                itemLoaded = await ItemAssetsCollection.InstantiateAsync(GameplayDataSettings.ItemAssets.DefaultCharacterItemTypeID);
                Debug.LogWarning("Item Loading failed");
            }
            Traverse.Create(characterModel).Field<Item>("characterItem").Value = itemLoaded;
            // Debug.Log(peer.EndPoint.ToString() + " CreateRemoteCharacterForClient");
            // 统一设置初始位姿
            instance.transform.SetPositionAndRotation(position, rotation);

             MakeRemotePhysicsPassive(instance);

            StripAllCustomFaceParts(instance);

            if (characterModel?.characterModel.CustomFace != null && !string.IsNullOrEmpty(customFaceJson))
            {
                var customFaceData = JsonUtility.FromJson<CustomFaceSettingData>(customFaceJson);
                characterModel.characterModel.CustomFace.LoadFromData(customFaceData);
            }

            try
            {
                var cm = characterModel.characterModel;

                COOPManager.ChangeArmorModel(cm, null);
                COOPManager.ChangeHelmatModel(cm, null);
                COOPManager.ChangeFaceMaskModel(cm, null);
                COOPManager.ChangeBackpackModel(cm, null);
                COOPManager.ChangeHeadsetModel(cm, null);
            }
            catch { }


            instance.AddComponent<RemoteReplicaTag>();
            var anim = instance.GetComponentInChildren<Animator>(true);
            if (anim)
            {
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                anim.updateMode = AnimatorUpdateMode.Normal;
            }

            var h = instance.GetComponentInChildren<Health>(true);
            if (h) h.autoInit = false;            // ★ 阻止 Start()->Init() 把血直接回满
            instance.AddComponent<AutoRequestHealthBar>(); // 你已有就不要重复
                                                           // 主机创建完后立刻挂监听并推一次
            Server_HookOneHealth(peer, instance);
            instance.AddComponent<HostForceHealthBar>();

            NetInterpUtil.Attach(instance)?.Push(position, rotation);
            AnimInterpUtil.Attach(instance); // 先挂上，样本由后续网络包填
            cmc.gameObject.SetActive(false);
            remoteCharacters[peer] = instance;
            cmc.gameObject.SetActive(true);
            return instance;
        }
        private async UniTask CreateRemoteCharacterForClient(string playerId, Vector3 position, Quaternion rotation, string customFaceJson)
        {
            if (IsSelfId(playerId)) return; // ★ 不给自己创建“远程自己”
            if (clientRemoteCharacters.ContainsKey(playerId) && clientRemoteCharacters[playerId] != null) return;

            Debug.Log(playerId + " CreateRemoteCharacterForClient");

            var levelManager = LevelManager.Instance;
            if (levelManager == null || levelManager.MainCharacter == null) return;


            GameObject instance = Instantiate(CharacterMainControl.Main.gameObject, position, rotation);
            var characterModel = instance.GetComponent<CharacterMainControl>();

            var itemLoaded = await Saves.ItemSavesUtilities.LoadItem(LevelManager.MainCharacterItemSaveKey);
            if (itemLoaded == null)
            {
                itemLoaded = await ItemAssetsCollection.InstantiateAsync(GameplayDataSettings.ItemAssets.DefaultCharacterItemTypeID);
            }
            Traverse.Create(characterModel).Field<Item>("characterItem").Value = itemLoaded;

            var cmc = instance.GetComponent<CharacterMainControl>();
            StripAllHandItems(cmc);

            instance.transform.SetPositionAndRotation(position, rotation);

            var cmc0 = instance.GetComponentInChildren<CharacterMainControl>(true);
            if (cmc0 && cmc0.modelRoot)
            {
                var e = rotation.eulerAngles;
                cmc0.modelRoot.transform.rotation = Quaternion.Euler(0f, e.y, 0f);
            }       

            MakeRemotePhysicsPassive(instance);
            StripAllCustomFaceParts(instance);

            // 如果入参为空，尽量从已知状态或待应用表拿，再应用（允许为空；为空时后续状态更新会补）
            if (string.IsNullOrEmpty(customFaceJson))
            {
                if (clientPlayerStatuses.TryGetValue(playerId, out var st) && !string.IsNullOrEmpty(st.CustomFaceJson))
                    customFaceJson = st.CustomFaceJson;
                else if (_cliPendingFace.TryGetValue(playerId, out var pending) && !string.IsNullOrEmpty(pending))
                    customFaceJson = pending;
            }


            Client_ApplyFaceIfAvailable(playerId, instance, customFaceJson);
 

            try
            {
                var cm = characterModel.characterModel;

                COOPManager.ChangeArmorModel(cm, null);
                COOPManager.ChangeHelmatModel(cm, null);
                COOPManager.ChangeFaceMaskModel(cm, null);
                COOPManager.ChangeBackpackModel(cm, null);
                COOPManager.ChangeHeadsetModel(cm, null);
            }
            catch {  }

            instance.AddComponent<RemoteReplicaTag>();
            var anim = instance.GetComponentInChildren<Animator>(true);
            if (anim)
            {
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                anim.updateMode = AnimatorUpdateMode.Normal;
            }

            var h = instance.GetComponentInChildren<Health>(true);
            if (h) h.autoInit = false;           
            instance.AddComponent<AutoRequestHealthBar>();
            Client_ApplyPendingRemoteIfAny(playerId, instance);

            NetInterpUtil.Attach(instance)?.Push(position, rotation);
            AnimInterpUtil.Attach(instance);
            cmc.gameObject.SetActive(false);
            clientRemoteCharacters[playerId] = instance;
            cmc.gameObject.SetActive(true);
        }

        private void ModBehaviour_onSlotContentChanged(ItemStatsSystem.Items.Slot obj)
        {
            if (!networkStarted || localPlayerStatus == null || !localPlayerStatus.IsInGame) return;
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
            SendEquipmentUpdate(equipmentData1);
        }

        private void SendEquipmentUpdate(EquipmentSyncData equipmentData)
        {
            if (localPlayerStatus == null || !networkStarted) return;

            writer.Reset();
            writer.Put((byte)Op.EQUIPMENT_UPDATE);      
            writer.Put(localPlayerStatus.EndPoint);
            writer.Put(equipmentData.SlotHash);
            writer.Put(equipmentData.ItemId ?? "");

            if (IsServer) netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
            else connectedPeer?.Send(writer, DeliveryMethod.ReliableOrdered);
        }


        private void SendWeaponUpdate(WeaponSyncData weaponSyncData)
        {
            if (localPlayerStatus == null || !networkStarted) return;

            writer.Reset();
            writer.Put((byte)Op.PLAYERWEAPON_UPDATE);    // opcode
            writer.Put(localPlayerStatus.EndPoint);
            writer.Put(weaponSyncData.SlotHash);
            writer.Put(weaponSyncData.ItemId ?? "");

            if (IsServer) netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
            else connectedPeer?.Send(writer, DeliveryMethod.ReliableOrdered);
        }


        private void HandleEquipmentUpdate(NetPeer sender, NetPacketReader reader)
        {
            string endPoint = reader.GetString();
            int slotHash = reader.GetInt();
            string itemId = reader.GetString();

            ApplyEquipmentUpdate(sender, slotHash, itemId).Forget();

            foreach (var p in netManager.ConnectedPeerList)
            {
                if (p == sender) continue;
                var w = new NetDataWriter();
                w.Put((byte)Op.EQUIPMENT_UPDATE);    
                w.Put(endPoint);
                w.Put(slotHash);
                w.Put(itemId);
                p.Send(w, DeliveryMethod.ReliableOrdered);
            }

        }

        private void HandleWeaponUpdate(NetPeer sender, NetPacketReader reader)
        {
            string endPoint = reader.GetString();
            int slotHash = reader.GetInt();
            string itemId = reader.GetString();

            ApplyWeaponUpdate(sender, slotHash, itemId).Forget();

            foreach (var p in netManager.ConnectedPeerList)
            {
                if (p == sender) continue;
                var w = new NetDataWriter();
                w.Put((byte)Op.PLAYERWEAPON_UPDATE);  
                w.Put(endPoint);
                w.Put(slotHash);
                w.Put(itemId);
                p.Send(w, DeliveryMethod.ReliableOrdered);
            }

        }

        // 主机：按 NetPeer 应用装备
        private async UniTask ApplyEquipmentUpdate(NetPeer peer, int slotHash, string itemId)
        {
            if (!remoteCharacters.TryGetValue(peer, out var remoteObj) || remoteObj == null) return;

            var characterModel = remoteObj.GetComponent<CharacterMainControl>().characterModel;
            if (characterModel == null) return;

            if (string.IsNullOrEmpty(itemId))
            {
                if (slotHash == 100) COOPManager.ChangeArmorModel(characterModel, null);
                if (slotHash == 200) COOPManager.ChangeHelmatModel(characterModel, null);
                if (slotHash == 300) COOPManager.ChangeFaceMaskModel(characterModel, null);
                if (slotHash == 400) COOPManager.ChangeBackpackModel(characterModel, null);
                if (slotHash == 500) COOPManager.ChangeHeadsetModel(characterModel, null);
                return;
            }

            string slotName = null;
            if (slotHash == CharacterEquipmentController.armorHash) slotName = "armorSlot";
            else if (slotHash == CharacterEquipmentController.helmatHash) slotName = "helmatSlot";
            else if (slotHash == CharacterEquipmentController.faceMaskHash) slotName = "faceMaskSlot";
            else if (slotHash == CharacterEquipmentController.backpackHash) slotName = "backpackSlot";
            else if (slotHash == CharacterEquipmentController.headsetHash) slotName = "headsetSlot";
            else
            {
                if (!string.IsNullOrEmpty(itemId) && int.TryParse(itemId, out var ids))
                {
                    Debug.Log($"尝试更新装备: {peer.EndPoint}, Slot={slotHash}, ItemId={itemId}");
                    var item = await COOPManager.GetItemAsync(ids);
                    if (item == null)
                    {
                        Debug.LogWarning($"无法获取物品: ItemId={itemId}，槽位 {slotHash} 未更新");
                    }
                    if (slotHash == 100)
                    {
                        COOPManager.ChangeArmorModel(characterModel, item);
                    }
                    if (slotHash == 200)
                    {
                        COOPManager.ChangeHelmatModel(characterModel, item);
                    }
                    if (slotHash == 300)
                    {
                        COOPManager.ChangeFaceMaskModel(characterModel, item);
                    }
                    if (slotHash == 400)
                    {
                        COOPManager.ChangeBackpackModel(characterModel, item);
                    }
                    if (slotHash == 500)
                    {
                        COOPManager.ChangeHeadsetModel(characterModel, item);
                    }
                }
                return;
            }

            try
            {
                if (int.TryParse(itemId, out var ids))
                {
                    var item = await COOPManager.GetItemAsync(ids);
                    if (item != null)
                    {
                        if (slotName == "armorSlot") COOPManager.ChangeArmorModel(characterModel, item);
                        if (slotName == "helmatSlot") COOPManager.ChangeHelmatModel(characterModel, item);
                        if (slotName == "faceMaskSlot") COOPManager.ChangeFaceMaskModel(characterModel, item);
                        if (slotName == "backpackSlot") COOPManager.ChangeBackpackModel(characterModel, item);
                        if (slotName == "headsetSlot") COOPManager.ChangeHeadsetModel(characterModel, item);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"更新装备失败(主机): {peer.EndPoint}, SlotHash={slotHash}, ItemId={itemId}, 错误: {ex.Message}");
            }
        }



        private async UniTask ApplyEquipmentUpdate_Client(string playerId, int slotHash, string itemId)
        {
            if (IsSelfId(playerId)) return;
            if (!clientRemoteCharacters.TryGetValue(playerId, out var remoteObj) || remoteObj == null) return;

            var characterModel = remoteObj.GetComponent<CharacterMainControl>().characterModel;
            if (characterModel == null) return;

            if (string.IsNullOrEmpty(itemId))
            {
                if (slotHash == 100) COOPManager.ChangeArmorModel(characterModel, null);
                if (slotHash == 200) COOPManager.ChangeHelmatModel(characterModel, null);
                if (slotHash == 300) COOPManager.ChangeFaceMaskModel(characterModel, null);
                if (slotHash == 400) COOPManager.ChangeBackpackModel(characterModel, null);
                if (slotHash == 500) COOPManager.ChangeHeadsetModel(characterModel, null);
                return;
            }

            string slotName = null;
            if (slotHash == CharacterEquipmentController.armorHash) slotName = "armorSlot";
            else if (slotHash == CharacterEquipmentController.helmatHash) slotName = "helmatSlot";
            else if (slotHash == CharacterEquipmentController.faceMaskHash) slotName = "faceMaskSlot";
            else if (slotHash == CharacterEquipmentController.backpackHash) slotName = "backpackSlot";
            else if (slotHash == CharacterEquipmentController.headsetHash) slotName = "headsetSlot";
            else
            {
                if (!string.IsNullOrEmpty(itemId) && int.TryParse(itemId, out var ids))
                {
                    var item = await COOPManager.GetItemAsync(ids);
                    if (item == null)
                    {
                        Debug.LogWarning($"无法获取物品: ItemId={itemId}，槽位 {slotHash} 未更新");
                    }
                    if (slotHash == 100)
                    {
                        COOPManager.ChangeArmorModel(characterModel, item);
                    }
                    if (slotHash == 200)
                    {
                        COOPManager.ChangeHelmatModel(characterModel, item);
                    }
                    if (slotHash == 300)
                    {
                        COOPManager.ChangeFaceMaskModel(characterModel, item);
                    }
                    if (slotHash == 400)
                    {
                        COOPManager.ChangeBackpackModel(characterModel, item);
                    }
                    if (slotHash == 500)
                    {
                        COOPManager.ChangeHeadsetModel(characterModel, item);
                    }
                }
                return;
            }

            try
            {
                if (int.TryParse(itemId, out var ids))
                {
                    var item = await COOPManager.GetItemAsync(ids);
                    if (item != null)
                    {
                        if (slotName == "armorSlot") COOPManager.ChangeArmorModel(characterModel, item);
                        if (slotName == "helmatSlot") COOPManager.ChangeHelmatModel(characterModel, item);
                        if (slotName == "faceMaskSlot") COOPManager.ChangeFaceMaskModel(characterModel, item);
                        if (slotName == "backpackSlot") COOPManager.ChangeBackpackModel(characterModel, item);
                        if (slotName == "headsetSlot") COOPManager.ChangeHeadsetModel(characterModel, item);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"更新装备失败(客户端): {playerId}, SlotHash={slotHash}, ItemId={itemId}, 错误: {ex.Message}");
            }
        }

        private readonly Dictionary<string, string> _lastWeaponAppliedByPeer = new Dictionary<string, string>();
        private readonly Dictionary<string, float> _lastWeaponAppliedTimeByPeer = new Dictionary<string, float>();
        private readonly Dictionary<string, string> _lastWeaponAppliedByPlayer = new Dictionary<string, string>();
        private readonly Dictionary<string, float> _lastWeaponAppliedTimeByPlayer = new Dictionary<string, float>();

        private static void SafeKillItemAgent(ItemStatsSystem.Item item)
        {
            if (item == null) return;
            try
            {
                var ag = item.ActiveAgent;
                if (ag != null && ag.gameObject != null)
                    UnityEngine.Object.Destroy(ag.gameObject);
            }
            catch {  }

            try { item.Detach(); } catch { }
        }

        // 只清“目标插槽”，避免每次都清三处带来的频繁销毁/创建
        private static void ClearWeaponSlot(CharacterModel model, HandheldSocketTypes socket)
        {
            COOPManager.ChangeWeaponModel(model, null, socket);
        }

        // 小工具：把 slotHash 解析为合法的 HandheldSocketTypes；无法识别时回退到右手
        private static HandheldSocketTypes ResolveSocketOrDefault(int slotHash)
        {
            var socket = (HandheldSocketTypes)slotHash;
            if (socket != HandheldSocketTypes.normalHandheld &&
                socket != HandheldSocketTypes.meleeWeapon &&
                socket != HandheldSocketTypes.leftHandSocket)
            {
                socket = HandheldSocketTypes.normalHandheld; // 回退
            }
            return socket;
        }

        private const float WeaponApplyDebounce = 0.20f; // 200ms 去抖窗口

        // 主机：按 NetPeer 应用武器
        private async UniTask ApplyWeaponUpdate(NetPeer peer, int slotHash, string itemId)
        {
            if (!remoteCharacters.TryGetValue(peer, out var remoteObj) || remoteObj == null) return;

            var cm = remoteObj.GetComponent<CharacterMainControl>();
            var model = cm ? cm.characterModel : null;
            if (model == null) return;

            // —— 幂等/去抖：同一 peer、同一槽、同一 item 在 200ms 内重复到达则忽略 ——
            string key = $"{peer?.Id ?? -1}:{slotHash}";
            string want = itemId ?? string.Empty;
            if (_lastWeaponAppliedByPeer.TryGetValue(key, out var last) && last == want)
            {
                // 同值重复，直接跳过
                return;
            }
            if (_lastWeaponAppliedTimeByPeer.TryGetValue(key, out var ts))
            {
                if (Time.time - ts < WeaponApplyDebounce && last == want)
                    return;
            }
            _lastWeaponAppliedByPeer[key] = want;
            _lastWeaponAppliedTimeByPeer[key] = Time.time;

            var socket = ResolveSocketOrDefault(slotHash);

            try
            {
                if (!string.IsNullOrEmpty(itemId) && int.TryParse(itemId, out var typeId))
                {
                    // 准备 Item，挂载前先杀残留 agent
                    var item = await COOPManager.GetItemAsync(typeId);
                    if (item != null)
                    {
                        SafeKillItemAgent(item);

                        // 只清目标插槽，避免多余的三处全清
                        ClearWeaponSlot(model, socket);

                        // 等一帧让销毁真正完成，避免“已有 agent”撞车
                        await UniTask.NextFrame();

                        // 挂载目标
                        COOPManager.ChangeWeaponModel(model, item, socket);

                        try
                        {
                            await UniTask.NextFrame(); // 让挂载真正生效，避免同帧取不到组件

                            var gun = model ? model.GetComponentInChildren<ItemAgent_Gun>(true) : null;
                            Transform mz = (gun && gun.muzzle) ? gun.muzzle : null;
                            if (!mz && model)
                            {
                                // 兜底从骨骼名找一下
                                var t = model.transform;
                                mz = t.Find("Muzzle") ??
                                     (model.RightHandSocket ? model.RightHandSocket.Find("Muzzle") : null) ??
                                     (model.LefthandSocket ? model.LefthandSocket.Find("Muzzle") : null) ??
                                     (model.MeleeWeaponSocket ? model.MeleeWeaponSocket.Find("Muzzle") : null);
                            }

                            if (playerStatuses.TryGetValue(peer, out var ps) && ps != null && !string.IsNullOrEmpty(ps.EndPoint) && gun)
                            {
                                _gunCacheByShooter[ps.EndPoint] = (gun, mz);
                            }
                        }
                        catch {}

                        // 缓存弹丸和 muzzleFx
                        var gunSetting = item.GetComponent<ItemSetting_Gun>();
                        var pfb = (gunSetting && gunSetting.bulletPfb)
                                ? gunSetting.bulletPfb
                                : Duckov.Utilities.GameplayDataSettings.Prefabs.DefaultBullet;
                        _projCacheByWeaponType[typeId] = pfb;
                        _muzzleFxCacheByWeaponType[typeId] = gunSetting ? gunSetting.muzzleFxPfb : null;
                    }
                }
                else
                {
                    // 只清指定插槽
                    ClearWeaponSlot(model, socket);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"更新武器失败(主机): {peer?.EndPoint}, Slot={socket}, ItemId={itemId}, 错误: {ex.Message}");
            }
        }


        // 客户端：按 玩家ID 应用武器（幂等 + 去抖 + 杀残留 agent + 只清目标 + 等一帧再挂）
        private async UniTask ApplyWeaponUpdate_Client(string playerId, int slotHash, string itemId)
        {
            if (IsSelfId(playerId)) return;

            if (!clientRemoteCharacters.TryGetValue(playerId, out var remoteObj) || remoteObj == null) return;
            var cm = remoteObj.GetComponent<CharacterMainControl>();
            var model = cm ? cm.characterModel : null;
            if (model == null) return;

            string key = $"{playerId}:{slotHash}";
            string want = itemId ?? string.Empty;
            if (_lastWeaponAppliedByPlayer.TryGetValue(key, out var last) && last == want)
            {
                return;
            }
            if (_lastWeaponAppliedTimeByPlayer.TryGetValue(key, out var ts))
            {
                if (Time.time - ts < WeaponApplyDebounce && last == want)
                    return;
            }
            _lastWeaponAppliedByPlayer[key] = want;
            _lastWeaponAppliedTimeByPlayer[key] = Time.time;

            var socket = ResolveSocketOrDefault(slotHash);

            try
            {
                if (!string.IsNullOrEmpty(itemId) && int.TryParse(itemId, out var typeId))
                {
                    var item = await COOPManager.GetItemAsync(typeId);
                    if (item != null)
                    {
                        SafeKillItemAgent(item);

                        ClearWeaponSlot(model, socket);
                        await UniTask.NextFrame();

                        COOPManager.ChangeWeaponModel(model, item, socket);

                        var gunSetting = item.GetComponent<ItemSetting_Gun>();
                        var pfb = (gunSetting && gunSetting.bulletPfb)
                                ? gunSetting.bulletPfb
                                : Duckov.Utilities.GameplayDataSettings.Prefabs.DefaultBullet;
                        _projCacheByWeaponType[typeId] = pfb;
                        _muzzleFxCacheByWeaponType[typeId] = gunSetting ? gunSetting.muzzleFxPfb : null;
                    }
                }
                else
                {
                    ClearWeaponSlot(model, socket);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"更新武器失败(客户端): {playerId}, Slot={socket}, ItemId={itemId}, 错误: {ex.Message}");
            }
        }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            if (IsServer)
            {
                if (request.Data != null && request.Data.GetString() == "gameKey") request.Accept();
                else request.Reject();
            }
            else request.Reject();
        }

        public void OnPeerConnected(NetPeer peer)
        {
            Debug.Log($"连接成功: {peer.EndPoint}");
            connectedPeer = peer;

            if (!IsServer)
            {
                status = Main.Localization.GetFormatted("status_connected_to", peer.EndPoint);
                isConnecting = false;
                SendClientStatusUpdate();
            }

            if (!playerStatuses.ContainsKey(peer))
            {
                playerStatuses[peer] = new PlayerStatus
                {
                    EndPoint = peer.EndPoint.ToString(),
                    PlayerName = IsServer ? $"Player_{peer.Id}" : "Host",
                    Latency = peer.Ping,
                    IsInGame = false,
                    LastIsInGame = false,
                    Position = Vector3.zero,
                    Rotation = Quaternion.identity,
                    CustomFaceJson = null
                };
            }

            if (IsServer) SendPlayerStatusUpdate();

            if (IsServer)
            {
                // 1) 主机自己
                var hostMain = CharacterMainControl.Main;
                var hostH = hostMain ? hostMain.GetComponentInChildren<Health>(true) : null;
                if (hostH)
                {
                    var w = new NetDataWriter();
                    w.Put((byte)Op.AUTH_HEALTH_REMOTE);
                    w.Put(GetPlayerId(null)); // Host 的 playerId
                    try { w.Put(hostH.MaxHealth); } catch { w.Put(0f); }
                    try { w.Put(hostH.CurrentHealth); } catch { w.Put(0f); }
                    peer.Send(w, DeliveryMethod.ReliableOrdered);
                }

                if (remoteCharacters != null)
                {
                    foreach (var kv in remoteCharacters)
                    {
                        var owner = kv.Key;
                        var go = kv.Value;

                        if (owner == null || go == null) continue;

                        var h = go.GetComponentInChildren<Health>(true);
                        if (!h) continue;

                        var w = new NetDataWriter();
                        w.Put((byte)Op.AUTH_HEALTH_REMOTE);
                        w.Put(GetPlayerId(owner)); // 原主的 playerId
                        try { w.Put(h.MaxHealth); } catch { w.Put(0f); }
                        try { w.Put(h.CurrentHealth); } catch { w.Put(0f); }
                        peer.Send(w, DeliveryMethod.ReliableOrdered);
                    }
                }
            }

        }

        // 可选：若你保留了非Q方法，务必也统一
        private void HandlePositionUpdate(NetPeer sender, NetPacketReader reader)
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


        private void HandlePositionUpdate_Q(NetPeer peer, string endPoint, Vector3 position, Quaternion rotation)
        {
            if (peer != null && playerStatuses.TryGetValue(peer, out var st))
            {
                st.Position = position;
                st.Rotation = rotation;

                if (remoteCharacters.TryGetValue(peer, out var go) && go != null)
                {
                    var ni = NetInterpUtil.Attach(go);
                    ni?.Push(position, rotation);
                }

                foreach (var p in netManager.ConnectedPeerList)
                {
                    if (p == peer) continue;
                    writer.Reset();
                    writer.Put((byte)Op.POSITION_UPDATE);
                    writer.Put(st.EndPoint ?? endPoint);
                    writer.PutV3cm(position);
                    Vector3 fwd = rotation * Vector3.forward;
                    writer.PutDir(fwd);
                    p.Send(writer, DeliveryMethod.Unreliable);
                }
            }
        }


        private void HandleClientStatusUpdate(NetPeer peer, NetPacketReader reader)
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

            if (!playerStatuses.ContainsKey(peer))
                playerStatuses[peer] = new PlayerStatus();

            var st = playerStatuses[peer];
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

            if (isInGame && !remoteCharacters.ContainsKey(peer))
            {
                CreateRemoteCharacterAsync(peer, position, rotation, customFaceJson).Forget();
                foreach (var e in equipmentList) ApplyEquipmentUpdate(peer, e.SlotHash, e.ItemId).Forget();
                foreach (var w in weaponList) ApplyWeaponUpdate(peer, w.SlotHash, w.ItemId).Forget();
            }
            else if (isInGame)
            {
                if (remoteCharacters.TryGetValue(peer, out var go) && go != null)
                {
                    go.transform.position = position;
                    go.GetComponentInChildren<CharacterMainControl>().modelRoot.transform.rotation = rotation;
                }
                foreach (var e in equipmentList) ApplyEquipmentUpdate(peer, e.SlotHash, e.ItemId).Forget();
                foreach (var w in weaponList) ApplyWeaponUpdate(peer, w.SlotHash, w.ItemId).Forget();
            }

            playerStatuses[peer] = st;

            SendPlayerStatusUpdate();

        }

        // 客户端：拦截本地生成后，向主机发开火请求（带上 clientScatter / ads01）
        public void Net_OnClientShoot(ItemAgent_Gun gun, Vector3 muzzle, Vector3 baseDir, Vector3 firstCheckStart)
        {
            if (IsServer || connectedPeer == null) return;

            if (baseDir.sqrMagnitude < 1e-8f)
            {
                var fallback = (gun != null && gun.muzzle != null) ? gun.muzzle.forward : Vector3.forward;
                baseDir = fallback.sqrMagnitude < 1e-8f ? Vector3.forward : fallback.normalized;
            }

            if (gun && gun.muzzle)
            {
                int weaponType = (gun.Item != null) ? gun.Item.TypeID : 0;
                Client_PlayLocalShotFx(gun, gun.muzzle, weaponType);
            }

            writer.Reset();
            writer.Put((byte)Op.FIRE_REQUEST);        // opcode
            writer.Put(localPlayerStatus.EndPoint);   // shooterId
            writer.Put(gun.Item.TypeID);              // weaponType
            writer.PutV3cm(muzzle);
            writer.PutDir(baseDir);
            writer.PutV3cm(firstCheckStart);

            // === 新增：把当前这一枪的散布与ADS状态作为提示发给主机 ===
            float clientScatter = 0f;
            float ads01 = 0f;
            try
            {
                clientScatter = Mathf.Max(0f, gun.CurrentScatter); // 客户端这帧真实散布（已包含ADS影响）
                ads01 = (gun.IsInAds ? 1f : 0f);
            }
            catch { }
            writer.Put(clientScatter);
            writer.Put(ads01);

            // 仍旧带原有的“提示载荷”，用于爆炸等参数兜底
            var hint = new ProjectileContext();
            try
            {
                bool hasBulletItem = (gun.BulletItem != null);

                // 伤害
                float charMul = gun.CharacterDamageMultiplier;
                float bulletMul = hasBulletItem ? Mathf.Max(0.0001f, gun.BulletDamageMultiplier) : 1f;
                int shots = Mathf.Max(1, gun.ShotCount);
                hint.damage = gun.Damage * bulletMul * charMul / shots;
                if (gun.Damage > 1f && hint.damage < 1f) hint.damage = 1f;

                // 暴击
                float bulletCritRateGain = hasBulletItem ? gun.bulletCritRateGain : 0f;
                float bulletCritDmgGain = hasBulletItem ? gun.BulletCritDamageFactorGain : 0f;
                hint.critDamageFactor = (gun.CritDamageFactor + bulletCritDmgGain) * (1f + gun.CharacterGunCritDamageGain);
                hint.critRate = gun.CritRate * (1f + gun.CharacterGunCritRateGain + bulletCritRateGain);

                // 元素/破甲/爆炸/流血等（保持你原有写法）
                switch (gun.GunItemSetting.element)
                {
                    case ElementTypes.physics: hint.element_Physics = 1f; break;
                    case ElementTypes.fire: hint.element_Fire = 1f; break;
                    case ElementTypes.poison: hint.element_Poison = 1f; break;
                    case ElementTypes.electricity: hint.element_Electricity = 1f; break;
                    case ElementTypes.space: hint.element_Space = 1f; break;
                }

                hint.armorPiercing = gun.ArmorPiercing + (hasBulletItem ? gun.BulletArmorPiercingGain : 0f);
                hint.armorBreak = gun.ArmorBreak + (hasBulletItem ? gun.BulletArmorBreakGain : 0f);
                hint.explosionRange = gun.BulletExplosionRange;
                hint.explosionDamage = gun.BulletExplosionDamage * gun.ExplosionDamageMultiplier;
                if (hasBulletItem)
                {
                    hint.buffChance = gun.BulletBuffChanceMultiplier * gun.BuffChance;
                    hint.bleedChance = gun.BulletBleedChance;
                }
                hint.penetrate = gun.Penetrate;
                hint.fromWeaponItemID = (gun.Item != null ? gun.Item.TypeID : 0);
            }
            catch { /* 忽略 */ }

            writer.PutProjectilePayload(hint);  // 带着提示载荷发给主机
            connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        }


        private void HandleFireRequest(NetPeer peer, NetPacketReader r)
        {
            string shooterId = r.GetString();
            int weaponType = r.GetInt();
            Vector3 muzzle = r.GetV3cm();
            Vector3 baseDir = r.GetDir();
            Vector3 firstCheckStart = r.GetV3cm();

            // === 新增：读取客户端这帧的散布 & ADS 提示 ===
            float clientScatter = 0f;
            float ads01 = 0f;
            try
            {
                clientScatter = r.GetFloat();
                ads01 = r.GetFloat();
            }
            catch
            {
                clientScatter = 0f; ads01 = 0f; // 兼容老包
            }

            // 读取客户端随包提示载荷（可能不存在，Try 不会抛异常）
            _payloadHint = default;
            _hasPayloadHint = NetPack_Projectile.TryGetProjectilePayload(r, ref _payloadHint);

            if (!remoteCharacters.TryGetValue(peer, out var who) || !who) { _hasPayloadHint = false; return; }

            var cm = who.GetComponent<CharacterMainControl>().characterModel;

            // —— 贪婪地查找远端玩家的枪 —— 
            ItemAgent_Gun gun = null;
            if (cm)
            {
                try
                {
                    gun = who.GetComponent<CharacterMainControl>()?.GetGun();
                    if (!gun && cm.RightHandSocket) gun = cm.RightHandSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                    if (!gun && cm.LefthandSocket) gun = cm.LefthandSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                    if (!gun && cm.MeleeWeaponSocket) gun = cm.MeleeWeaponSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                }
                catch { }
            }

            // 找不到 muzzle 就从骨骼里兜底
            if (muzzle == default || muzzle.sqrMagnitude < 1e-8f)
            {
                Transform mz = null;
                if (cm)
                {
                    if (!mz && cm.RightHandSocket) mz = cm.RightHandSocket.Find("Muzzle");
                    if (!mz && cm.LefthandSocket) mz = cm.LefthandSocket.Find("Muzzle");
                    if (!mz && cm.MeleeWeaponSocket) mz = cm.MeleeWeaponSocket.Find("Muzzle");
                }
                if (!mz) mz = who.transform.Find("Muzzle");
                if (mz) muzzle = mz.position;
            }

            // —— 有 gun 走权威生成；无 gun 走可视兜底 —— 
            Vector3 finalDir;
            float speed, distance;

            if (gun) // 正常路径：主机生成真正的弹丸
            {
                if (!Server_SpawnProjectile(gun, muzzle, baseDir, firstCheckStart, out finalDir, clientScatter, ads01))
                { _hasPayloadHint = false; return; }

                speed = gun.BulletSpeed * (gun.Holder ? gun.Holder.GunBulletSpeedMultiplier : 1f);
                distance = gun.BulletDistance + 0.4f;
            }
            else
            {
                // 没 gun 的可视兜底
                finalDir = (baseDir.sqrMagnitude > 1e-8f ? baseDir.normalized : Vector3.forward);
                speed = _speedCacheByWeaponType.TryGetValue(weaponType, out var sp) ? sp : 60f;
                distance = _distCacheByWeaponType.TryGetValue(weaponType, out var dist) ? dist : 50f;
                // 可选：也可以在服务器生成一个“无 holder”的 Projectile（略）
            }

            // —— 广播 FIRE_EVENT（带主机权威 ctx）——
            writer.Reset();
            writer.Put((byte)Op.FIRE_EVENT);
            writer.Put(shooterId);
            writer.Put(weaponType);
            writer.PutV3cm(muzzle);
            writer.PutDir(finalDir);
            writer.Put(speed);
            writer.Put(distance);

            var payloadCtx = new ProjectileContext();
            if (gun != null)
            {
                bool hasBulletItem = false;
                try { hasBulletItem = (gun.BulletItem != null); } catch { }

                // …（保留你原有的 payload 构造，略）…
                try
                {
                    float charMul = gun.CharacterDamageMultiplier;
                    float bulletMul = hasBulletItem ? Mathf.Max(0.0001f, gun.BulletDamageMultiplier) : 1f;
                    int shots = Mathf.Max(1, gun.ShotCount);
                    payloadCtx.damage = gun.Damage * bulletMul * charMul / shots;
                    if (gun.Damage > 1f && payloadCtx.damage < 1f) payloadCtx.damage = 1f;
                }
                catch { if (payloadCtx.damage <= 0f) payloadCtx.damage = 1f; }

                try
                {
                    float bulletCritRateGain = hasBulletItem ? gun.bulletCritRateGain : 0f;
                    float bulletCritDmgGain = hasBulletItem ? gun.BulletCritDamageFactorGain : 0f;
                    payloadCtx.critDamageFactor = (gun.CritDamageFactor + bulletCritDmgGain) * (1f + gun.CharacterGunCritDamageGain);
                    payloadCtx.critRate = gun.CritRate * (1f + gun.CharacterGunCritRateGain + bulletCritRateGain);
                }
                catch { }

                try
                {
                    float apGain = hasBulletItem ? gun.BulletArmorPiercingGain : 0f;
                    float abGain = hasBulletItem ? gun.BulletArmorBreakGain : 0f;
                    payloadCtx.armorPiercing = gun.ArmorPiercing + apGain;
                    payloadCtx.armorBreak = gun.ArmorBreak + abGain;
                }
                catch { }

                try
                {
                    var setting = gun.GunItemSetting;
                    if (setting != null)
                    {
                        switch (setting.element)
                        {
                            case ElementTypes.physics: payloadCtx.element_Physics = 1f; break;
                            case ElementTypes.fire: payloadCtx.element_Fire = 1f; break;
                            case ElementTypes.poison: payloadCtx.element_Poison = 1f; break;
                            case ElementTypes.electricity: payloadCtx.element_Electricity = 1f; break;
                            case ElementTypes.space: payloadCtx.element_Space = 1f; break;
                        }
                    }

                    payloadCtx.explosionRange = gun.BulletExplosionRange;
                    payloadCtx.explosionDamage = gun.BulletExplosionDamage * gun.ExplosionDamageMultiplier;

                    if (hasBulletItem)
                    {
                        payloadCtx.buffChance = gun.BulletBuffChanceMultiplier * gun.BuffChance;
                        payloadCtx.bleedChance = gun.BulletBleedChance;
                    }

                    payloadCtx.penetrate = gun.Penetrate;
                    payloadCtx.fromWeaponItemID = (gun.Item != null ? gun.Item.TypeID : 0);
                }
                catch { }
            }

            writer.PutProjectilePayload(payloadCtx);
            netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);

            PlayMuzzleFxAndShell(shooterId, weaponType, muzzle, finalDir);
            PlayShootAnimOnServerPeer(peer);

            // 清理本次 hint 状态
            _hasPayloadHint = false;
        }




        private void PlayShootAnimOnServerPeer(NetPeer peer)
        {
            if (!remoteCharacters.TryGetValue(peer, out var who) || !who) return;
            var animCtrl = who.GetComponent<CharacterMainControl>().characterModel.GetComponentInParent<CharacterAnimationControl_MagicBlend>();
            if (animCtrl && animCtrl.animator)
            {
                animCtrl.OnAttack();  // 这个控制器里会触发 Attack trigger + 攻击图层权重曲线
            }
        }

        private void PlayMuzzleFxAndShell(string shooterId, int weaponType, Vector3 muzzlePos, Vector3 finalDir)
        {
            try
            {
                // 1) 定位 shooter GameObject
                GameObject shooterGo = null;
                if (IsSelfId(shooterId))
                {
                    var cmSelf = LevelManager.Instance?.MainCharacter?.GetComponent<CharacterMainControl>();
                    if (cmSelf) shooterGo = cmSelf.gameObject;
                }
                else if (!string.IsNullOrEmpty(shooterId) && shooterId.StartsWith("AI:"))
                {
                    if (int.TryParse(shooterId.Substring(3), out var aiId))
                    {
                        if (aiById.TryGetValue(aiId, out var cmc) && cmc)
                            shooterGo = cmc.gameObject;
                    }
                }
                else
                {
                    if (IsServer)
                    {
                        // Server：EndPoint -> NetPeer -> remoteCharacters
                        NetPeer foundPeer = null;
                        foreach (var kv in playerStatuses)
                        {
                            if (kv.Value != null && kv.Value.EndPoint == shooterId) { foundPeer = kv.Key; break; }
                        }
                        if (foundPeer != null) remoteCharacters.TryGetValue(foundPeer, out shooterGo);
                    }
                    else
                    {
                        // Client：直接用 shooterId 查远端克隆
                        clientRemoteCharacters.TryGetValue(shooterId, out shooterGo);
                    }
                }

                // 2) 尝试命中缓存（避免每包 GetComponentInChildren）
                ItemAgent_Gun gun = null;
                Transform muzzleTf = null;
                if (!string.IsNullOrEmpty(shooterId))
                {
                    if (_gunCacheByShooter.TryGetValue(shooterId, out var cached) && cached.gun)
                    {
                        gun = cached.gun;
                        muzzleTf = cached.muzzle;
                    }
                }

                // 3) 缓存未命中 → 扫描一次并写入缓存
                if (shooterGo && (!gun || !muzzleTf))
                {
                    var cmc = shooterGo.GetComponent<CharacterMainControl>();
                    var model = cmc ? cmc.characterModel : null;

                    if (!gun && model)
                    {
                        if (model.RightHandSocket && !gun) gun = model.RightHandSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                        if (model.LefthandSocket && !gun) gun = model.LefthandSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                        if (model.MeleeWeaponSocket && !gun) gun = model.MeleeWeaponSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                    }
                    if (!gun) gun = cmc ? (cmc.CurrentHoldItemAgent as ItemAgent_Gun) : null;

                    if (gun && gun.muzzle && !muzzleTf) muzzleTf = gun.muzzle;

                    if (!string.IsNullOrEmpty(shooterId) && gun)
                    {
                        _gunCacheByShooter[shooterId] = (gun, muzzleTf);
                    }
                }

                // 4) 没有 muzzle 就用兜底挂点（只负责火光，不做抛壳/回座力）
                GameObject tmp = null;
                if (!muzzleTf)
                {
                    tmp = new GameObject("TempMuzzleFX");
                    tmp.transform.position = muzzlePos;
                    tmp.transform.rotation = Quaternion.LookRotation(finalDir, Vector3.up);
                    muzzleTf = tmp.transform;
                }

                // 5) 真正播放（包含火光 + 抛壳 + 回座力；gun==null 时内部仅火光）
                Client_PlayLocalShotFx(gun, muzzleTf, weaponType);

                if (tmp) Destroy(tmp, 0.2f);

                // 6) 非主机端本地顺带触发一次攻击动画（和你原逻辑一致）
                if (!IsServer && shooterGo)
                {
                    var anim = shooterGo.GetComponentInChildren<CharacterAnimationControl_MagicBlend>(true);
                    if (anim && anim.animator) anim.OnAttack();
                }
            }
            catch
            {
                // 保底，避免任何异常打断网络流
            }
        }



        private void TryStartVisualRecoil(ItemAgent_Gun gun)
        {
            if (!gun) return;
            try
            {
                Traverse.Create(gun).Method("StartVisualRecoil").GetValue();
                return;
            }
            catch { }

            try
            {
                // 兜底：等价于 StartVisualRecoil() 内部把 _recoilBack=true
                Traverse.Create(gun).Field<bool>("_recoilBack").Value = true;
            }
            catch { }
        }


        private void Host_OnMainCharacterShoot(ItemAgent_Gun gun)
        {
            if (!networkStarted || !IsServer) return;
            if (gun == null || gun.Holder == null || !gun.Holder.IsMainCharacter) return;

            var proj = Traverse.Create(gun).Field<Projectile>("projInst").Value;
            if (proj == null) return;

            Vector3 finalDir = proj.transform.forward;
            if (finalDir.sqrMagnitude < 1e-8f) finalDir = (gun.muzzle ? gun.muzzle.forward : Vector3.forward);
            finalDir.Normalize();

            Vector3 muzzleWorld = proj.transform.position;
            float speed = gun.BulletSpeed * (gun.Holder ? gun.Holder.GunBulletSpeedMultiplier : 1f);
            float distance = gun.BulletDistance + 0.4f;

            var w = writer;
            w.Reset();
            w.Put((byte)Op.FIRE_EVENT);
            w.Put(localPlayerStatus.EndPoint);
            w.Put(gun.Item.TypeID);
            w.PutV3cm(muzzleWorld);
            w.PutDir(finalDir);
            w.Put(speed);
            w.Put(distance);

            var payloadCtx = new ProjectileContext();

            bool hasBulletItem = false;
            try { hasBulletItem = (gun.BulletItem != null); } catch { }

            float charMul = 1f, bulletMul = 1f;
            int shots = 1;
            try
            {
                charMul = gun.CharacterDamageMultiplier;
                bulletMul = hasBulletItem ? Mathf.Max(0.0001f, gun.BulletDamageMultiplier) : 1f;
                shots = Mathf.Max(1, gun.ShotCount);
            }
            catch { }

            try
            {
                payloadCtx.damage = gun.Damage * bulletMul * charMul / shots;
                if (gun.Damage > 1f && payloadCtx.damage < 1f) payloadCtx.damage = 1f;
            }
            catch { if (payloadCtx.damage <= 0f) payloadCtx.damage = 1f; }

            try
            {
                float bulletCritRateGain = hasBulletItem ? gun.bulletCritRateGain : 0f;
                float bulletCritDmgGain = hasBulletItem ? gun.BulletCritDamageFactorGain : 0f;
                payloadCtx.critDamageFactor = (gun.CritDamageFactor + bulletCritDmgGain) * (1f + gun.CharacterGunCritDamageGain);
                payloadCtx.critRate = gun.CritRate * (1f + gun.CharacterGunCritRateGain + bulletCritRateGain);
            }
            catch { }

            try
            {
                float apGain = hasBulletItem ? gun.BulletArmorPiercingGain : 0f;
                float abGain = hasBulletItem ? gun.BulletArmorBreakGain : 0f;
                payloadCtx.armorPiercing = gun.ArmorPiercing + apGain;
                payloadCtx.armorBreak = gun.ArmorBreak + abGain;
            }
            catch { }

            try
            {
                var setting = gun.GunItemSetting;
                if (setting != null)
                {
                    switch (setting.element)
                    {
                        case ElementTypes.physics: payloadCtx.element_Physics = 1f; break;
                        case ElementTypes.fire: payloadCtx.element_Fire = 1f; break;
                        case ElementTypes.poison: payloadCtx.element_Poison = 1f; break;
                        case ElementTypes.electricity: payloadCtx.element_Electricity = 1f; break;
                        case ElementTypes.space: payloadCtx.element_Space = 1f; break;
                    }
                }

                payloadCtx.explosionRange = gun.BulletExplosionRange;
                payloadCtx.explosionDamage = gun.BulletExplosionDamage * gun.ExplosionDamageMultiplier;

                if (hasBulletItem)
                {
                    payloadCtx.buffChance = gun.BulletBuffChanceMultiplier * gun.BuffChance;
                    payloadCtx.bleedChance = gun.BulletBleedChance;
                }

                payloadCtx.penetrate = gun.Penetrate;
                payloadCtx.fromWeaponItemID = gun.Item.TypeID;
            }
            catch { }

            w.PutProjectilePayload(payloadCtx);
            netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);

            PlayMuzzleFxAndShell(localPlayerStatus.EndPoint, gun.Item.TypeID, muzzleWorld, finalDir);
        }

        // 主机：真正生成子弹（用 clientScatter 代替本地的 gun.CurrentScatter 参与随机散布）
        bool Server_SpawnProjectile(ItemAgent_Gun gun, Vector3 muzzle, Vector3 baseDir, Vector3 firstCheckStart, out Vector3 finalDir, float clientScatter, float ads01)
        {
            finalDir = baseDir.sqrMagnitude < 1e-8f ? Vector3.forward : baseDir.normalized;

            // ====== 随机散布仍由主机来做，但幅度优先用客户端提示的散布 ======
            bool isMain = (gun.Holder && gun.Holder.IsMainCharacter);
            float extra = 0f;
            if (isMain)
            {
                // 和原版一致：仅主角才叠加耐久衰减额外散布
                extra = Mathf.Max(1f, gun.CurrentScatter) * Mathf.Lerp(1.5f, 0f, Mathf.InverseLerp(0f, 0.5f, gun.durabilityPercent));
            }

            // 核心：优先采用客户端当前帧的散布（它已把ADS影响折进 CurrentScatter 里）
            float usedScatter = (clientScatter > 0f ? clientScatter : gun.CurrentScatter);

            // 计算偏航
            float yaw = UnityEngine.Random.Range(-0.5f, 0.5f) * (usedScatter + extra);
            finalDir = (Quaternion.Euler(0f, yaw, 0f) * finalDir).normalized;

            // ====== 生成 Projectile ======
            var projectile = (gun.GunItemSetting && gun.GunItemSetting.bulletPfb)
                ? gun.GunItemSetting.bulletPfb
                : Duckov.Utilities.GameplayDataSettings.Prefabs.DefaultBullet;

            var projInst = LevelManager.Instance.BulletPool.GetABullet(projectile);
            projInst.transform.position = muzzle;
            if (finalDir.sqrMagnitude < 1e-8f) finalDir = Vector3.forward;
            projInst.transform.rotation = Quaternion.LookRotation(finalDir, Vector3.up);

            // ====== 依赖 Holder/子弹 的数值（保持你原来的兜底写法，不改动） ======
            float characterDamageMultiplier = (gun.Holder != null) ? gun.CharacterDamageMultiplier : 1f;
            float gunBulletSpeedMul = (gun.Holder != null) ? gun.Holder.GunBulletSpeedMultiplier : 1f;

            bool hasBulletItem = (gun.BulletItem != null);
            float bulletDamageMul = hasBulletItem ? gun.BulletDamageMultiplier : 1f;
            float bulletCritRateGain = hasBulletItem ? gun.bulletCritRateGain : 0f;
            float bulletCritDmgGain = hasBulletItem ? gun.BulletCritDamageFactorGain : 0f;
            float bulletArmorPiercingGain = hasBulletItem ? gun.BulletArmorPiercingGain : 0f;
            float bulletArmorBreakGain = hasBulletItem ? gun.BulletArmorBreakGain : 0f;
            float bulletExplosionRange = hasBulletItem ? gun.BulletExplosionRange : 0f;
            float bulletExplosionDamage = hasBulletItem ? gun.BulletExplosionDamage : 0f;
            float bulletBuffChanceMul = hasBulletItem ? gun.BulletBuffChanceMultiplier : 0f;
            float bulletBleedChance = hasBulletItem ? gun.BulletBleedChance : 0f;

            // === 若 BulletItem 缺失，用“客户端提示载荷/本地缓存”兜底爆炸参数（保持原样） ===
            try
            {
                if (bulletExplosionRange <= 0f)
                {
                    if (_hasPayloadHint && _payloadHint.fromWeaponItemID == gun.Item.TypeID && _payloadHint.explosionRange > 0f)
                        bulletExplosionRange = _payloadHint.explosionRange;
                    else if (_explRangeCacheByWeaponType.TryGetValue(gun.Item.TypeID, out var cachedR))
                        bulletExplosionRange = cachedR;
                }
                if (bulletExplosionDamage <= 0f)
                {
                    if (_hasPayloadHint && _payloadHint.fromWeaponItemID == gun.Item.TypeID && _payloadHint.explosionDamage > 0f)
                        bulletExplosionDamage = _payloadHint.explosionDamage;
                    else if (_explDamageCacheByWeaponType.TryGetValue(gun.Item.TypeID, out var cachedD))
                        bulletExplosionDamage = cachedD;
                }
                if (bulletExplosionRange > 0f) _explRangeCacheByWeaponType[gun.Item.TypeID] = bulletExplosionRange;
                if (bulletExplosionDamage > 0f) _explDamageCacheByWeaponType[gun.Item.TypeID] = bulletExplosionDamage;
            }
            catch { }

            var ctx = new ProjectileContext
            {
                firstFrameCheck = true,
                firstFrameCheckStartPoint = firstCheckStart,
                direction = finalDir,
                speed = gun.BulletSpeed * gunBulletSpeedMul,
                distance = gun.BulletDistance + 0.4f,
                halfDamageDistance = (gun.BulletDistance + 0.4f) * 0.5f,
                critDamageFactor = (gun.CritDamageFactor + bulletCritDmgGain) * (1f + gun.CharacterGunCritDamageGain),
                critRate = gun.CritRate * (1f + gun.CharacterGunCritRateGain + bulletCritRateGain),
                armorPiercing = gun.ArmorPiercing + bulletArmorPiercingGain,
                armorBreak = gun.ArmorBreak + bulletArmorBreakGain,
                explosionRange = bulletExplosionRange,
                explosionDamage = bulletExplosionDamage * gun.ExplosionDamageMultiplier,
                bleedChance = bulletBleedChance,
                fromWeaponItemID = gun.Item.TypeID
            };

            // 伤害（和你原来的除以 ShotCount 的逻辑一致）
            int perShotDiv = Mathf.Max(1, gun.ShotCount);
            ctx.damage = gun.Damage * bulletDamageMul * characterDamageMultiplier / perShotDiv;
            if (gun.Damage > 1f && ctx.damage < 1f) ctx.damage = 1f;

            // 元素
            switch (gun.GunItemSetting.element)
            {
                case ElementTypes.physics: ctx.element_Physics = 1f; break;
                case ElementTypes.fire: ctx.element_Fire = 1f; break;
                case ElementTypes.poison: ctx.element_Poison = 1f; break;
                case ElementTypes.electricity: ctx.element_Electricity = 1f; break;
                case ElementTypes.space: ctx.element_Space = 1f; break;
            }

            if (bulletBuffChanceMul > 0f)
            {
                ctx.buffChance = bulletBuffChanceMul * gun.BuffChance;
            }

            // fromCharacter / team 兜底，确保进入伤害系统
            if (gun.Holder)
            {
                ctx.fromCharacter = gun.Holder;
                ctx.team = gun.Holder.Team;
                if (gun.Holder.HasNearByHalfObsticle()) ctx.ignoreHalfObsticle = true;
            }
            else
            {
                var hostChar = LevelManager.Instance?.MainCharacter;
                if (hostChar != null)
                {
                    ctx.team = hostChar.Team;
                    ctx.fromCharacter = hostChar;
                }
            }
            if (ctx.critRate > 0.99f) ctx.ignoreHalfObsticle = true;

            projInst.Init(ctx);
            _serverSpawnedFromClient.Add(projInst);
            return true;
        }



        private void HandleFireEvent(NetPacketReader r)
        {
            // —— 主机广播的“射击视觉事件”的基础参数 —— 
            string shooterId = r.GetString();
            int weaponType = r.GetInt();
            Vector3 muzzle = r.GetV3cm();     
            Vector3 dir = r.GetDir();     
            float speed = r.GetFloat();
            float distance = r.GetFloat();

            // 尝试找到“开火者”的枪口（仅用于起点兜底/特效）
            CharacterMainControl shooterCMC = null;
            if (IsSelfId(shooterId)) shooterCMC = CharacterMainControl.Main;
            else if (clientRemoteCharacters.TryGetValue(shooterId, out var shooterGo) && shooterGo)
                shooterCMC = shooterGo.GetComponent<CharacterMainControl>();

            ItemAgent_Gun gun = null; Transform muzzleTf = null;
            if (shooterCMC && shooterCMC.characterModel)
            {
                gun = shooterCMC.GetGun();
                var model = shooterCMC.characterModel;
                if (!gun && model.RightHandSocket) gun = model.RightHandSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                if (!gun && model.LefthandSocket) gun = model.LefthandSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                if (!gun && model.MeleeWeaponSocket) gun = model.MeleeWeaponSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                if (gun) muzzleTf = gun.muzzle;
            }

            // 生成起点（优先网络给的 muzzle，失败再用枪口/自身）
            Vector3 spawnPos = muzzleTf ? muzzleTf.position : muzzle;

            // —— 先用主机载荷初始化 ctx（关键：包含 explosionRange / explosionDamage）——
            var ctx = new ProjectileContext
            {
                direction = dir,
                speed = speed,
                distance = distance,
                halfDamageDistance = distance * 0.5f,
                firstFrameCheck = true,
                firstFrameCheckStartPoint = muzzle,
                team = (shooterCMC && shooterCMC) ? shooterCMC.Team :
                       (LevelManager.Instance?.MainCharacter ? LevelManager.Instance.MainCharacter.Team : Teams.player)
            };

            bool gotPayload = (r.AvailableBytes > 0) && NetPack_Projectile.TryGetProjectilePayload(r, ref ctx);

            // —— 只有在“旧包/无载荷”的情况下，才用本地枪械做兜底推导 —— 
            if (!gotPayload && gun != null)
            {
                bool hasBulletItem = false;
                try { hasBulletItem = (gun.BulletItem != null); } catch { }

                // 伤害
                try
                {
                    float charMul = Mathf.Max(0.0001f, gun.CharacterDamageMultiplier);
                    float bulletMul = hasBulletItem ? Mathf.Max(0.0001f, gun.BulletDamageMultiplier) : 1f;
                    int shots = Mathf.Max(1, gun.ShotCount);
                    ctx.damage = gun.Damage * bulletMul * charMul / shots;
                    if (gun.Damage > 1f && ctx.damage < 1f) ctx.damage = 1f;
                }
                catch { if (ctx.damage <= 0f) ctx.damage = 1f; }

                // 暴击
                try
                {
                    ctx.critDamageFactor = (gun.CritDamageFactor + gun.BulletCritDamageFactorGain) * (1f + gun.CharacterGunCritDamageGain);
                    ctx.critRate = gun.CritRate * (1f + gun.CharacterGunCritRateGain + gun.bulletCritRateGain);
                }
                catch { }

                // 破甲
                try
                {
                    float apGain = hasBulletItem ? gun.BulletArmorPiercingGain : 0f;
                    float abGain = hasBulletItem ? gun.BulletArmorBreakGain : 0f;
                    ctx.armorPiercing = gun.ArmorPiercing + apGain;
                    ctx.armorBreak = gun.ArmorBreak + abGain;
                }
                catch { }

                // 元素
                try
                {
                    var setting = gun.GunItemSetting;
                    if (setting != null)
                    {
                        switch (setting.element)
                        {
                            case ElementTypes.physics: ctx.element_Physics = 1f; break;
                            case ElementTypes.fire: ctx.element_Fire = 1f; break;
                            case ElementTypes.poison: ctx.element_Poison = 1f; break;
                            case ElementTypes.electricity: ctx.element_Electricity = 1f; break;
                            case ElementTypes.space: ctx.element_Space = 1f; break;
                        }
                    }
                }
                catch { }

                // 状态 / 爆炸 / 穿透（注意：只有“无载荷”才从本地枪写入爆炸参数）
                try
                {
                    if (hasBulletItem)
                    {
                        ctx.buffChance = gun.BulletBuffChanceMultiplier * gun.BuffChance;
                        ctx.bleedChance = gun.BulletBleedChance;
                    }
                    ctx.explosionRange = gun.BulletExplosionRange;                                // 注意!!!!← RPG 的关键
                    ctx.explosionDamage = gun.BulletExplosionDamage * gun.ExplosionDamageMultiplier;
                    ctx.penetrate = gun.Penetrate;

                    if (ctx.fromWeaponItemID == 0 && gun.Item != null)
                        ctx.fromWeaponItemID = gun.Item.TypeID;
                }
                catch
                {
                    if (ctx.fromWeaponItemID == 0) ctx.fromWeaponItemID = weaponType;
                }

                if (ctx.halfDamageDistance <= 0f) ctx.halfDamageDistance = ctx.distance * 0.5f;

                try
                {
                    if (gun.Holder && gun.Holder.HasNearByHalfObsticle()) ctx.ignoreHalfObsticle = true;
                    if (ctx.critRate > 0.99f) ctx.ignoreHalfObsticle = true;
                }
                catch { }
            }

            if (gotPayload && ctx.explosionRange <= 0f && gun != null)
            {
                try
                {
                    ctx.explosionRange = gun.BulletExplosionRange;
                    ctx.explosionDamage = gun.BulletExplosionDamage * gun.ExplosionDamageMultiplier;
                }
                catch { }
            }

            // 生成弹丸（客户端只做可视；爆炸逻辑由 Projectile 基于 ctx.explosionRange>0 触发）
            Projectile pfb = null;
            try { if (gun && gun.GunItemSetting && gun.GunItemSetting.bulletPfb) pfb = gun.GunItemSetting.bulletPfb; } catch { }
            if (!pfb) pfb = Duckov.Utilities.GameplayDataSettings.Prefabs.DefaultBullet;
            if (!pfb) return;

            var proj = LevelManager.Instance.BulletPool.GetABullet(pfb);
            proj.transform.position = spawnPos;
            proj.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            proj.Init(ctx);

            PlayMuzzleFxAndShell(shooterId, weaponType, spawnPos, dir);
            TryPlayShootAnim(shooterId);
        }







        private void TryPlayShootAnim(string shooterId)
        {
            // 自己开火的广播会带自己的 shooterId，这里直接跳过，避免把动作套在本地自己或主机身上
            if (IsSelfId(shooterId)) return;

            if (!clientRemoteCharacters.TryGetValue(shooterId, out var shooterGo) || !shooterGo) return;

            var animCtrl = shooterGo.GetComponent<CharacterAnimationControl_MagicBlend>();
            if (animCtrl && animCtrl.animator)
            {
                animCtrl.OnAttack();
            }
        }



        private bool TryGetProjectilePrefab(int weaponTypeId, out Projectile pfb)
         => _projCacheByWeaponType.TryGetValue(weaponTypeId, out pfb);


        public void BroadcastReliable(NetDataWriter w)
        {
            if (!IsServer || netManager == null) return;
            netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        public void SendReliable(NetDataWriter w)
        {
            if (IsServer) netManager?.SendToAll(w, DeliveryMethod.ReliableOrdered);
            else connectedPeer?.Send(w, DeliveryMethod.ReliableOrdered);
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {

            // 统一：读取 1 字节的操作码（Op）
            if (reader.AvailableBytes <= 0) { reader.Recycle(); return; }
            var op = (Op)reader.GetByte();
            //  Debug.Log($"[RECV OP] {(byte)op}, avail={reader.AvailableBytes}");

            switch (op)
            {
                // ===== 主机 -> 客户端：下发全量玩家状态 =====
                case Op.PLAYER_STATUS_UPDATE:
                    if (!IsServer)
                    {
                        int playerCount = reader.GetInt();
                        clientPlayerStatuses.Clear();

                        for (int i = 0; i < playerCount; i++)
                        {
                            string endPoint = reader.GetString();
                            string playerName = reader.GetString();
                            int latency = reader.GetInt();
                            bool isInGame = reader.GetBool();
                            Vector3 position = reader.GetVector3();        
                            Quaternion rotation = reader.GetQuaternion();

                            string sceneId = reader.GetString();
                            string customFaceJson = reader.GetString();

                            int equipmentCount = reader.GetInt();
                            var equipmentList = new List<EquipmentSyncData>();
                            for (int j = 0; j < equipmentCount; j++)
                                equipmentList.Add(EquipmentSyncData.Deserialize(reader));

                            int weaponCount = reader.GetInt();
                            var weaponList = new List<WeaponSyncData>();
                            for (int j = 0; j < weaponCount; j++)
                                weaponList.Add(WeaponSyncData.Deserialize(reader));

                            if (IsSelfId(endPoint)) continue;

                            if (!clientPlayerStatuses.TryGetValue(endPoint, out var st))
                                st = clientPlayerStatuses[endPoint] = new PlayerStatus();

                            st.EndPoint = endPoint;
                            st.PlayerName = playerName;
                            st.Latency = latency;
                            st.IsInGame = isInGame;
                            st.LastIsInGame = isInGame;
                            st.Position = position;
                            st.Rotation = rotation;
                            if (!string.IsNullOrEmpty(customFaceJson))
                                st.CustomFaceJson = customFaceJson;
                            st.EquipmentList = equipmentList;
                            st.WeaponList = weaponList;

                            if (!string.IsNullOrEmpty(sceneId))
                            {
                                st.SceneId = sceneId;
                                _cliLastSceneIdByPlayer[endPoint] = sceneId; // 给 A 的兜底也喂一份
                            }

                            if (clientRemoteCharacters.TryGetValue(st.EndPoint, out var existing) && existing != null)
                                Client_ApplyFaceIfAvailable(st.EndPoint, existing, st.CustomFaceJson);

                            if (isInGame)
                            {
                                if (!clientRemoteCharacters.ContainsKey(endPoint) || clientRemoteCharacters[endPoint] == null)
                                {
                                    CreateRemoteCharacterForClient(endPoint, position, rotation, customFaceJson).Forget();
                                }
                                else
                                {
                                    var go = clientRemoteCharacters[endPoint];
                                    var ni = NetInterpUtil.Attach(go);
                                    ni?.Push(st.Position, st.Rotation);
                                }

                                foreach (var e in equipmentList) ApplyEquipmentUpdate_Client(endPoint, e.SlotHash, e.ItemId).Forget();
                                foreach (var w in weaponList) ApplyWeaponUpdate_Client(endPoint, w.SlotHash, w.ItemId).Forget();
                            }
                        }
                    }
                    break;

                // ===== 客户端 -> 主机：上报自身状态 =====
                case Op.CLIENT_STATUS_UPDATE:
                    if (IsServer)
                    {
                        HandleClientStatusUpdate(peer, reader);
                    }
                    break;

                // ===== 位置信息（量化版本）=====
                case Op.POSITION_UPDATE:
                    if (IsServer)
                    {
                        string endPointC = reader.GetString();
                        Vector3 posS = reader.GetV3cm();   // ← 原来是 GetVector3()
                        Vector3 dirS = reader.GetDir();
                        Quaternion rotS = Quaternion.LookRotation(dirS, Vector3.up);

                        HandlePositionUpdate_Q(peer, endPointC, posS, rotS);
                    }
                    else
                    {
                        string endPointS = reader.GetString();
                        Vector3 posS = reader.GetV3cm();   // ← 原来是 GetVector3()
                        Vector3 dirS = reader.GetDir();
                        Quaternion rotS = Quaternion.LookRotation(dirS, Vector3.up);

                        if (IsSelfId(endPointS)) break;

                        // 防御性：若包损坏，不推进插值也不拉起角色
                        if (float.IsNaN(posS.x) || float.IsNaN(posS.y) || float.IsNaN(posS.z) ||
                            float.IsInfinity(posS.x) || float.IsInfinity(posS.y) || float.IsInfinity(posS.z))
                            break;

                        if (!clientPlayerStatuses.TryGetValue(endPointS, out var st))
                            st = clientPlayerStatuses[endPointS] = new PlayerStatus { EndPoint = endPointS, IsInGame = true };

                        st.Position = posS;
                        st.Rotation = rotS;

                        if (clientRemoteCharacters.TryGetValue(endPointS, out var go) && go != null)
                        {
                            var ni = NetInterpUtil.Attach(go);
                            ni?.Push(st.Position, st.Rotation);   // 原有：位置与根旋转插值

                            var cmc = go.GetComponentInChildren<CharacterMainControl>(true);
                            if (cmc && cmc.modelRoot)
                            {
                                var e = st.Rotation.eulerAngles;
                                cmc.modelRoot.transform.rotation = Quaternion.Euler(0f, e.y, 0f);
                            }
                        }
                        else
                        {
                            CreateRemoteCharacterForClient(endPointS, posS, rotS, st.CustomFaceJson).Forget();
                        }
                    }
                    break;

                //动画
                case Op.ANIM_SYNC:
                    if (IsServer)
                    {
                        // 保持客户端 -> 主机
                        HandleClientAnimationStatus(peer, reader);
                    }
                    else
                    {
                        // 保持主机 -> 客户端（playerId）
                        string playerId = reader.GetString();
                        if (IsSelfId(playerId)) break;

                        float moveSpeed = reader.GetFloat();
                        float moveDirX = reader.GetFloat();
                        float moveDirY = reader.GetFloat();
                        bool isDashing = reader.GetBool();
                        bool isAttacking = reader.GetBool();
                        int handState = reader.GetInt();
                        bool gunReady = reader.GetBool();
                        int stateHash = reader.GetInt();
                        float normTime = reader.GetFloat();

                        if (clientRemoteCharacters.TryGetValue(playerId, out var obj) && obj != null)
                        {
                            var ai = AnimInterpUtil.Attach(obj);
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

                    }
                    break;

                // ===== 装备更新 =====
                case Op.EQUIPMENT_UPDATE:
                    if (IsServer)
                    {
                        HandleEquipmentUpdate(peer, reader);
                    }
                    else
                    {
                        string endPoint = reader.GetString();
                        if (IsSelfId(endPoint)) break;
                        int slotHash = reader.GetInt();
                        string itemId = reader.GetString();
                        ApplyEquipmentUpdate_Client(endPoint, slotHash, itemId).Forget();
                    }
                    break;

                // ===== 武器更新 =====
                case Op.PLAYERWEAPON_UPDATE:
                    if (IsServer)
                    {
                        HandleWeaponUpdate(peer, reader);
                    }
                    else
                    {
                        string endPoint = reader.GetString();
                        if (IsSelfId(endPoint)) break;
                        int slotHash = reader.GetInt();
                        string itemId = reader.GetString();
                        ApplyWeaponUpdate_Client(endPoint, slotHash, itemId).Forget();
                    }
                    break;

                case Op.FIRE_REQUEST:
                    if (IsServer)
                    {
                        HandleFireRequest(peer, reader);
                    }
                    break;

                case Op.FIRE_EVENT:
                    if (!IsServer)
                    {
                        //Debug.Log("[RECV FIRE_EVENT] opcode path");
                        HandleFireEvent(reader);
                    }
                    break;

                default:
                    // 有未知 opcode 时给出警告，便于排查（比如双端没一起更新）
                    Debug.LogWarning($"Unknown opcode: {(byte)op}");
                    break;

                case Op.GRENADE_THROW_REQUEST:
                    if (IsServer) HandleGrenadeThrowRequest(peer, reader);
                    break;
                case Op.GRENADE_SPAWN:
                    if (!IsServer) HandleGrenadeSpawn(reader);
                    break;
                case Op.GRENADE_EXPLODE:
                    if (!IsServer) HandleGrenadeExplode(reader);
                    break;

                //case Op.DISCOVER_REQUEST:
                //    if (IsServer) HandleDiscoverRequest(peer, reader);
                //    break;
                //case Op.DISCOVER_RESPONSE:
                //    if (!IsServer) HandleDiscoverResponse(peer, reader);
                //    break;
                case Op.ITEM_DROP_REQUEST:
                    if (IsServer) HandleItemDropRequest(peer, reader);
                    break;

                case Op.ITEM_SPAWN:
                    if (!IsServer) HandleItemSpawn(reader);
                    break;
                case Op.ITEM_PICKUP_REQUEST:
                    if (IsServer) HandleItemPickupRequest(peer, reader);
                    break;
                case Op.ITEM_DESPAWN:
                    if (!IsServer) HandleItemDespawn(reader);
                    break;

                case Op.MELEE_ATTACK_REQUEST:
                    if (IsServer) HandleMeleeAttackRequest(peer, reader);
                    break;
                case Op.MELEE_ATTACK_SWING:
                    {
                        if (!IsServer)
                        {
                            string shooter = reader.GetString();
                            float delay = reader.GetFloat(); 

                            //先找玩家远端
                            if (!IsSelfId(shooter) && clientRemoteCharacters.TryGetValue(shooter, out var who) && who)
                            {
                                var anim = who.GetComponentInChildren<CharacterAnimationControl_MagicBlend>(true);
                                if (anim != null) anim.OnAttack();

                                var cmc = who.GetComponent<CharacterMainControl>();
                                var model = cmc ? cmc.characterModel : null;
                                if (model) 鸭科夫联机Mod.MeleeFx.SpawnSlashFx(model);
                            }
                            //兼容 AI:xxx
                            else if (shooter.StartsWith("AI:"))
                            {
                                if (int.TryParse(shooter.Substring(3), out var aiId) && aiById.TryGetValue(aiId, out var cmc) && cmc)
                                {
                                    var anim = cmc.GetComponentInChildren<CharacterAnimationControl_MagicBlend>(true);
                                    if (anim != null) anim.OnAttack();

                                    var model = cmc.characterModel;
                                    if (model) 鸭科夫联机Mod.MeleeFx.SpawnSlashFx(model);
                                }
                            }
                        }
                        break;
                    }

                case Op.MELEE_HIT_REPORT:
                    if (IsServer) HandleMeleeHitReport(peer, reader);
                    break;

                case Op.ENV_HURT_REQUEST:
                    if (IsServer) Server_HandleEnvHurtRequest(peer, reader);
                    break;
                case Op.ENV_HURT_EVENT:
                    if (!IsServer) Client_ApplyDestructibleHurt(reader);
                    break;
                case Op.ENV_DEAD_EVENT:
                    if (!IsServer) Client_ApplyDestructibleDead(reader);
                    break;

                case Op.PLAYER_HEALTH_REPORT:
                    {
                        if (IsServer)
                        {
                            float max = reader.GetFloat();
                            float cur = reader.GetFloat();
                            if (max <= 0f)
                            {
                                _srvPendingHp[peer] = (max, cur);
                                break;
                            }
                            if (remoteCharacters != null && remoteCharacters.TryGetValue(peer, out var go) && go)
                            {
                                // 主机本地先写实自己能立刻看到
                                ApplyHealthAndEnsureBar(go, max, cur);

                                // 再用统一广播流程，发给本人 + 其他客户端
                                var h = go.GetComponentInChildren<Health>(true);
                                if (h) Server_OnHealthChanged(peer, h);
                            }
                            else
                            {
                                //远端克隆还没创建缓存起来，等钩到 Health 后应用
                                _srvPendingHp[peer] = (max, cur);
                            }
                        }
                        break;
                    }


                case Op.AUTH_HEALTH_SELF:
                    {
                        float max = reader.GetFloat();
                        float cur = reader.GetFloat();

                        if (max <= 0f)
                        {
                            _cliSelfHpMax = max; _cliSelfHpCur = cur;
                            _cliSelfHpPending = true;
                            break;
                        }

                        // --- 防回弹：受击窗口内不接受“比本地更高”的回显 ---
                        bool shouldApply = true;
                        try
                        {
                            var main = CharacterMainControl.Main;
                            var selfH = main ? main.Health : null;
                            if (selfH)
                            {
                                float localCur = selfH.CurrentHealth;
                                // 仅在“刚受击的短时间窗”里做保护；平时允许正常回显（例如治疗）
                                if (Time.time - _cliLastSelfHurtAt <= SELF_ACCEPT_WINDOW)
                                {
                                    // 如果回显值会让血量“变多”（典型回弹），判定为陈旧 echo 丢弃
                                    if (cur > localCur + 0.0001f)
                                    {

                                       UnityEngine.Debug.Log($"[HP][SelfEcho] drop stale echo in window: local={localCur:F3} srv={cur:F3}");

                                        shouldApply = false;
                                    }
                                }
                            }
                        }
                        catch { }

                        _cliApplyingSelfSnap = true;
                        _cliEchoMuteUntil = Time.time + SELF_MUTE_SEC;
                        try
                        {
                            if (shouldApply)
                            {
                                if (_cliSelfHpPending)
                                {
                                    _cliSelfHpMax = max; _cliSelfHpCur = cur;
                                    Client_ApplyPendingSelfIfReady();
                                }
                                else
                                {
                                    var main = CharacterMainControl.Main;
                                    var go = main ? main.gameObject : null;
                                    if (go)
                                    {
                                        var h = main.Health;
                                        var cmc = main;
                                        if (h)
                                        {
                                            try { h.autoInit = false; } catch { }
                                            BindHealthToCharacter(h, cmc);
                                            ForceSetHealth(h, max, cur, ensureBar: true);
                                        }
                                    }
                                    _cliSelfHpPending = false;
                                }
                            }
                            else
                            {
                                // 丢弃这帧自回显，不改本地血量
                            }
                        }
                        finally
                        {
                            _cliApplyingSelfSnap = false;
                        }
                        break;
                    }

                case Op.AUTH_HEALTH_REMOTE:
                    {
                        if (!IsServer)
                        {
                            string playerId = reader.GetString();
                            float max = reader.GetFloat();
                            float cur = reader.GetFloat();

                            // 无效快照直接挂起，避免把 0/0 覆盖到血条
                            if (max <= 0f)
                            {
                                _cliPendingRemoteHp[playerId] = (max, cur);
                                break;
                            }

                            if (clientRemoteCharacters != null && clientRemoteCharacters.TryGetValue(playerId, out var go) && go)
                                ApplyHealthAndEnsureBar(go, max, cur);
                            else
                                _cliPendingRemoteHp[playerId] = (max, cur);
                        }
                        break;
                    }

                case Op.PLAYER_BUFF_SELF_APPLY:
                    if (!IsServer) HandlePlayerBuffSelfApply(reader);
                    break;
                case Op.HOST_BUFF_PROXY_APPLY:
                    if (!IsServer) HandleBuffProxyApply(reader);
                    break;


                case Op.SCENE_VOTE_START:
                    {
                        if (!IsServer)
                        {
                            Client_OnSceneVoteStart(reader);
                            // 观战中收到“开始投票”，记一个“投票结束就结算”的意图
                            if (_spectatorActive) _spectatorEndOnVotePending = true;
                        }
                        break;
                    }

                case Op.SCENE_VOTE_REQ:
                    {
                        if (IsServer)
                        {
                            string targetId = reader.GetString();
                            byte flags = reader.GetByte();
                            bool hasCurtain, useLoc, notifyEvac, saveToFile;
                            UnpackFlags(flags, out hasCurtain, out useLoc, out notifyEvac, out saveToFile);

                            string curtainGuid = null;
                            if (hasCurtain) TryGetString(reader, out curtainGuid);
                            if (!TryGetString(reader, out var locName)) locName = string.Empty;

                            // ★ 主机若正处于观战，记下“投票结束就结算”的意图
                            if (_spectatorActive) _spectatorEndOnVotePending = true;

                            Host_BeginSceneVote_Simple(targetId, curtainGuid, notifyEvac, saveToFile, useLoc, locName);
                        }
                        break;
                    }



                case Op.SCENE_READY_SET:
                    {
                        if (IsServer)
                        {
                            bool ready = reader.GetBool();
                            Server_OnSceneReadySet(peer, ready);
                        }
                        else
                        {
                            string pid = reader.GetString();
                            bool rdy = reader.GetBool();

                            if (!sceneReady.ContainsKey(pid) && sceneParticipantIds.Contains(pid))
                                sceneReady[pid] = false;

                            if (sceneReady.ContainsKey(pid))
                            {
                                sceneReady[pid] = rdy;
                                Debug.Log($"[SCENE] READY_SET -> {pid} = {rdy}");
                            }
                            else
                            {
                                Debug.LogWarning($"[SCENE] READY_SET for unknown pid '{pid}'. participants=[{string.Join(",", sceneParticipantIds)}]");
                            }
                        }
                        break;
                    }

                case Op.SCENE_BEGIN_LOAD:
                    {
                        if (!IsServer)
                        {
                            // 观战玩家：投票结束时直接弹死亡结算，不参与接下来的本地切图
                            if (_spectatorActive && _spectatorEndOnVotePending)
                            {
                                _spectatorEndOnVotePending = false;
                                sceneVoteActive = false;
                                sceneReady.Clear();
                                localReady = false;

                                EndSpectatorAndShowClosure(); // 直接用你现成的方法弹结算
                                break; // 不再调用 Client_OnBeginSceneLoad(reader)
                            }

                            // 普通玩家照常走
                            Client_OnBeginSceneLoad(reader);
                        }
                        break;
                    }

                case Op.SCENE_CANCEL:
                    {
                        sceneVoteActive = false;
                        sceneReady.Clear();
                        localReady = false;

                        if (_spectatorActive && _spectatorEndOnVotePending)
                        {

                            _spectatorEndOnVotePending = false;
                            EndSpectatorAndShowClosure();
                        }
                        break;
                    }


                case Op.SCENE_READY:
                    {
                        string id = reader.GetString();   // 发送者 id（EndPoint）
                        string sid = reader.GetString();  // SceneId（string）
                        Vector3 pos = reader.GetVector3(); // 初始位置
                        Quaternion rot = reader.GetQuaternion();
                        string face = reader.GetString();

                        if (IsServer)
                        {
                            Server_HandleSceneReady(peer, id, sid, pos, rot, face);
                        }
                        // 客户端若收到这条（主机广播），实际创建工作由 REMOTE_CREATE 完成，这里不处理
                        break;
                    }

                case Op.ENV_SYNC_REQUEST:
                    if (IsServer) Server_BroadcastEnvSync(peer);
                    break;

                case Op.ENV_SYNC_STATE:
                    {
                        // 客户端应用
                        if (!IsServer)
                        {
                            long day = reader.GetLong();
                            double sec = reader.GetDouble();
                            float scale = reader.GetFloat();
                            int seed = reader.GetInt();
                            bool forceW = reader.GetBool();
                            int forceWVal = reader.GetInt();
                            int curWeather = reader.GetInt();
                            byte stormLv = reader.GetByte();

                            int lootCount = 0;
                            try { lootCount = reader.GetInt(); } catch { lootCount = 0; }
                            var vis = new Dictionary<int, bool>(lootCount);
                            for (int i = 0; i < lootCount; ++i)
                            {
                                int k = 0; bool on = false;
                                try { k = reader.GetInt(); } catch { }
                                try { on = reader.GetBool(); } catch { }
                                vis[k] = on;
                            }
                            Client_ApplyLootVisibility(vis);

                            // 再读门快照（如果主机这次没带就是 0）
                            int doorCount = 0;
                            try { doorCount = reader.GetInt(); } catch { doorCount = 0; }
                            for (int i = 0; i < doorCount; ++i)
                            {
                                int dk = 0; bool cl = false;
                                try { dk = reader.GetInt(); } catch { }
                                try { cl = reader.GetBool(); } catch { }
                                Client_ApplyDoorState(dk, cl);
                            }

                            int deadCount = 0;
                            try { deadCount = reader.GetInt(); } catch { deadCount = 0; }
                            for (int i = 0; i < deadCount; ++i)
                            {
                                uint did = 0;
                                try { did = reader.GetUInt(); } catch { }
                                if (did != 0) Client_ApplyDestructibleDead_Snapshot(did);
                            }

                            Client_ApplyEnvSync(day, sec, scale, seed, forceW, forceWVal, curWeather, stormLv);
                        }
                        break;
                    }


                case Op.LOOT_REQ_OPEN:
                    {
                        if (IsServer) Server_HandleLootOpenRequest(peer, reader);
                        break;
                    }



                case Op.LOOT_STATE:
                    {
                        if (IsServer) break;
                        Client_ApplyLootboxState(reader);

                        break;
                    }
                case Op.LOOT_REQ_PUT:
                    {
                        if (!IsServer) break;
                        Server_HandleLootPutRequest(peer, reader);
                        break;
                    }
                case Op.LOOT_REQ_TAKE:
                    {
                        if (!IsServer) break;
                        Server_HandleLootTakeRequest(peer, reader);
                        break;
                    }
                case Op.LOOT_PUT_OK:
                    {
                        if (IsServer) break;
                        Client_OnLootPutOk(reader);
                        break;
                    }
                case Op.LOOT_TAKE_OK:
                    {
                        if (IsServer) break;
                        Client_OnLootTakeOk(reader);
                        break;
                    }

                case Op.LOOT_DENY:
                    {
                        if (IsServer) break;
                        string reason = reader.GetString();
                        Debug.LogWarning($"[LOOT] 请求被拒绝：{reason}");

                        // no_inv 不要立刻重试，避免请求风暴
                        if (reason == "no_inv")
                            break;

                        // 其它可恢复类错误（如 rm_fail/bad_snapshot）再温和地刷新一次
                        var lv = Duckov.UI.LootView.Instance;
                        var inv = lv ? lv.TargetInventory : null;
                        if (inv) Client_RequestLootState(inv);
                        break;
                    }



                case Op.AI_SEED_SNAPSHOT:
                    {
                        if (!IsServer) HandleAiSeedSnapshot(reader);
                        break;
                    }
                case Op.AI_LOADOUT_SNAPSHOT:
                    {
                        byte ver = reader.GetByte();
                        int aiId = reader.GetInt();

                        int ne = reader.GetInt();
                        var equips = new List<(int slot, int tid)>(ne);
                        for (int i = 0; i < ne; ++i)
                        {
                            int sh = reader.GetInt();
                            int tid = reader.GetInt();
                            equips.Add((sh, tid));
                        }

                        int nw = reader.GetInt();
                        var weapons = new List<(int slot, int tid)>(nw);
                        for (int i = 0; i < nw; ++i)
                        {
                            int sh = reader.GetInt();
                            int tid = reader.GetInt();
                            weapons.Add((sh, tid));
                        }

                        bool hasFace = reader.GetBool();
                        string faceJson = hasFace ? reader.GetString() : null;

                        bool hasModelName = reader.GetBool();
                        string modelName = hasModelName ? reader.GetString() : null;

                        int iconType = reader.GetInt();

                        bool showName = false;
                        if (ver >= 4) showName = reader.GetBool();

                        string displayName = null;
                        if (ver >= 5)
                        {
                            bool hasName = reader.GetBool();
                            if (hasName) displayName = reader.GetString();
                        }

                        if (IsServer) break;

                        if (LogAiLoadoutDebug)
                            Debug.Log($"[AI-RECV] ver={ver} aiId={aiId} model='{modelName}' icon={iconType} showName={showName} faceLen={(faceJson != null ? faceJson.Length : 0)}");

                        if (aiById.TryGetValue(aiId, out var cmc) && cmc)
                            Client_ApplyAiLoadout(aiId, equips, weapons, faceJson, modelName, iconType, showName, displayName).Forget();
                        else
                            pendingAiLoadouts[aiId] = (equips, weapons, faceJson, modelName, iconType, showName, displayName);

                        break;
                    }

                case Op.AI_TRANSFORM_SNAPSHOT:
                    {
                        if (IsServer) break; 
                        int n = reader.GetInt();

                        if (!_aiSceneReady)
                        {
                            for (int i = 0; i < n; ++i)
                            {
                                int aiId = reader.GetInt();
                                Vector3 p = reader.GetV3cm();
                                Vector3 f = reader.GetDir();
                                if (_pendingAiTrans.Count < 512) _pendingAiTrans.Enqueue((aiId, p, f)); // 防“Mr.Sans”炸锅
                            }
                            break;
                        }

                        for (int i = 0; i < n; i++)
                        {
                            int aiId = reader.GetInt();
                            Vector3 p = reader.GetV3cm();
                            Vector3 f = reader.GetDir();
                            ApplyAiTransform(aiId, p, f); // 抽成函数复用下面冲队列逻辑
                        }
                        break;
                    }

                case Op.AI_ANIM_SNAPSHOT:
                    {
                        if (!IsServer)
                        {
                            int n = reader.GetInt();
                            for (int i = 0; i < n; ++i)
                            {
                                int id = reader.GetInt();
                                var st = new AiAnimState
                                {
                                    speed = reader.GetFloat(),
                                    dirX = reader.GetFloat(),
                                    dirY = reader.GetFloat(),
                                    hand = reader.GetInt(),
                                    gunReady = reader.GetBool(),
                                    dashing = reader.GetBool(),
                                };
                                if (!Client_ApplyAiAnim(id, st))
                                    _pendingAiAnims[id] = st;
                            }
                        }
                        break;
                    }

                case Op.AI_ATTACK_SWING:
                    {
                        if (!IsServer)
                        {
                            int id = reader.GetInt();
                            if (aiById.TryGetValue(id, out var cmc) && cmc)
                            {
                                var anim = cmc.GetComponent<CharacterAnimationControl_MagicBlend>();
                                if (anim != null) anim.OnAttack();
                                var model = cmc.characterModel;
                                if (model) MeleeFx.SpawnSlashFx(model);
                            }
                        }
                        break;
                    }

                case Op.AI_HEALTH_SYNC:
                    {
                        int id = reader.GetInt();

                        float max = 0f, cur = 0f;
                        if (reader.AvailableBytes >= 8)
                        {   
                            max = reader.GetFloat();
                            cur = reader.GetFloat();
                        }
                        else
                        {                            
                            cur = reader.GetFloat();
                        }

                        Client_ApplyAiHealth(id, max, cur);
                        break;
                    }


                // --- 客户端：读取 aiId，并把它传下去 ---
                case Op.DEAD_LOOT_SPAWN:
                    {
                        int scene = reader.GetInt();
                        int aiId = reader.GetInt();
                        int lootUid = reader.GetInt();                  
                        Vector3 pos = reader.GetV3cm();
                        Quaternion rot = reader.GetQuaternion();
                        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex != scene) break;

                        SpawnDeadLootboxAt(aiId, lootUid, pos, rot);    
                        break;
                    }

             


                case Op.AI_NAME_ICON:
                    {
                        if (IsServer) break;

                        int aiId = reader.GetInt();
                        int iconType = reader.GetInt();
                        bool showName = reader.GetBool();
                        string displayName = null;
                        bool hasName = reader.GetBool();
                        if (hasName) displayName = reader.GetString();

                        if (aiById.TryGetValue(aiId, out var cmc) && cmc)
                        {
                            RefreshNameIconWithRetries(cmc, iconType, showName, displayName).Forget();
                        }
                        else
                        {
                            Debug.LogWarning($"[AI_icon_Name 10s] cmc is null!");
                        }
                        // 若当前还没绑定上 cmc，就先忽略；每 10s 会兜底播一遍
                        break;
                    }

                case Op.PLAYER_DEAD_TREE:
                    {
                        if (!IsServer) break;
                        Vector3 pos = reader.GetV3cm();
                        Quaternion rot = reader.GetQuaternion();

                        var snap = ReadItemSnapshot(reader);        
                        var tmpRoot = BuildItemFromSnapshot(snap);  
                        if (!tmpRoot) { Debug.LogWarning("[LOOT] PLAYER_DEAD_TREE BuildItemFromSnapshot failed."); break; }

                        var deadPfb = ResolveDeadLootPrefabOnServer();
                        var box = InteractableLootbox.CreateFromItem(tmpRoot, pos + Vector3.up * 0.10f, rot, true, deadPfb, false);
                        if (box) Server_OnDeadLootboxSpawned(box, null);   // 用新版重载：会发 lootUid + aiId + 随后 LOOT_STATE

                        if (remoteCharacters.TryGetValue(peer, out var proxy) && proxy)
                        {
                            UnityEngine.Object.Destroy(proxy);
                            remoteCharacters.Remove(peer);
                        }

                        // B) 广播给所有客户端：这个玩家的远程代理需要销毁
                        if (playerStatuses.TryGetValue(peer, out var st) && !string.IsNullOrEmpty(st.EndPoint))
                        {
                            var w2 = writer; w2.Reset();
                            w2.Put((byte)Op.REMOTE_DESPAWN);
                            w2.Put(st.EndPoint);                 // 客户端用 EndPoint 当 key
                            netManager.SendToAll(w2, DeliveryMethod.ReliableOrdered);
                        }


                        if (tmpRoot && tmpRoot.gameObject) UnityEngine.Object.Destroy(tmpRoot.gameObject);
                        break;
                    }

                case Op.LOOT_REQ_SPLIT:
                    {
                        if (!IsServer) break;
                        Server_HandleLootSplitRequest(peer, reader);
                        break;
                    }

                case Op.REMOTE_DESPAWN:
                    {
                        if (IsServer) break;                 // 只客户端处理
                        string id = reader.GetString();
                        if (clientRemoteCharacters.TryGetValue(id, out var go) && go)
                            UnityEngine.Object.Destroy(go);
                        clientRemoteCharacters.Remove(id);
                        break;
                    }

                case Op.AI_SEED_PATCH:
                    HandleAiSeedPatch(reader);
                    break;

                case Op.DOOR_REQ_SET:
                    {
                        if (IsServer) Server_HandleDoorSetRequest(peer, reader);
                        break;
                    }
                case Op.DOOR_STATE:
                    {
                        if (!IsServer)
                        {
                            int k = reader.GetInt();
                            bool cl = reader.GetBool();
                            Client_ApplyDoorState(k, cl);
                        }
                        break;
                    }

                case Op.LOOT_REQ_SLOT_UNPLUG:
                    {
                        if (IsServer) Server_HandleLootSlotUnplugRequest(peer, reader);
                        break;
                    }
                case Op.LOOT_REQ_SLOT_PLUG:
                    {
                        if (IsServer) Server_HandleLootSlotPlugRequest(peer, reader);
                        break;
                    }


                case Op.SCENE_GATE_READY:
                    {
                        if (IsServer)
                        {
                            string pid = reader.GetString();
                            string sid = reader.GetString();

                            // 若主机还没确定 gate 的 sid，就用第一次 READY 的 sid
                            if (string.IsNullOrEmpty(_srvGateSid))
                                _srvGateSid = sid;

                            if (sid == _srvGateSid)
                            {
                                _srvGateReadyPids.Add(pid);

                            }
                        }
                        break;
                    }

                case Op.SCENE_GATE_RELEASE:
                    {
                        if (!IsServer)
                        {
                            string sid = reader.GetString();
                            // 允许首次对齐或服务端/客户端估算不一致的情况
                            if (string.IsNullOrEmpty(_cliGateSid) || sid == _cliGateSid)
                            {
                                _cliGateSid = sid;
                                _cliSceneGateReleased = true;
                                Client_ReportSelfHealth_IfReadyOnce();
                            }
                            else
                            {
                                Debug.LogWarning($"[GATE] release sid mismatch: srv={sid}, cli={_cliGateSid} — accepting");
                                _cliGateSid = sid;                // 对齐后仍放行
                                _cliSceneGateReleased = true;
                                Client_ReportSelfHealth_IfReadyOnce();
                            }
                        }
                        break;
                    }


                case Op.PLAYER_HURT_EVENT:
                    if (!IsServer) Client_ApplySelfHurtFromServer(reader);
                    break;





            }

            reader.Recycle();
        }



        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Debug.Log($"断开连接: {peer.EndPoint}, 原因: {disconnectInfo.Reason}");
            if (!IsServer)
            {
                status = Main.Localization.Get("status_connection_closed");
                isConnecting = false;
            }
            if (connectedPeer == peer) connectedPeer = null;

            if (playerStatuses.ContainsKey(peer))
            {
                var _st = playerStatuses[peer];
                if (_st != null && !string.IsNullOrEmpty(_st.EndPoint))
                    _cliLastSceneIdByPlayer.Remove(_st.EndPoint);
                playerStatuses.Remove(peer);
            }
            if (remoteCharacters.ContainsKey(peer) && remoteCharacters[peer] != null)
            {
                Destroy(remoteCharacters[peer]);
                remoteCharacters.Remove(peer);
            }



        }

        public void OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError socketError)
        {
            Debug.LogError($"网络错误: {socketError} 来自 {endPoint}");
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            if (playerStatuses.ContainsKey(peer))
                playerStatuses[peer].Latency = latency;
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            string msg = reader.GetString();

            if (IsServer && msg == "DISCOVER_REQUEST")
            {
                writer.Reset();
                writer.Put("DISCOVER_RESPONSE");
                netManager.SendUnconnectedMessage(writer, remoteEndPoint);
            }
            else if (!IsServer && msg == "DISCOVER_RESPONSE")
            {
                string hostInfo = remoteEndPoint.Address + ":" + port;
                if (!hostSet.Contains(hostInfo))
                {
                    hostSet.Add(hostInfo);
                    hostList.Add(hostInfo);
                    Debug.Log("发现主机: " + hostInfo);
                }
            }
        }

        private void SendBroadcastDiscovery()
        {
            if (IsServer) return;
            writer.Reset();
            writer.Put("DISCOVER_REQUEST");
            netManager.SendUnconnectedMessage(writer, "255.255.255.255", port);
        }

        private void ConnectToHost(string ip, int port)
        {
            // 基础校验
            if (string.IsNullOrWhiteSpace(ip))
            {
                status = Main.Localization.Get("status_ip_empty");
                isConnecting = false;
                return;
            }
            if (port <= 0 || port > 65535)
            {
                status = Main.Localization.Get("status_port_invalid");
                isConnecting = false;
                return;
            }

            if (IsServer)
            {
                Debug.LogWarning("服务器模式不能主动连接其他主机");
                return;
            }
            if (isConnecting)
            {
                Debug.LogWarning("正在连接中.");
                return;
            }

            //如未启动或仍在主机模式，则切到“客户端网络”
            if (netManager == null || !netManager.IsRunning || IsServer || !networkStarted)
            {
                try
                {
                    StartNetwork(false); // 启动/切换到客户端模式
                }
                catch (Exception e)
                {
                    Debug.LogError($"启动客户端网络失败：{e}");
                    status = Main.Localization.Get("status_client_network_failed");
                    isConnecting = false;
                    return;
                }
            }

            // 二次确认
            if (netManager == null || !netManager.IsRunning)
            {
                status = Main.Localization.Get("status_client_not_started");
                isConnecting = false;
                return;
            }

            try
            {
                status = Main.Localization.GetFormatted("status_connecting", ip, port);
                isConnecting = true;

                // 若已有连接，先断开（以免残留状态）
                try { connectedPeer?.Disconnect(); } catch { }
                connectedPeer = null;

                if (writer == null) writer = new LiteNetLib.Utils.NetDataWriter();

                writer.Reset();
                writer.Put("gameKey");
                netManager.Connect(ip, port, writer);
            }
            catch (Exception ex)
            {
                Debug.LogError($"连接到主机失败: {ex}");
                status = Main.Localization.Get("status_connection_failed");
                isConnecting = false;
                connectedPeer = null;
            }
        }

        private void SendClientStatusUpdate()
        {
            if (IsServer || connectedPeer == null) return;

            localPlayerStatus.CustomFaceJson = LoadLocalCustomFaceJson();
            var equipmentList = GetLocalEquipment();
            var weaponList = GetLocalWeapons();

            writer.Reset();
            writer.Put((byte)Op.CLIENT_STATUS_UPDATE);     // opcode
            writer.Put(localPlayerStatus.EndPoint);
            writer.Put(localPlayerStatus.PlayerName);
            writer.Put(localPlayerStatus.IsInGame);
            writer.PutVector3(localPlayerStatus.Position); 
            writer.PutQuaternion(localPlayerStatus.Rotation);

            writer.Put(localPlayerStatus?.SceneId ?? string.Empty);

            writer.Put(localPlayerStatus.CustomFaceJson ?? "");

            writer.Put(equipmentList.Count);
            foreach (var e in equipmentList) e.Serialize(writer);

            writer.Put(weaponList.Count);
            foreach (var w in weaponList) w.Serialize(writer);

            connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        }


        private void SendPlayerStatusUpdate()
        {
            if (!IsServer) return;

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

                var equipmentList = st == localPlayerStatus ? GetLocalEquipment() : (st.EquipmentList ?? new List<EquipmentSyncData>());
                writer.Put(equipmentList.Count);
                foreach (var e in equipmentList) e.Serialize(writer);

                var weaponList = st == localPlayerStatus ? GetLocalWeapons() : (st.WeaponList ?? new List<WeaponSyncData>());
                writer.Put(weaponList.Count);
                foreach (var w in weaponList) w.Serialize(writer);
            }

            netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
        }


        private void SendPositionUpdate()
        {
            if (localPlayerStatus == null || !networkStarted) return;

            var main = CharacterMainControl.Main;
            if (!main) return;

            var tr = main.transform;
            var mr = main.modelRoot ? main.modelRoot.transform : null;

            Vector3 pos = tr.position;
            Vector3 fwd = mr ? mr.forward : tr.forward;
            if (fwd.sqrMagnitude < 1e-12f) fwd = Vector3.forward;
         

            writer.Reset();
            writer.Put((byte)Op.POSITION_UPDATE);
            writer.Put(localPlayerStatus.EndPoint);

            // 统一：量化坐标 + 方向
            NetPack.PutV3cm(writer, pos);
            NetPack.PutDir(writer, fwd);

            if (IsServer) netManager.SendToAll(writer, DeliveryMethod.Unreliable);
            else connectedPeer?.Send(writer, DeliveryMethod.Unreliable);
        }



        private List<EquipmentSyncData> GetLocalEquipment()
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
                    Debug.LogError($"获取槽位 {slotNames[i]} 时发生错误: {ex.Message}");
                }
            }

            return equipmentList;
        }

        private List<WeaponSyncData> GetLocalWeapons()
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
                Debug.LogError($"获取本地武器数据时发生错误: {ex.Message}");
            }

            return weaponList;
        }

        void OnGUI()
        {
            if (showUI)
            {
                mainWindowRect = GUI.Window(94120, mainWindowRect, DrawMainWindow, Main.Localization.Get("ui_main_window_title"));

                if (showPlayerStatusWindow)
                {
                    playerStatusWindowRect = GUI.Window(94121, playerStatusWindowRect, DrawPlayerStatusWindow, Main.Localization.Get("ui_player_status_title"));
                }
            }

            if (sceneVoteActive)
            {
                float h = 220f;
                var area = new Rect(10, Screen.height * 0.5f - h * 0.5f, 320, h);
                GUILayout.BeginArea(area, GUI.skin.box);
                GUILayout.Label($"{Main.Localization.Get("ui_scene_vote_ready")}  [{SceneInfoCollection.GetSceneInfo(sceneTargetId).DisplayName}]");
                GUILayout.Label(Main.Localization.GetFormatted("ui_ready_status", readyKey, localReady ? Main.Localization.Get("ui_ready") : Main.Localization.Get("ui_not_ready")));

                GUILayout.Space(8);
                GUILayout.Label(Main.Localization.Get("ui_player_ready_status"));
                foreach (var pid in sceneParticipantIds)
                {
                    bool r = false; sceneReady.TryGetValue(pid, out r);
                    GUILayout.Label($"• {pid}  —— {(r ? Main.Localization.Get("ui_ready_checkmark") : Main.Localization.Get("ui_not_ready_hourglass"))}");
                }
                GUILayout.EndArea();
            }

            if (_spectatorActive)
            {
                var style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.LowerCenter,
                    fontSize = 18
                };
                style.normal.textColor = Color.white;

                //string who = "";
                try
                {
                   // var cmc = (_spectateIdx >= 0 && _spectateIdx < _spectateList.Count) ? _spectateList[_spectateIdx] : null;
                   // who = cmc ? (cmc.name ?? "队友") : "队友";
                }
                catch { }

                GUI.Label(new Rect(0, Screen.height - 40, Screen.width, 30),
                    Main.Localization.Get("ui_spectator_mode"), style);
            }


        }

        private void DrawMainWindow(int windowID)
        {
            GUILayout.BeginVertical();
            string currentMode = IsServer ? Main.Localization.Get("ui_mode_server") : Main.Localization.Get("ui_mode_client");
            GUILayout.Label($"{Main.Localization.Get("ui_current_mode")}: {currentMode}");

            string switchMode = IsServer ? Main.Localization.Get("ui_mode_client") : Main.Localization.Get("ui_mode_server");
            if (GUILayout.Button(Main.Localization.GetFormatted("ui_switch_to_mode", switchMode)))
            {
                IsServer = !IsServer;
                StartNetwork(IsServer);
            }

            GUILayout.Space(10);

            if (!IsServer)
            {
                GUILayout.Label(Main.Localization.Get("ui_lan_host_list"));

                if (hostList.Count == 0)
                {
                    GUILayout.Label(Main.Localization.Get("ui_no_hosts_found"));
                }
                else
                {
                    foreach (var host in hostList)
                    {
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button(Main.Localization.Get("ui_button_connect"), GUILayout.Width(60)))
                        {
                            var parts = host.Split(':');
                            if (parts.Length == 2 && int.TryParse(parts[1], out int p))
                            {
                                if (netManager == null || !netManager.IsRunning || IsServer || !networkStarted)
                                {
                                    StartNetwork(false);
                                }

                                ConnectToHost(parts[0], p);
                            }
                        }
                        GUILayout.Label(host);
                        GUILayout.EndHorizontal();
                    }
                }

                GUILayout.Space(20);
                GUILayout.Label(Main.Localization.Get("ui_manual_connect"));
                GUILayout.BeginHorizontal();
                GUILayout.Label("IP:", GUILayout.Width(40));
                manualIP = GUILayout.TextField(manualIP, GUILayout.Width(150));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label(Main.Localization.Get("ui_port_label"), GUILayout.Width(40));
                manualPort = GUILayout.TextField(manualPort, GUILayout.Width(150));
                GUILayout.EndHorizontal();
                if (GUILayout.Button(Main.Localization.Get("ui_button_manual_connect")))
                {
                    if (int.TryParse(manualPort, out int p))
                    {
                        if (netManager == null || !netManager.IsRunning || IsServer || !networkStarted)
                        {
                            StartNetwork(false);
                        }

                        ConnectToHost(manualIP, p);
                    }
                    else
                    {
                        status = Main.Localization.Get("status_port_format_error");
                    }
                }

                GUILayout.Space(20);
                GUILayout.Label(Main.Localization.Get("ui_status_label") + " " + status);
            }
            else
            {
                GUILayout.Label(Main.Localization.GetFormatted("ui_server_port", port));
                GUILayout.Label(Main.Localization.GetFormatted("ui_connection_count", netManager?.ConnectedPeerList.Count ?? 0));
            }

            GUILayout.Space(10);
            showPlayerStatusWindow = GUILayout.Toggle(showPlayerStatusWindow, Main.Localization.GetFormatted("ui_toggle_player_status_window", toggleWindowKey));

            if (GUILayout.Button(Main.Localization.Get("ui_debug_print_lootboxes")))
            {
                foreach (var i in LevelManager.LootBoxInventories)
                {
                    try
                    {
                        Debug.Log($"Name {i.Value.name}" + $" DisplayNameKey {i.Value.DisplayNameKey}" + $" Key {i.Key}");
                    }
                    catch
                    {
                        continue;
                    }
                }

            }
            //if (GUILayout.Button("[Debug] 所有maplist"))
            //{
            //    const string keyword = "MapSelectionEntry";

            //    var trs = Object.FindObjectsByType<Transform>(
            //        FindObjectsInactive.Include, FindObjectsSortMode.None);

            //    var gos = trs
            //        .Select(t => t.gameObject)
            //        .Where(go => go.name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
            //        .ToList();

            //    foreach (var i in gos)
            //    {
            //        try
            //        {
            //            var map = i.GetComponentInChildren<MapSelectionEntry>();
            //            if (map != null)
            //            {
            //                Debug.Log($"BeaconIndex {map.BeaconIndex}" + $" SceneID {map.SceneID}" + $" name {map.name}");
            //            }
            //        }
            //        catch { continue; }
            //    }

            //}


            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private MapSelectionEntry GetMapSelectionEntrylist(string SceneID)
        {
            const string keyword = "MapSelectionEntry";

            var trs = Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            var gos = trs
                .Select(t => t.gameObject)
                .Where(go => go.name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            foreach(var i in gos)
            {
                try
                {
                    var map = i.GetComponentInChildren<MapSelectionEntry>();
                    if (map != null)
                    {
                        if(map.SceneID == SceneID)
                        {
                            return map;
                        }
                    }
                }
                catch { continue; }
            }
            return null;
        }

        private void DrawPlayerStatusWindow(int windowID)
        {
            if (GUI.Button(new Rect(playerStatusWindowRect.width - 25, 5, 20, 20), "×"))
            {
                showPlayerStatusWindow = false;
            }

            playerStatusScrollPos = GUILayout.BeginScrollView(playerStatusScrollPos, GUILayout.ExpandWidth(true));

            if (localPlayerStatus != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"ID: {localPlayerStatus.EndPoint}", GUILayout.Width(180));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{Main.Localization.Get("ui_player_name_label")} {localPlayerStatus.PlayerName}", GUILayout.Width(180));
                GUILayout.Label($"{Main.Localization.Get("ui_player_latency_label")} {localPlayerStatus.Latency}ms", GUILayout.Width(100));
                GUILayout.Label($"{Main.Localization.Get("ui_player_in_game_label")} {(localPlayerStatus.IsInGame ? Main.Localization.Get("ui_yes") : Main.Localization.Get("ui_no"))}");
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
            }

            if (IsServer)
            {
                foreach (var kvp in playerStatuses)
                {
                    var st = kvp.Value;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"ID: {st.EndPoint}", GUILayout.Width(180));
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{Main.Localization.Get("ui_player_name_label")} {st.PlayerName}", GUILayout.Width(180));
                    GUILayout.Label($"{Main.Localization.Get("ui_player_latency_label")} {st.Latency}ms", GUILayout.Width(100));
                    GUILayout.Label($"{Main.Localization.Get("ui_player_in_game_label")} {(st.IsInGame ? Main.Localization.Get("ui_yes") : Main.Localization.Get("ui_no"))}");
                    GUILayout.EndHorizontal();
                    GUILayout.Space(10);
                }
            }
            else
            {
                foreach (var kvp in clientPlayerStatuses)
                {
                    var st = kvp.Value;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"ID: {st.EndPoint}", GUILayout.Width(180));
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{Main.Localization.Get("ui_player_name_label")} {st.PlayerName}", GUILayout.Width(180));
                    GUILayout.Label($"{Main.Localization.Get("ui_player_latency_label")} {st.Latency}ms", GUILayout.Width(100));
                    GUILayout.Label($"{Main.Localization.Get("ui_player_in_game_label")} {(st.IsInGame ? Main.Localization.Get("ui_yes") : Main.Localization.Get("ui_no"))}");
                    GUILayout.EndHorizontal();
                    GUILayout.Space(10);
                }
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        void OnDestroy()
        {
            StopNetwork();
        }

        private static string CleanName(string n)
        {
            if (string.IsNullOrEmpty(n)) return string.Empty;
            if (n.EndsWith("(Clone)", StringComparison.Ordinal)) n = n.Substring(0, n.Length - "(Clone)".Length);
            return n.Trim();
        }
        private static string TypeNameOf(Grenade g)
        {
            return g ? g.GetType().FullName : string.Empty;
        }

        public void CacheGrenadePrefab(int typeId, Grenade prefab)
        {
            if (!prefab) return;
            prefabByTypeId[typeId] = prefab;
        }


        private bool TryResolvePrefab(int typeId, string _, string __, out Grenade prefab)
        {
            prefab = null;
            if (prefabByTypeId.TryGetValue(typeId, out var p) && p) { prefab = p; return true; }
            return false;
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
            connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        // 主机：处理请求
        private void HandleGrenadeThrowRequest(NetPeer peer, NetPacketReader r)
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
                prefab = await 鸭科夫联机Mod.COOPManager.GetGrenadePrefabByItemIdAsync(typeId);

            if (!prefab)
            {
                // 兜底：让客户端自己解析（仍读空字符串）
                uint fid = nextGrenadeId++;
                Server_BroadcastGrenadeSpawn(fid, typeId, string.Empty, string.Empty, start, vel,
                    _create, _shake, _dmg, _delayOnHit, _delay, _isMine, _mineRange);
                return;
            }

            CacheGrenadePrefab(typeId, prefab);

            // 读取服务器模板（含整包 DamageInfo）
            var tpl = await ReadGrenadeTemplateAsync(typeId);

            // 找到这位 peer 对应的服务器角色（owner）
            var fromChar = TryGetRemoteCharacterForPeer(peer);
            // 若还没有映射，为了验证也可临时用主机自己： if (fromChar == null) fromChar = CharacterMainControl.Main;
            var g = Instantiate(prefab, start, Quaternion.identity);
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
            g.Launch(start, vel, fromChar,true);
        }

        // 客户端：生成视觉手雷
        // 客户端：收到主机广播的手雷生成
        private void HandleGrenadeSpawn(NetPacketReader r)
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
                CacheGrenadePrefab(typeId, prefab);

                var g = Instantiate(prefab, start, Quaternion.identity);
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

        static void AddNetGrenadeTag(GameObject go, uint id)
        {
            if (!go) return;
            var tag = go.GetComponent<NetGrenadeTag>() ?? go.AddComponent<NetGrenadeTag>();
            tag.id = id;
        }


        // 客户端：只按 typeId 精确解析（Item → Skill_Grenade → grenadePfb）并生成
        private async Cysharp.Threading.Tasks.UniTask ResolveAndSpawnClientAsync(
            uint id, int typeId, Vector3 start, Vector3 vel,
            bool create, float shake, float dmg, bool delayOnHit, float delay,
            bool isMine, float mineRange)
        {
            var prefab = await 鸭科夫联机Mod.COOPManager.GetGrenadePrefabByItemIdAsync(typeId);
            if (!prefab)
            {
                UnityEngine.Debug.LogError($"[CLIENT] grenade prefab exact resolve failed: typeId={typeId}");
                return;
            }

            CacheGrenadePrefab(typeId, prefab);

            var g = Instantiate(prefab, start, Quaternion.identity);
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


        private void HandleGrenadeExplode(NetPacketReader r)
        {
            uint id = r.GetUInt();
            Vector3 pos = r.GetV3cm();
            float dmg = r.GetFloat();
            float shake = r.GetFloat();
            if (clientGrenades.TryGetValue(id, out var go) && go)
            {
                go.SendMessage("Explode", SendMessageOptions.DontRequireReceiver);
                Destroy(go, 0.1f);
                clientGrenades.Remove(id);
            }
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
            BroadcastReliable(writer);
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
            BroadcastReliable(writer);
        }
        private void Server_BroadcastGrenadeExplode(uint id, Grenade g, Vector3 pos)
        {
            writer.Reset();
            writer.Put((byte)Op.GRENADE_EXPLODE);
            writer.Put(id);
            writer.PutV3cm(pos);
            writer.Put(g.damageRange);
            writer.Put(g.explosionShakeStrength);
            BroadcastReliable(writer);
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

        private CharacterMainControl TryGetRemoteCharacterForPeer(NetPeer peer)
        {
            if (remoteCharacters.TryGetValue(peer, out var remoteObj) && remoteObj)
            {
                var cm = remoteObj.GetComponent<CharacterMainControl>().characterModel;
                if (cm != null) return cm.characterMainControl;
            }
            return null;
        }


        public void WriteItemSnapshot(NetDataWriter w, Item item)
        {
            w.Put(item.TypeID);
            w.Put(item.StackCount);
            w.Put(item.Durability);
            w.Put(item.DurabilityLoss);
            w.Put(item.Inspected);

            // Slots：只写“有内容”的槽
            var slots = item.Slots;
            if (slots != null && slots.list != null)
            {
                int filled = 0;
                foreach (var s in slots.list) if (s != null && s.Content != null) filled++;
                w.Put((ushort)filled);
                foreach (var s in slots.list)
                {
                    if (s == null || s.Content == null) continue;
                    w.Put(s.Key ?? string.Empty);
                    WriteItemSnapshot(w, s.Content);
                }
            }
            else
            {
                w.Put((ushort)0);
            }

            // Inventory：**只写非空**，不写任何占位
            var invItems = TryGetInventoryItems(item.Inventory);
            if (invItems != null)
            {
                var valid = new System.Collections.Generic.List<Item>(invItems.Count);
                foreach (var c in invItems) if (c != null) valid.Add(c);

                w.Put((ushort)valid.Count);
                foreach (var child in valid)
                    WriteItemSnapshot(w, child);
            }
            else
            {
                w.Put((ushort)0);
            }
        }



        // 读快照
        static ItemSnapshot ReadItemSnapshot(NetPacketReader r)
        {
            ItemSnapshot s;
            s.typeId = r.GetInt();
            s.stack = r.GetInt();
            s.durability = r.GetFloat();
            s.durabilityLoss = r.GetFloat();
            s.inspected = r.GetBool();
            s.slots = new List<(string, ItemSnapshot)>();
            s.inventory = new List<ItemSnapshot>();

            int slotsCount = r.GetUShort();
            for (int i = 0; i < slotsCount; i++)
            {
                string key = r.GetString();
                var child = ReadItemSnapshot(r);
                s.slots.Add((key, child));
            }
            int invCount = r.GetUShort();
            for (int i = 0; i < invCount; i++)
            {
                var child = ReadItemSnapshot(r);
                s.inventory.Add(child);
            }
            return s;
        }

        // 用快照构建实例（递归）
        static Item BuildItemFromSnapshot(ItemSnapshot s)
        {
            Item item = null;
            try
            {
                item = COOPManager.GetItemAsync(s.typeId).Result; 
            }
            catch (Exception e)
            {
                Debug.LogError($"[ITEM] 实例化失败 typeId={s.typeId}, err={e}");
                return null;
            }
            if (item == null) return null;
            ApplySnapshotToItem(item, s);
            return item;
        }


        // 把快照写回到 item（递归挂接）
        static void ApplySnapshotToItem(Item item, ItemSnapshot s)
        {
            try
            {
                // 仅可堆叠才设置数量，避免“不可堆叠，无法设置数量”
                if (item.Stackable)
                {
                    int target = s.stack;
                    if (target < 1) target = 1;
                    try { target = Mathf.Clamp(target, 1, item.MaxStackCount); } catch { }
                    item.StackCount = target;
                }

                item.Durability = s.durability;
                item.DurabilityLoss = s.durabilityLoss;
                item.Inspected = s.inspected;

                // Slots
                if (s.slots != null && s.slots.Count > 0 && item.Slots != null)
                {
                    foreach (var (key, childSnap) in s.slots)
                    {
                        if (string.IsNullOrEmpty(key)) continue;
                        var slot = item.Slots.GetSlot(key);
                        if (slot == null) { Debug.LogWarning($"[ITEM] 找不到槽位 key={key} on {item.DisplayName}"); continue; }
                        var child = BuildItemFromSnapshot(childSnap);
                        if (child == null) continue;
                        if (!slot.Plug(child, out _))
                            TryAddToInventory(item.Inventory, child);
                    }
                }

                // 容器内容
                if (s.inventory != null && s.inventory.Count > 0)
                {
                    foreach (var childSnap in s.inventory)
                    {
                        var child = BuildItemFromSnapshot(childSnap);
                        if (child == null) continue;
                        TryAddToInventory(item.Inventory, child);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ITEM] ApplySnapshot 出错: {e}");
            }
        }



        // 取容器内列表（反射兜底）
        static List<Item> TryGetInventoryItems(Inventory inv)
        {
            if (inv == null) return null;

            var list = inv.Content;
            return list;
        }

        // 向容器添加（反射兜底）
        static bool TryAddToInventory(Inventory inv, Item child)
        {
            if (inv == null || child == null) return false;
            try
            {
                // 统一走“合并 + 放入”，内部会在需要时 Detach
                return ItemUtilities.AddAndMerge(inv, child, 0);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ITEM] Inventory.Add* 失败: {e.Message}");
                try { child.Detach(); return inv.AddItem(child); } catch { }
            }
            return false;
        }


        public void SendItemDropRequest(uint token, Item item, Vector3 pos, bool createRb, Vector3 dir, float angle)
        {
            if (netManager == null || IsServer) return;
            var w = writer;
            w.Reset();
            w.Put((byte)Op.ITEM_DROP_REQUEST);
            w.Put(token);
            NetPack.PutV3cm(w, pos);
            NetPack.PutDir(w, dir);
            w.Put(angle);
            w.Put(createRb);
            WriteItemSnapshot(w, item);
            SendReliable(w);
        }

        void HandleItemDropRequest(NetPeer peer, NetPacketReader r)
        {
            if (!IsServer) return;
            uint token = r.GetUInt();
            Vector3 pos = r.GetV3cm();
            Vector3 dir = r.GetDir();
            float angle = r.GetFloat();
            bool create = r.GetBool();
            var snap = ReadItemSnapshot(r);

            // 在主机生成物体（并阻止 Postfix 再广播）
            var item = BuildItemFromSnapshot(snap);
            if (item == null) return;
            _serverSpawnedFromClientItems.Add(item);
            var agent = item.Drop(pos, create, dir, angle);

            // 分配唯一 id，入表
            uint id = AllocateDropId();
            serverDroppedItems[id] = item;


            if (agent && agent.gameObject) AddNetDropTag(agent.gameObject, id);

            // 广播 SPAWN（包含 token，发回给所有客户端）
            var w = writer;
            w.Reset();
            w.Put((byte)Op.ITEM_SPAWN);
            w.Put(token);          // 回显客户端 token（发起者据此忽略）
            w.Put(id);
            NetPack.PutV3cm(w, pos);
            NetPack.PutDir(w, dir);
            w.Put(angle);
            w.Put(create);
            WriteItemSnapshot(w, item); // 用实际生成后的状态
            BroadcastReliable(w);
        }

        void HandleItemSpawn(NetPacketReader r)
        {
            if (IsServer) return;
            uint token = r.GetUInt();
            uint id = r.GetUInt();
            Vector3 pos = r.GetV3cm();
            Vector3 dir = r.GetDir();
            float angle = r.GetFloat();
            bool create = r.GetBool();
            var snap = ReadItemSnapshot(r);

            if (pendingLocalDropTokens.Remove(token))
            {
                if (pendingTokenItems.TryGetValue(token, out var localItem) && localItem != null)
                {
                    clientDroppedItems[id] = localItem;   // 主机id -> 本地item
                    pendingTokenItems.Remove(token);

                    AddNetDropTag(localItem, id);
                }
                else
                {
                    // 回退重建一份
                    var item2 = BuildItemFromSnapshot(snap);
                    if (item2 != null)
                    {
                        _clientSpawnByServerItems.Add(item2);
                        var agent2 = item2.Drop(pos, create, dir, angle);
                        clientDroppedItems[id] = item2;

                        if (agent2 && agent2.gameObject) AddNetDropTag(agent2.gameObject, id);
                    }
                }
                return;
            }

            // 正常路径：主机发来的新掉落
            var item = BuildItemFromSnapshot(snap);
            if (item == null) return;

            _clientSpawnByServerItems.Add(item);
            var agent = item.Drop(pos, create, dir, angle);
            clientDroppedItems[id] = item;

            if (agent && agent.gameObject) AddNetDropTag(agent.gameObject, id);
        }

        void SendItemPickupRequest(uint dropId)
        {
            if (IsServer || !networkStarted) return;
            var w = writer; w.Reset();
            w.Put((byte)Op.ITEM_PICKUP_REQUEST);
            w.Put(dropId);
            SendReliable(w);
        }

        void HandleItemPickupRequest(NetPeer peer, NetPacketReader r)
        {
            if (!IsServer) return;
            uint id = r.GetUInt();
            if (!serverDroppedItems.TryGetValue(id, out var item) || item == null)
                return; // 可能已经被别人拿走

            // 从映射表移除，并销毁场景 agent（若仍存在）
            serverDroppedItems.Remove(id);
            try
            {
                var agent = item.ActiveAgent;
                if (agent != null && agent.gameObject != null)
                    UnityEngine.Object.Destroy(agent.gameObject);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning($"[ITEM] 服务器销毁 agent 异常: {e.Message}"); }

            // 广播 DESPAWN
            var w = writer; w.Reset();
            w.Put((byte)Op.ITEM_DESPAWN);
            w.Put(id);
            BroadcastReliable(w);
        }

        void HandleItemDespawn(NetPacketReader r)
        {
            if (IsServer) return;
            uint id = r.GetUInt();
            if (clientDroppedItems.TryGetValue(id, out var item))
            {
                clientDroppedItems.Remove(id);
                try
                {
                    var agent = item?.ActiveAgent;
                    if (agent != null && agent.gameObject != null)
                        UnityEngine.Object.Destroy(agent.gameObject);
                }
                catch (Exception e) { UnityEngine.Debug.LogWarning($"[ITEM] 客户端销毁 agent 异常: {e.Message}"); }
            }
        }

        static void AddNetDropTag(UnityEngine.GameObject go, uint id)
        {
            if (!go) return;
            var tag = go.GetComponent<NetDropTag>() ?? go.AddComponent<NetDropTag>();
            tag.id = id;
        }
        static void AddNetDropTag(Item item, uint id)
        {
            try
            {
                var ag = item?.ActiveAgent;
                if (ag && ag.gameObject) AddNetDropTag(ag.gameObject, id);
            }
            catch { }
        }

        // 只按 typeId 解析：从 itemId → Item → Skill_Grenade.grenadePfb
        private async Cysharp.Threading.Tasks.UniTask ResolveAndSpawnClientAsync(PendingSpawn p)
        {
            var prefab = await 鸭科夫联机Mod.COOPManager.GetGrenadePrefabByItemIdAsync(p.typeId);
            if (!prefab)
            {
                UnityEngine.Debug.LogError($"[CLIENT] grenade prefab exact resolve failed: typeId={p.typeId}");
                return;
            }

            // 回灌缓存（只按 typeId）
            CacheGrenadePrefab(p.typeId, prefab);

            // 实例化 + 参数回放 + 启动
            var g = Instantiate(prefab, p.start, Quaternion.identity);
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
        private void ProcessPendingGrenades()
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
                    UnityEngine.Debug.LogError($"[CLIENT] grenade prefab resolve timeout: typeId={p.typeId}");
                    pending.RemoveAt(i);
                    continue;
                }

                // 快路径：本地已缓存（只按 typeId）
                if (prefabByTypeId.TryGetValue(p.typeId, out var prefab) && prefab)
                {
                    CacheGrenadePrefab(p.typeId, prefab);

                    var g = Instantiate(prefab, p.start, Quaternion.identity);
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

        // 服务器：根据 itemId 读取 Skill_Grenade 的“整包模板”
        // 返回：di（整包 DamageInfo）+ 其它 Grenade 字段（OnRelease 里会赋的）
        private async Cysharp.Threading.Tasks.UniTask<(global::DamageInfo di, bool create, float shake, float effectRange, bool delayFromCollide, float delay, bool isMine, float mineRange)>
            ReadGrenadeTemplateAsync(int typeId)
        {
            Item item = null;
            try
            {
                item = await 鸭科夫联机Mod.COOPManager.GetItemAsync(typeId);
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

        // 客户端：近战起手用于远端看得见
        public void Net_OnClientMeleeAttack(float dealDelay, Vector3 snapPos, Vector3 snapDir)
        {
            if (!networkStarted || IsServer || connectedPeer == null) return;
            writer.Reset();
            writer.Put((byte)Op.MELEE_ATTACK_REQUEST);
            writer.Put(dealDelay);
            writer.PutV3cm(snapPos);
            writer.PutDir(snapDir);
            connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        public void BroadcastMeleeSwing(string playerId, float dealDelay)
        {
            foreach (var p in netManager.ConnectedPeerList)
            {
                var w = new NetDataWriter();
                w.Put((byte)Op.MELEE_ATTACK_SWING);
                w.Put(playerId);
                w.Put(dealDelay);
                p.Send(w, DeliveryMethod.ReliableOrdered);
            }
        }


        // 主机：收到客户端“近战起手”，播动作 + 强制挥空 FX（避免动画事件缺失）
        void HandleMeleeAttackRequest(NetPeer sender, NetPacketReader reader)
        {

            float delay = reader.GetFloat();
            Vector3 pos = reader.GetV3cm();
            Vector3 dir = reader.GetDir();

            if (remoteCharacters.TryGetValue(sender, out var who) && who)
            {
                var anim = who.GetComponent<CharacterMainControl>().characterModel.GetComponent<CharacterAnimationControl_MagicBlend>();
                if (anim != null) anim.OnAttack();

                var model = who.GetComponent<CharacterMainControl>().characterModel;
                if (model) 鸭科夫联机Mod.MeleeFx.SpawnSlashFx(model);
            }

            string pid = (playerStatuses.TryGetValue(sender, out var st) && !string.IsNullOrEmpty(st.EndPoint))
                          ? st.EndPoint : sender.EndPoint.ToString();
            foreach (var p in netManager.ConnectedPeerList)
            {
                if (p == sender) continue;
                var w = new NetDataWriter();
                w.Put((byte)Op.MELEE_ATTACK_SWING);
                w.Put(pid);
                w.Put(delay);
                p.Send(w, DeliveryMethod.ReliableOrdered);
            }
        }

        // 工具：判断 DR 是否属于攻击者自己
        static bool IsSelfDR(DamageReceiver dr, CharacterMainControl attacker)
        {
            if (!dr || !attacker) return false;
            var owner = dr.GetComponentInParent<CharacterMainControl>(true);
            return owner == attacker;
        }

        // 工具：该 DR 是否属于“角色”（而不是环境/建筑）
        static bool IsCharacterDR(DamageReceiver dr)
        {
            return dr && dr.GetComponentInParent<CharacterMainControl>(true) != null;
        }
        void HandleMeleeHitReport(NetPeer sender, NetPacketReader reader)
        {
            Debug.Log($"[SERVER] HandleMeleeHitReport begin, from={sender?.EndPoint}, bytes={reader.AvailableBytes}");

            string attackerId = reader.GetString();

            float dmg = reader.GetFloat();
            float ap = reader.GetFloat();
            float cdf = reader.GetFloat();
            float cr = reader.GetFloat();
            int crit = reader.GetInt();

            Vector3 hitPoint = reader.GetV3cm();
            Vector3 normal = reader.GetDir();

            int wid = reader.GetInt();
            float bleed = reader.GetFloat();
            bool boom = reader.GetBool();
            float range = reader.GetFloat();

            if (!remoteCharacters.TryGetValue(sender, out var attackerGo) || !attackerGo)
            {
                Debug.LogWarning("[SERVER] melee: attackerGo missing for sender");
                return;
            }

            // 拿攻击者控制器（尽量是远端玩家本体）
            CharacterMainControl attackerCtrl = null;
            var attackerModel = attackerGo.GetComponent<CharacterModel>() ?? attackerGo.GetComponentInChildren<CharacterModel>(true);
            if (attackerModel && attackerModel.characterMainControl) attackerCtrl = attackerModel.characterMainControl;
            if (!attackerCtrl) attackerCtrl = attackerGo.GetComponent<CharacterMainControl>() ?? attackerGo.GetComponentInChildren<CharacterMainControl>(true);
            if (!attackerCtrl)
            {
                Debug.LogWarning("[SERVER] melee: attackerCtrl null (实例结构异常)");
                return;
            }

            // —— 搜附近候选（包含 Trigger）——
            int mask = GameplayDataSettings.Layers.damageReceiverLayerMask;
            float radius = Mathf.Clamp(range * 0.6f, 0.4f, 1.2f);

            Collider[] buf = new Collider[12];
            int n = 0;
            try
            {
                n = Physics.OverlapSphereNonAlloc(hitPoint, radius, buf, mask, QueryTriggerInteraction.UseGlobal);
            }
            catch
            {
                var tmp = Physics.OverlapSphere(hitPoint, radius, mask, QueryTriggerInteraction.UseGlobal);
                n = Mathf.Min(tmp.Length, buf.Length);
                Array.Copy(tmp, buf, n);
            }

            DamageReceiver best = null;
            float bestD2 = float.MaxValue;

            for (int i = 0; i < n; i++)
            {
                var col = buf[i]; if (!col) continue;
                var dr = col.GetComponent<DamageReceiver>(); if (!dr) continue;

                if (IsSelfDR(dr, attackerCtrl)) continue;                // 排自己
                if (IsCharacterDR(dr) && !Team.IsEnemy(dr.Team, attackerCtrl.Team)) continue; // 角色才做敌我判定

                float d2 = (dr.transform.position - hitPoint).sqrMagnitude;
                if (d2 < bestD2) { bestD2 = d2; best = dr; }
            }

            // 兜底：沿攻击方向短球扫
            if (!best)
            {
                Vector3 dir = attackerCtrl.transform.forward;
                Vector3 start = hitPoint - dir * 0.5f;
                if (Physics.SphereCast(start, 0.3f, dir, out var hit, 1.5f, mask, QueryTriggerInteraction.UseGlobal))
                {
                    var dr = hit.collider ? hit.collider.GetComponent<DamageReceiver>() : null;
                    if (dr != null && !IsSelfDR(dr, attackerCtrl))
                    {
                        if (!IsCharacterDR(dr) || Team.IsEnemy(dr.Team, attackerCtrl.Team))
                            best = dr;
                    }
                }
            }

            if (!best)
            {
                Debug.Log($"[SERVER] melee hit miss @ {hitPoint} r={radius}");
                return;
            }

            // 目标类型区分（角色/环境）
            bool victimIsChar = IsCharacterDR(best);

            // ★ 关键：环境/建筑用“空攻击者”避免二次缩放；角色保留攻击者
            var attackerForDI = (victimIsChar || !ServerTuning.UseNullAttackerForEnv) ? attackerCtrl : null;

            var di = new DamageInfo(attackerForDI)
            {
                damageValue = dmg,
                armorPiercing = ap,
                critDamageFactor = cdf,
                critRate = cr,
                crit = crit,
                damagePoint = hitPoint,
                damageNormal = normal,
                fromWeaponItemID = wid,
                bleedChance = bleed,
                isExplosion = boom
            };

            float scale = victimIsChar ? ServerTuning.RemoteMeleeCharScale : ServerTuning.RemoteMeleeEnvScale;
            if (Mathf.Abs(scale - 1f) > 1e-3f) di.damageValue = Mathf.Max(0f, di.damageValue * scale);

            Debug.Log($"[SERVER] melee hit -> target={best.name} raw={dmg} scaled={di.damageValue} env={!victimIsChar}");
            best.Hurt(di);
        }

        // 客户端：把受击请求发到主机（只发 payload，不结算）
        public void Client_RequestDestructibleHurt(uint id, DamageInfo dmg)
        {
            if (!networkStarted || IsServer || connectedPeer == null) return;

            var w = new NetDataWriter();
            w.Put((byte)Op.ENV_HURT_REQUEST);
            w.Put(id);

            // 复用你已有的紧凑负载（见 NetPack.PutDamagePayload）
            w.PutDamagePayload(
                dmg.damageValue, dmg.armorPiercing, dmg.critDamageFactor, dmg.critRate, dmg.crit,
                dmg.damagePoint, dmg.damageNormal, dmg.fromWeaponItemID, dmg.bleedChance, dmg.isExplosion,
                0f
            );
            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
        }

        // 主机：收到客户端请求，按本地规则结算，再广播受击
        private void Server_HandleEnvHurtRequest(NetPeer sender, NetPacketReader r)
        {
            uint id = r.GetUInt();
            var payload = r.GetDamagePayload(); // (dmg, ap, cdf, cr, crit, point, normal, wid, bleed, boom, range)

            var hs = FindDestructible(id);
            if (!hs) return;

            // 组 DamageInfo（用服务端权威；必要时可以做白名单/射线校验）
            var info = new DamageInfo
            {
                damageValue = payload.dmg * ServerTuning.RemoteMeleeEnvScale, // 你在 Mod.cs 的建议倍率
                armorPiercing = payload.ap,
                critDamageFactor = payload.cdf,
                critRate = payload.cr,
                crit = payload.crit,
                damagePoint = payload.point,
                damageNormal = payload.normal,
                fromWeaponItemID = payload.wid,
                bleedChance = payload.bleed,
                isExplosion = payload.boom,
                fromCharacter = null // 避免角色系数干扰（与 ServerTuning.UseNullAttackerForEnv 配套）
            };

            // 由 HealthSimpleBase 自己在 OnHurt 里做扣血/死亡判定（Postfix 会自动广播）
            try { hs.dmgReceiver.Hurt(info); } catch { }
        }

        // 主机：把受击事件广播给所有客户端：包括当前位置供播放 HitFx，以及当前血量（可用于客户端UI/调试）
        public void Server_BroadcastDestructibleHurt(uint id, float newHealth, DamageInfo dmg)
        {
            if (!networkStarted || !IsServer) return;
            var w = new NetDataWriter();
            w.Put((byte)Op.ENV_HURT_EVENT);
            w.Put(id);
            w.Put(newHealth);
            // Hit视觉信息足够：点+法线
            w.PutV3cm(dmg.damagePoint);
            w.PutDir(dmg.damageNormal.sqrMagnitude < 1e-6f ? Vector3.forward : dmg.damageNormal.normalized);
            netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        public void Server_BroadcastDestructibleDead(uint id, DamageInfo dmg)
        {
            var w = new NetDataWriter();
            w.Put((byte)Op.ENV_DEAD_EVENT);
            w.Put(id);
            w.PutV3cm(dmg.damagePoint);
            w.PutDir(dmg.damageNormal.sqrMagnitude < 1e-6f ? Vector3.up : dmg.damageNormal.normalized);
            netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        // 客户端：复现受击视觉（不改血量，不触发本地 OnHurt）
        // 客户端：复现受击视觉 + Breakable 的“危险态”显隐
        private void Client_ApplyDestructibleHurt(NetPacketReader r)
        {
            uint id = r.GetUInt();
            float curHealth = r.GetFloat();
            Vector3 point = r.GetV3cm();
            Vector3 normal = r.GetDir();

            // 已死亡就不播受击
            if (_deadDestructibleIds.Contains(id)) return;

            // 如果主机侧已经 <= 0，直接走死亡复现兜底
            if (curHealth <= 0f)
            {
                Client_ApplyDestructibleDead_Inner(id, point, normal);
                return;
            }

            var hs = FindDestructible(id);
            if (!hs) return;

            // 播放受击火花（项目里已有的 HurtVisual）
            var hv = hs.GetComponent<HurtVisual>();
            if (hv && hv.HitFx) Object.Instantiate(hv.HitFx, point, Quaternion.LookRotation(normal));

            // Breakable 的“危险态”切换（不改血，只做可视化）
            var br = hs.GetComponent<Breakable>();
            if (br)
            {
                // 危险阈值：源码里是 simpleHealth.HealthValue <= dangerHealth 时切到 danger。:contentReference[oaicite:7]{index=7}
                try
                {
                    // 当服务器汇报的血量低于危险阈值，且本地还没进危险态时，切显示 & 播一次 fx
                    if (curHealth <= br.dangerHealth && !_dangerDestructibleIds.Contains(id))
                    {
                        // normal -> danger
                        if (br.normalVisual) br.normalVisual.SetActive(false);
                        if (br.dangerVisual) br.dangerVisual.SetActive(true);
                        if (br.dangerFx) Object.Instantiate(br.dangerFx, br.transform.position, br.transform.rotation);
                        _dangerDestructibleIds.Add(id);
                    }
                }
                catch { /* 防御式：反编译字段为 null 时静默 */ }
            }
        }


        // 客户端：死亡复现（实际干活的内部函数）
        // 客户端：死亡复现（Breakable/半障碍/受击FX/碰撞体）
        private void Client_ApplyDestructibleDead_Inner(uint id, Vector3 point, Vector3 normal)
        {
            if (_deadDestructibleIds.Contains(id)) return;
            _deadDestructibleIds.Add(id);

            var hs = FindDestructible(id);
            if (!hs) return;

            // ★★ Breakable：复现 OnDead 里的可视化与爆炸（不做真正的扣血计算）
            var br = hs.GetComponent<Breakable>();
            if (br)
            {
                try
                {
                    // 视觉：normal/danger -> breaked
                    if (br.normalVisual) br.normalVisual.SetActive(false);
                    if (br.dangerVisual) br.dangerVisual.SetActive(false);
                    if (br.breakedVisual) br.breakedVisual.SetActive(true);

                    // 关闭主碰撞体
                    if (br.mainCollider) br.mainCollider.SetActive(false);

                    // 爆炸（与源码一致：LevelManager.ExplosionManager.CreateExplosion(...)）:contentReference[oaicite:9]{index=9}
                    if (br.createExplosion)
                    {
                        // fromCharacter 在客户端可为空，不影响范围伤害的演出
                        var di = br.explosionDamageInfo;
                        di.fromCharacter = null;
                        LevelManager.Instance.ExplosionManager.CreateExplosion(
                            hs.transform.position, br.explosionRadius, di, ExplosionFxTypes.normal, 1f
                        );
                    }
                }
                catch { /* 忽略反编译差异引发的异常 */ }
            }

            // HalfObsticle：走它自带的 Dead（工程里已有）  
            var half = hs.GetComponent<HalfObsticle>();
            if (half) { try { half.Dead(new DamageInfo { damagePoint = point, damageNormal = normal }); } catch { } }

            // 死亡特效（HurtVisual.DeadFx），项目里已有
            var hv = hs.GetComponent<HurtVisual>();
            if (hv && hv.DeadFx) Object.Instantiate(hv.DeadFx, hs.transform.position, hs.transform.rotation);

            // 关掉所有 Collider，防止残留可交互
            foreach (var c in hs.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }

        // 原来的 ENV_DEAD_EVENT 入口里，改为调用内部函数并记死
        private void Client_ApplyDestructibleDead(NetPacketReader r)
        {
            uint id = r.GetUInt();
            Vector3 point = r.GetV3cm();
            Vector3 normal = r.GetDir();
            Client_ApplyDestructibleDead_Inner(id, point, normal);
        }


        public void RegisterDestructible(uint id, HealthSimpleBase hs)
        {
            if (id == 0 || hs == null) return;
            if (IsServer) _serverDestructibles[id] = hs;
            else _clientDestructibles[id] = hs;
        }

        // 容错：找不到就全局扫一遍（场景切换后第一次命中时也能兜底）
        private HealthSimpleBase FindDestructible(uint id)
        {
            HealthSimpleBase hs = null;
            if (IsServer) _serverDestructibles.TryGetValue(id, out hs);
            else _clientDestructibles.TryGetValue(id, out hs);
            if (hs) return hs;

            var all = Object.FindObjectsOfType<HealthSimpleBase>(true);
            foreach (var e in all)
            {
                var tag = e.GetComponent<NetDestructibleTag>() ?? e.gameObject.AddComponent<NetDestructibleTag>();
                RegisterDestructible(tag.id, e);
                if (tag.id == id) hs = e;
            }
            return hs;
        }
        private void BuildDestructibleIndex()
        {
            // —— 兜底清空，防止跨图脏状态 —— //
            if (_deadDestructibleIds != null) _deadDestructibleIds.Clear();
            if (_dangerDestructibleIds != null) _dangerDestructibleIds.Clear();

            if (_serverDestructibles != null) _serverDestructibles.Clear();
            if (_clientDestructibles != null) _clientDestructibles.Clear();

            // 遍历所有 HSB（包含未激活物体，避免漏 index）
            var all = UnityEngine.Object.FindObjectsOfType<HealthSimpleBase>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var hs = all[i];
                if (!hs) continue;

                var tag = hs.GetComponent<NetDestructibleTag>();
                if (!tag) continue; // 我们只索引带有 NetDestructibleTag 的目标（墙/油桶等）

                // —— 统一计算稳定ID —— //
                uint id = ComputeStableIdForDestructible(hs);
                if (id == 0u)
                {
                    // 兜底：偶发异常时用自身 gameObject 算一次
                    try { id = NetDestructibleTag.ComputeStableId(hs.gameObject); } catch { }
                }
                tag.id = id;

                // —— 注册到现有索引（与你项目里的一致） —— //
                RegisterDestructible(tag.id, hs);
            }

            // —— 仅主机：扫描一遍“初始即已破坏”的目标，写进 _deadDestructibleIds —— //
            if (IsServer) // ⇦ 这里用你项目中判断“是否为主机”的字段/属性；若无则换成你原有判断
            {
                ScanAndMarkInitiallyDeadDestructibles();
            }
        }


        private void OnSceneLoaded_IndexDestructibles(Scene s, LoadSceneMode m)
        {
            if (!networkStarted) return;
            BuildDestructibleIndex();

            _cliHookedSelf = false;

            if (!IsServer)
            {
                _cliInitHpReported = false;      // 允许再次上报
                Client_ReportSelfHealth_IfReadyOnce(); // 你已有的方法（只上报一次）
            }

            try
            {
                if (!networkStarted || localPlayerStatus == null) return;

                var ok = ComputeIsInGame(out var sid);
                localPlayerStatus.SceneId = sid;
                localPlayerStatus.IsInGame = ok;

                if (!IsServer) SendClientStatusUpdate();
                else SendPlayerStatusUpdate();
            }
            catch { }

        }

        private void OnLevelInitialized_IndexDestructibles()
        {
            if (!networkStarted) return;
            BuildDestructibleIndex();
        }

        private string GetPlayerId(NetPeer peer)
        {
            if (peer == null)
            {
                if (localPlayerStatus != null && !string.IsNullOrEmpty(localPlayerStatus.EndPoint))
                    return localPlayerStatus.EndPoint;   // 例如 "Host:9050"
                return $"Host:{port}";
            }
            if (playerStatuses != null && playerStatuses.TryGetValue(peer, out var st) && !string.IsNullOrEmpty(st.EndPoint))
                return st.EndPoint;
            return peer.EndPoint.ToString();
        }

        // 绑定 Health⇄Character，修复“Health 没绑定角色”导致的 UI/Hidden 逻辑缺参
        private static void BindHealthToCharacter(Health h, CharacterMainControl cmc)
        {
            try { FI_characterCached?.SetValue(h, cmc); FI_hasCharacter?.SetValue(h, true); } catch { }
        }

        // 起条兜底：多帧重复请求血条，避免 UI 初始化竞态
        private static IEnumerator EnsureBarRoutine(Health h, int attempts, float interval)
        {
            for (int i = 0; i < attempts; i++)
            {
                if (h == null) yield break;
                try { h.showHealthBar = true; } catch { }
                try { h.RequestHealthBar(); } catch { }
                try { h.OnMaxHealthChange?.Invoke(h); } catch { }
                try { h.OnHealthChange?.Invoke(h); } catch { }
                yield return new WaitForSeconds(interval);
            }
        }

        // 把 (max,cur) 灌到 Health，并确保血条显示（修正 defaultMax=0）
        private void ForceSetHealth(Health h, float max, float cur, bool ensureBar = true)
        {
            if (!h) return;

            float nowMax = 0f; try { nowMax = h.MaxHealth; } catch { }
            int defMax = 0; try { defMax = (int)(FI_defaultMax?.GetValue(h) ?? 0); } catch { }

            // ★ 只要传入的 max 更大，就把 defaultMaxHealth 调到更大，并触发一次 Max 变更事件
            if (max > 0f && (nowMax <= 0f || max > nowMax + 0.0001f || defMax <= 0))
            {
                try
                {
                    FI_defaultMax?.SetValue(h, Mathf.RoundToInt(max));
                    FI_lastMax?.SetValue(h, -12345f);
                    h.OnMaxHealthChange?.Invoke(h);
                }
                catch { }
            }

            // ★ 避免被 SetHealth() 按旧 Max 夹住
            float effMax = 0f; try { effMax = h.MaxHealth; } catch { }
            if (effMax > 0f && cur > effMax + 0.0001f)
            {
                try { FI__current?.SetValue(h, cur); } catch { }
                try { h.OnHealthChange?.Invoke(h); } catch { }
            }
            else
            {
                try { h.SetHealth(cur); } catch { try { FI__current?.SetValue(h, cur); } catch { } }
                try { h.OnHealthChange?.Invoke(h); } catch { }
            }

            if (ensureBar)
            {
                try { h.showHealthBar = true; } catch { }
                try { h.RequestHealthBar(); } catch { }
                StartCoroutine(EnsureBarRoutine(h, 30, 0.1f));
            }
        }


        // 统一应用到某个 GameObject 的 Health（含绑定）
        // Mod.cs
        private void ApplyHealthAndEnsureBar(GameObject go, float max, float cur)
        {
            if (!go) return;

            var cmc = go.GetComponent<CharacterMainControl>();
            var h = go.GetComponentInChildren<Health>(true);
            if (!cmc || !h) return;

            try { h.autoInit = false; } catch { }

            // 绑定 Health ⇄ Character（否则 UI/Hidden 判断拿不到角色）
            BindHealthToCharacter(h, cmc);

            // 先把数值灌进去（内部会触发 OnMax/OnHealth）
            ForceSetHealth(h, max > 0 ? max : 40f, (cur > 0 ? cur : (max > 0 ? max : 40f)), ensureBar: false);

            // 立刻起条 + 多帧兜底（UI 还没起来时反复 Request）
            try { h.showHealthBar = true; } catch { }
            try { h.RequestHealthBar(); } catch { }

            // 触发一轮事件，部分 UI 需要
            try { h.OnMaxHealthChange?.Invoke(h); } catch { }
            try { h.OnHealthChange?.Invoke(h); } catch { }

            // 多帧重试：8 次、每 0.25s 一次（你已有 EnsureBarRoutine(h, attempts, interval)）
            StartCoroutine(EnsureBarRoutine(h, 8, 0.25f));
        }


        private void Server_OnHealthChanged(NetPeer ownerPeer, Health h)
        {
            if (!IsServer || !h) return;

            float max = 0f, cur = 0f;
            try { max = h.MaxHealth; } catch { }
            try { cur = h.CurrentHealth; } catch { }

            if (max <= 0f) return;
            // 去抖 + 限频（与你现有字段保持一致）
            if (_srvLastSent.TryGetValue(h, out var last))
                if (Mathf.Approximately(max, last.max) && Mathf.Approximately(cur, last.cur))
                    return;

            float now = Time.time;
            if (_srvNextSend.TryGetValue(h, out var tNext) && now < tNext)
                return;

            _srvLastSent[h] = (max, cur);
            _srvNextSend[h] = now + SRV_HP_SEND_COOLDOWN;

            // 计算 playerId（你已有的辅助方法）
            string pid = GetPlayerId(ownerPeer);

            // ✅ 回传本人快照：AUTH_HEALTH_SELF（修复“自己本地看起来没伤害”的现象）
            if (ownerPeer != null && ownerPeer.ConnectionState == ConnectionState.Connected)
            {
                var w1 = new NetDataWriter();
                w1.Put((byte)Op.AUTH_HEALTH_SELF);
                w1.Put(max);
                w1.Put(cur);
                ownerPeer.Send(w1, DeliveryMethod.ReliableOrdered);
            }

            // ✅ 广播给其他玩家：AUTH_HEALTH_REMOTE（带 playerId）
            var w2 = new NetDataWriter();
            w2.Put((byte)Op.AUTH_HEALTH_REMOTE);
            w2.Put(pid);
            w2.Put(max);
            w2.Put(cur);

            foreach (var p in netManager.ConnectedPeerList)
            {
                if (p == ownerPeer) continue; // 跳过本人，避免重复
                p.Send(w2, DeliveryMethod.ReliableOrdered);
            }
        }

        private void Server_HookOneHealth(NetPeer peer, GameObject instance)
        {
            if (!instance) return;

            var h = instance.GetComponentInChildren<Health>(true);
            var cmc = instance.GetComponent<CharacterMainControl>();
            if (!h) return;

            try { h.autoInit = false; } catch { }
            BindHealthToCharacter(h, cmc); // 你已有：修正 hasCharacter 以便 UI/Hidden 逻辑正常

            // 记录归属 + 绑定事件（避免重复）
            _srvHealthOwner[h] = peer;      // host 自己传 null
            if (!_srvHooked.Contains(h))
            {
                h.OnHealthChange.AddListener(_ => Server_OnHealthChanged(peer, h));
                h.OnMaxHealthChange.AddListener(_ => Server_OnHealthChanged(peer, h));
                _srvHooked.Add(h);
            }

            // 1) 若服务器已缓存了该客户端“自报”的权威血量，先套用并广给其他客户端
            if (peer != null && _srvPendingHp.TryGetValue(peer, out var snap))
            {
                ApplyHealthAndEnsureBar(instance, snap.max, snap.cur);
                _srvPendingHp.Remove(peer);
                Server_OnHealthChanged(peer, h);
                return;
            }

            // 2) 否则读取当前值；若 Max<=0（常见于克隆且 autoInit=false），用兜底 40f 起条并广播
            float max = 0f, cur = 0f;
            try { max = h.MaxHealth; } catch { }
            try { cur = h.CurrentHealth; } catch { }

            if (max <= 0f) { max = 40f; if (cur <= 0f) cur = max; }

            ApplyHealthAndEnsureBar(instance, max, cur); // 会确保 showHealthBar + RequestHealthBar + 多帧重试
            Server_OnHealthChanged(peer, h);             // 立刻推一帧给“其他玩家”
        }




        // 服务器兜底：每帧确保所有权威对象都已挂监听（含主机自己）
        private void Server_EnsureAllHealthHooks()
        {
            if (!IsServer || !networkStarted) return;

            var hostMain = CharacterMainControl.Main;
            if (hostMain) Server_HookOneHealth(null, hostMain.gameObject);

            if (remoteCharacters != null)
            {
                foreach (var kv in remoteCharacters)
                {
                    var peer = kv.Key;
                    var go = kv.Value;
                    if (peer == null || !go) continue;
                    Server_HookOneHealth(peer, go);
                }
            }
        }

        private void Client_ApplyPendingSelfIfReady()
        {
            if (!_cliSelfHpPending) return;
            var main = CharacterMainControl.Main;
            if (!main) return;

            var h = main.GetComponentInChildren<Health>(true);
            var cmc = main.GetComponent<CharacterMainControl>();
            if (!h) return;

            try { h.autoInit = false; } catch { } // 防止本地也被 Init() 回满
            BindHealthToCharacter(h, cmc);
            ForceSetHealth(h, _cliSelfHpMax, _cliSelfHpCur, ensureBar: true);

            // 若现在血量已到 0，补一次死亡事件（只在客户端本地）
            Client_EnsureSelfDeathEvent(h, cmc);

            _cliSelfHpPending = false;
        }

        private void Client_ApplyPendingRemoteIfAny(string playerId, GameObject go)
        {
            if (string.IsNullOrEmpty(playerId) || !go) return;
            if (!_cliPendingRemoteHp.TryGetValue(playerId, out var snap)) return;

            var cmc = go.GetComponent<CharacterMainControl>();
            var h = cmc.Health;

            if (!h) return;

            try { h.autoInit = false; } catch { }
            BindHealthToCharacter(h, cmc);

            float applyMax = snap.max > 0f ? snap.max : (h.MaxHealth > 0f ? h.MaxHealth : 40f);
            float applyCur = snap.cur > 0f ? snap.cur : applyMax;

            ForceSetHealth(h, applyMax, applyCur, ensureBar: true);
            _cliPendingRemoteHp.Remove(playerId);


            if (_cliPendingProxyBuffs.TryGetValue(playerId, out var pendings) && pendings != null && pendings.Count > 0)
            {
                if (cmc)
                {
                    foreach (var (weaponTypeId, buffId) in pendings)
                    {
                        COOPManager.ResolveBuffAsync(weaponTypeId, buffId)
                            .ContinueWith(b => { if (b != null && cmc) cmc.AddBuff(b, null, weaponTypeId); })
                            .Forget();
                    }
                }
                _cliPendingProxyBuffs.Remove(playerId);
            }

        }

        private void Client_ReportSelfHealth_IfReadyOnce()
        {
            if (_cliApplyingSelfSnap || Time.time < _cliEchoMuteUntil) return;
            if (IsServer || _cliInitHpReported) return;
            if (connectedPeer == null || connectedPeer.ConnectionState != ConnectionState.Connected) return;

            var main = CharacterMainControl.Main;
            var h = main ? main.GetComponentInChildren<Health>(true) : null;
            if (!h) return;

            float max = 0f, cur = 0f;
            try { max = h.MaxHealth; } catch { }
            try { cur = h.CurrentHealth; } catch { }

            var w = new NetDataWriter();
            w.Put((byte)Op.PLAYER_HEALTH_REPORT);
            w.Put(max);
            w.Put(cur);
            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);

            _cliInitHpReported = true;
        }

        void HandlePlayerBuffSelfApply(NetPacketReader r)
        {
            int weaponTypeId = r.GetInt(); // overrideWeaponID（通常就是武器/手雷的 Item.TypeID）
            int buffId = r.GetInt();       // 兜底的 buff id
            ApplyBuffToSelf_Client(weaponTypeId, buffId).Forget();
        }


        async Cysharp.Threading.Tasks.UniTask ApplyBuffToSelf_Client(int weaponTypeId, int buffId)
        {
            var me = LevelManager.Instance ? LevelManager.Instance.MainCharacter : null;
            if (!me) return;

            var buff = await COOPManager.ResolveBuffAsync(weaponTypeId, buffId);
            if (buff != null) me.AddBuff(buff, fromWho: null, overrideWeaponID: weaponTypeId);
        }

        void HandleBuffProxyApply(NetPacketReader r)
        {
            string hostId = r.GetString();   // e.g. "Host:9050"
            int weaponTypeId = r.GetInt();
            int buffId = r.GetInt();
            ApplyBuffProxy_Client(hostId, weaponTypeId, buffId).Forget();
        }

        async Cysharp.Threading.Tasks.UniTask ApplyBuffProxy_Client(string playerId, int weaponTypeId, int buffId)
        {
            if (IsSelfId(playerId)) return; // 不应该给本地自己用这个分支
            if (!clientRemoteCharacters.TryGetValue(playerId, out var go) || go == null)
            {
                // 远端主机克隆还没生成？先记下来，等 CreateRemoteCharacterForClient 时补发
                if (!_cliPendingProxyBuffs.TryGetValue(playerId, out var list))
                    list = _cliPendingProxyBuffs[playerId] = new System.Collections.Generic.List<(int, int)>();
                list.Add((weaponTypeId, buffId));
                return;
            }

            var cmc = go.GetComponent<CharacterMainControl>();
            if (!cmc) return;

            var buff = await COOPManager.ResolveBuffAsync(weaponTypeId, buffId);
            if (buff != null) cmc.AddBuff(buff, fromWho: null, overrideWeaponID: weaponTypeId);


        }

        private List<string> BuildParticipantIds_Server()
        {
            var list = new List<string>();

            // 计算主机当前 SceneId（仅当真正处于关卡中）
            string hostSceneId = null;
            ComputeIsInGame(out hostSceneId); // 返回 false 也无所谓，hostSceneId 可能为 null/空

            // 主机自己
            var hostPid = GetPlayerId(null);
            if (!string.IsNullOrEmpty(hostPid)) list.Add(hostPid);

            // 仅把“SceneId == 主机SceneId”的客户端加入
            foreach (var kv in playerStatuses)
            {
                var peer = kv.Key;
                if (peer == null) continue;

                // 优先从服务端缓存的现场表取（最权威），兜底用 playerStatuses 的 SceneId
                string peerScene = null;
                if (!_srvPeerScene.TryGetValue(peer, out peerScene))
                    peerScene = kv.Value?.SceneId;

                if (!string.IsNullOrEmpty(hostSceneId) && !string.IsNullOrEmpty(peerScene))
                {
                    if (peerScene == hostSceneId)
                    {
                        var pid = GetPlayerId(peer);
                        if (!string.IsNullOrEmpty(pid)) list.Add(pid);
                    }
                }
                else
                {
                    // 如果一开始拿不到 SceneId（极端竞态），先把玩家加进来，交给客户端白名单过滤
                    var pid = GetPlayerId(peer);
                    if (!string.IsNullOrEmpty(pid)) list.Add(pid);
                }
            }

            return list;
        }


        private IEnumerable<NetPeer> Server_EnumPeersInSameSceneAsHost()
        {
            string hostSceneId = localPlayerStatus != null ? localPlayerStatus.SceneId : null;
            if (string.IsNullOrEmpty(hostSceneId))
                ComputeIsInGame(out hostSceneId);
            if (string.IsNullOrEmpty(hostSceneId)) yield break;

            foreach (var p in netManager.ConnectedPeerList)
            {
                string peerScene = null;
                if (!_srvPeerScene.TryGetValue(p, out peerScene) && playerStatuses.TryGetValue(p, out var st))
                    peerScene = st.SceneId;

                if (!string.IsNullOrEmpty(peerScene) && peerScene == hostSceneId)
                    yield return p;
            }
        }

        public void Host_BeginSceneVote_Simple(string targetSceneId, string curtainGuid,
                                               bool notifyEvac, bool saveToFile,
                                               bool useLocation, string locationName)
        {
            sceneTargetId = targetSceneId ?? "";
            sceneCurtainGuid = string.IsNullOrEmpty(curtainGuid) ? null : curtainGuid;
            sceneNotifyEvac = notifyEvac;
            sceneSaveToFile = saveToFile;
            sceneUseLocation = useLocation;
            sceneLocationName = locationName ?? "";

            // 参与者（同图优先；拿不到 SceneId 的竞态由客户端再过滤）
            sceneParticipantIds.Clear();
            sceneParticipantIds.AddRange(BuildParticipantIds_Server());

            sceneVoteActive = true;
            localReady = false;
            sceneReady.Clear();
            foreach (var pid in sceneParticipantIds) sceneReady[pid] = false;

            // 计算主机当前 SceneId
            string hostSceneId = null;
            ComputeIsInGame(out hostSceneId);
            hostSceneId = hostSceneId ?? string.Empty;

            var w = new NetDataWriter();
            w.Put((byte)Op.SCENE_VOTE_START);
            w.Put((byte)2);                
            w.Put(sceneTargetId);

            bool hasCurtain = !string.IsNullOrEmpty(sceneCurtainGuid);
            byte flags = PackFlags(hasCurtain, sceneUseLocation, sceneNotifyEvac, sceneSaveToFile);
            w.Put(flags);

            if (hasCurtain) w.Put(sceneCurtainGuid);
            w.Put(sceneLocationName);       // 空串也写
            w.Put(hostSceneId);            

            w.Put(sceneParticipantIds.Count);
            foreach (var pid in sceneParticipantIds) w.Put(pid);

          
            netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            Debug.Log($"[SCENE] 投票开始 v2: target='{sceneTargetId}', hostScene='{hostSceneId}', loc='{sceneLocationName}', count={sceneParticipantIds.Count}");

            // 如需“只发同图”，可以替换为下面这段（二选一）：
            /*
            foreach (var p in netManager.ConnectedPeerList)
            {
                if (p == null) continue;

                string peerScene = null;
                if (!_srvPeerScene.TryGetValue(p, out peerScene) && playerStatuses.TryGetValue(p, out var st))
                    peerScene = st?.SceneId;

                if (!string.IsNullOrEmpty(peerScene) && peerScene == hostSceneId)
                {
                    var ww = new NetDataWriter();
                    ww.Put((byte)Op.SCENE_VOTE_START);
                    ww.Put((byte)2);
                    ww.Put(sceneTargetId);
                    ww.Put(flags);
                    if (hasCurtain) ww.Put(sceneCurtainGuid);
                    ww.Put(sceneLocationName);
                    ww.Put(hostSceneId);
                    ww.Put(sceneParticipantIds.Count);
                    foreach (var pid in sceneParticipantIds) ww.Put(pid);

                    p.Send(ww, DeliveryMethod.ReliableOrdered);
                }
            }
            */
        }





        // ===== 主机：有人（或主机自己）切换准备 =====
        private void Server_OnSceneReadySet(NetPeer fromPeer, bool ready)
        {
            if (!IsServer) return;

            // 统一 pid（fromPeer==null 代表主机自己）
            string pid = (fromPeer != null) ? GetPlayerId(fromPeer) : GetPlayerId(null);

            if (!sceneVoteActive) return;
            if (!sceneReady.ContainsKey(pid)) return; // 不在这轮投票里，丢弃

            sceneReady[pid] = ready;

            // 群发给所有客户端（不再二次按“同图”过滤）
            var w = new NetDataWriter();
            w.Put((byte)Op.SCENE_READY_SET);
            w.Put(pid);
            w.Put(ready);
            netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);

            // 检查是否全员准备
            foreach (var id in sceneParticipantIds)
                if (!sceneReady.TryGetValue(id, out bool r) || !r) return;

            // 全员就绪 → 开始加载
            Server_BroadcastBeginSceneLoad();
        }


        private void Server_BroadcastBeginSceneLoad()
        {

            if (_spectatorActive && _spectatorEndOnVotePending)
            {
                _spectatorEndOnVotePending = false;
                EndSpectatorAndShowClosure();
            }

            var w = new NetDataWriter();
            w.Put((byte)Op.SCENE_BEGIN_LOAD);
            w.Put((byte)1); // ver=1
            w.Put(sceneTargetId ?? "");

            bool hasCurtain = !string.IsNullOrEmpty(sceneCurtainGuid);
            byte flags = PackFlags(hasCurtain, sceneUseLocation, sceneNotifyEvac, sceneSaveToFile);
            w.Put(flags);

            if (hasCurtain) w.Put(sceneCurtainGuid);
            w.Put(sceneLocationName ?? "");

            // ★ 群发给所有客户端（客户端会根据是否正在投票/是否在名单自行处理）
            netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);

            // 主机本地执行加载
            allowLocalSceneLoad = true;
            var map = GetMapSelectionEntrylist(sceneTargetId);
            if (map != null && IsMapSelectionEntry)
            {
                IsMapSelectionEntry = false;
                allowLocalSceneLoad = false;
                Call_NotifyEntryClicked_ByInvoke(MapSelectionView.Instance,map,null);
            }      
            else
            {
                TryPerformSceneLoad_Local(sceneTargetId, sceneCurtainGuid, sceneNotifyEvac, sceneSaveToFile, sceneUseLocation, sceneLocationName);
            }

            // 收尾与清理
            sceneVoteActive = false;
            sceneParticipantIds.Clear();
            sceneReady.Clear();
            localReady = false;
        }

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

        // ===== 客户端：收到“投票开始”（带参与者 pid 列表）=====
        private void Client_OnSceneVoteStart(NetPacketReader r)
        {
            // ——读包：严格按顺序——
            if (!EnsureAvailable(r, 2)) { Debug.LogWarning("[SCENE] vote: header too short"); return; }
            byte ver = r.GetByte(); // switch 里已经吃掉了 op，这里是 ver
            if (ver != 1 && ver != 2)
            {
                Debug.LogWarning($"[SCENE] vote: unsupported ver={ver}");
                return;
            }

            if (!TryGetString(r, out sceneTargetId)) { Debug.LogWarning("[SCENE] vote: bad sceneId"); return; }

            if (!EnsureAvailable(r, 1)) { Debug.LogWarning("[SCENE] vote: no flags"); return; }
            byte flags = r.GetByte();
            bool hasCurtain, useLoc, notifyEvac, saveToFile;
            UnpackFlags(flags, out hasCurtain, out useLoc, out notifyEvac, out saveToFile);

            string curtainGuid = null;
            if (hasCurtain)
            {
                if (!TryGetString(r, out curtainGuid)) { Debug.LogWarning("[SCENE] vote: bad curtain"); return; }
            }

            string locName = null;
            if (!TryGetString(r, out locName)) { Debug.LogWarning("[SCENE] vote: bad location"); return; }

 
            string hostSceneId = string.Empty;
            if (ver >= 2)
            {
                if (!TryGetString(r, out hostSceneId)) { Debug.LogWarning("[SCENE] vote: bad hostSceneId"); return; }
                hostSceneId = hostSceneId ?? string.Empty;
            }

            if (!EnsureAvailable(r, 4)) { Debug.LogWarning("[SCENE] vote: no count"); return; }
            int cnt = r.GetInt();
            if (cnt < 0 || cnt > 256) { Debug.LogWarning("[SCENE] vote: weird count"); return; }

            sceneParticipantIds.Clear();
            for (int i = 0; i < cnt; i++)
            {
                if (!TryGetString(r, out var pid)) { Debug.LogWarning($"[SCENE] vote: bad pid[{i}]"); return; }
                sceneParticipantIds.Add(pid ?? "");
            }

            // ===== 过滤：不同图 & 不在白名单，直接忽略 =====
            string mySceneId = null;
            ComputeIsInGame(out mySceneId);
            mySceneId = mySceneId ?? string.Empty;

            // A) 同图过滤（仅 v2 有 hostSceneId；v1 无法判断同图，用 B 兜底）
            if (!string.IsNullOrEmpty(hostSceneId) && !string.IsNullOrEmpty(mySceneId))
            {
                if (!string.Equals(hostSceneId, mySceneId, StringComparison.Ordinal))
                {
                    Debug.Log($"[SCENE] vote: ignore (diff scene) host='{hostSceneId}' me='{mySceneId}'");
                    return;
                }
            }

            // B) 白名单过滤：不在参与名单，就不显示
            if (sceneParticipantIds.Count > 0 && localPlayerStatus != null)
            {
                var me = localPlayerStatus.EndPoint ?? string.Empty;
                if (!sceneParticipantIds.Contains(me))
                {
                    Debug.Log($"[SCENE] vote: ignore (not in participants) me='{me}'");
                    return;
                }
            }

            // ——赋值到状态 & 初始化就绪表——
            sceneCurtainGuid = curtainGuid;
            sceneUseLocation = useLoc;
            sceneNotifyEvac = notifyEvac;
            sceneSaveToFile = saveToFile;
            sceneLocationName = locName ?? "";

            sceneVoteActive = true;
            localReady = false;
            sceneReady.Clear();
            foreach (var pid in sceneParticipantIds) sceneReady[pid] = false;

            Debug.Log($"[SCENE] 收到投票 v{ver}: target='{sceneTargetId}', hostScene='{hostSceneId}', myScene='{mySceneId}', players={cnt}");

            // TODO：在这里弹出你的投票 UI（如果之前就是这里弹的，维持不变）
            // ShowSceneVoteUI(sceneTargetId, sceneLocationName, sceneParticipantIds) 等
        }




        // ===== 客户端：收到“某人准备状态变更”（pid + ready）=====
        private void Client_OnSomeoneReadyChanged(NetPacketReader r)
        {
            string pid = r.GetString();
            bool rd = r.GetBool();
            if (sceneReady.ContainsKey(pid)) sceneReady[pid] = rd;
        }
        public bool IsMapSelectionEntry = false;
        private void Client_OnBeginSceneLoad(NetPacketReader r)
        {
            if (!EnsureAvailable(r, 2)) { Debug.LogWarning("[SCENE] begin: header too short"); return; }
            byte ver = r.GetByte();
            if (ver != 1) { Debug.LogWarning($"[SCENE] begin: unsupported ver={ver}"); return; }

            if (!TryGetString(r, out var id)) { Debug.LogWarning("[SCENE] begin: bad sceneId"); return; }

            if (!EnsureAvailable(r, 1)) { Debug.LogWarning("[SCENE] begin: no flags"); return; }
            byte flags = r.GetByte();
            bool hasCurtain, useLoc, notifyEvac, saveToFile;
            UnpackFlags(flags, out hasCurtain, out useLoc, out notifyEvac, out saveToFile);

            string curtainGuid = null;
            if (hasCurtain)
            {
                if (!TryGetString(r, out curtainGuid)) { Debug.LogWarning("[SCENE] begin: bad curtain"); return; }
            }
            if (!TryGetString(r, out var locName)) { Debug.LogWarning("[SCENE] begin: bad locName"); return; }

            allowLocalSceneLoad = true;
            var map = GetMapSelectionEntrylist(sceneTargetId);
            if (map != null && sceneLocationName == "OnPointerClick")
            {
                IsMapSelectionEntry = false;
                allowLocalSceneLoad = false;
                Call_NotifyEntryClicked_ByInvoke(MapSelectionView.Instance, map, null);
            }
            else
            {
                TryPerformSceneLoad_Local(sceneTargetId, sceneCurtainGuid, sceneNotifyEvac, sceneSaveToFile, sceneUseLocation, sceneLocationName);
            }

            sceneVoteActive = false;
            sceneParticipantIds.Clear();
            sceneReady.Clear();
            localReady = false;
        }

        private void Client_SendReadySet(bool ready)
        {
            if (IsServer || connectedPeer == null) return;

            var w = new NetDataWriter();
            w.Put((byte)Op.SCENE_READY_SET);
            w.Put(ready);
            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);

            // ★ 本地乐观更新：立即把自己的 ready 写进就绪表，以免 UI 卡在“未准备”
            if (sceneVoteActive && localPlayerStatus != null)
            {
                var me = localPlayerStatus.EndPoint ?? string.Empty;
                if (!string.IsNullOrEmpty(me) && sceneReady.ContainsKey(me))
                    sceneReady[me] = ready;
            }
        }

        private void TryPerformSceneLoad_Local(string targetSceneId, string curtainGuid,
                                         bool notifyEvac, bool save,
                                         bool useLocation, string locationName)
        {
            try
            {
                var loader = SceneLoader.Instance;
                bool launched = false;           // 是否已触发加载

                // （如果后面你把 loader.LoadScene 恢复了，这里可以先试 loader 路径并把 launched=true）

                // 无论 loader 是否存在，都尝试 SceneLoaderProxy 兜底
                foreach (var ii in GameObject.FindObjectsOfType<SceneLoaderProxy>())
                {
                    try
                    {
                        if (Traverse.Create(ii).Field<string>("sceneID").Value == targetSceneId)
                        {
                            ii.LoadScene();
                            launched = true;
                            Debug.Log($"[SCENE] Fallback via SceneLoaderProxy -> {targetSceneId}");
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[SCENE] proxy check failed: " + e);
                    }
                }

                if (!launched)
                {
                    Debug.LogWarning($"[SCENE] Local load fallback failed: no proxy for '{targetSceneId}'");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SCENE] Local load failed: " + e);
            }
            finally
            {
                allowLocalSceneLoad = false;
                if (networkStarted)
                {
                    if (IsServer) SendPlayerStatusUpdate();
                    else SendClientStatusUpdate();
                }
            }
        }




        // 可留空，未来接上你自己的 GUID->SceneReference 工具
        private Eflatun.SceneReference.SceneReference TryResolveCurtain(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            return null;
        }

        // ——flags 打包/解包——
        static byte PackFlags(bool hasCurtain, bool useLoc, bool notifyEvac, bool saveToFile)
        {
            byte f = 0;
            if (hasCurtain) f |= 1 << 0;
            if (useLoc) f |= 1 << 1;
            if (notifyEvac) f |= 1 << 2;
            if (saveToFile) f |= 1 << 3;
            return f;
        }
        static void UnpackFlags(byte f, out bool hasCurtain, out bool useLoc, out bool notifyEvac, out bool saveToFile)
        {
            hasCurtain = (f & (1 << 0)) != 0;
            useLoc = (f & (1 << 1)) != 0;
            notifyEvac = (f & (1 << 2)) != 0;
            saveToFile = (f & (1 << 3)) != 0;
        }

        // ——安全读取（调试期防止崩溃）——
        static bool TryGetString(NetPacketReader r, out string s)
        {
            try { s = r.GetString(); return true; } catch { s = null; return false; }
        }
        static bool EnsureAvailable(NetPacketReader r, int need)
        {
            return r.AvailableBytes >= need;
        }

        // ====== 进图状态与 SceneId 获取 ======
        // Mod.cs
        private bool ComputeIsInGame(out string sceneId)
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

        private void TrySendSceneReadyOnce()
        {
            if (!networkStarted) return;

            // 只有真正进入地图（拿到 SceneId）才上报
            if (!ComputeIsInGame(out var sid) || string.IsNullOrEmpty(sid)) return;
            if (_sceneReadySidSent == sid) return; // 去抖：本场景只发一次

            var lm = LevelManager.Instance;
            var pos = (lm && lm.MainCharacter) ? lm.MainCharacter.transform.position : Vector3.zero;
            var rot = (lm && lm.MainCharacter) ? lm.MainCharacter.modelRoot.transform.rotation : Quaternion.identity;
            var faceJson = LoadLocalCustomFaceJson() ?? string.Empty;

            writer.Reset();
            writer.Put((byte)Op.SCENE_READY);    // 你的枚举里已有 23 = SCENE_READY
            writer.Put(localPlayerStatus?.EndPoint ?? (IsServer ? $"Host:{port}" : "Client:Unknown"));
            writer.Put(sid);
            writer.PutVector3(pos);
            writer.PutQuaternion(rot);
            writer.Put(faceJson);


            if (IsServer)
            {
                // 主机广播（本机也等同已就绪，方便让新进来的客户端看到主机）
                netManager?.SendToAll(writer, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                connectedPeer?.Send(writer, DeliveryMethod.ReliableOrdered);
            }

            _sceneReadySidSent = sid;
        }

        private static void MakeRemotePhysicsPassive(GameObject go)
        {
            if (!go) return;

            // 1) 典型运动/导航组件：关掉使其不再自行挪动
            var ai = go.GetComponentInChildren<AICharacterController>(true);
            if (ai) ai.enabled = false;

            var nma = go.GetComponentInChildren<NavMeshAgent>(true);
            if (nma) nma.enabled = false;

            var cc = go.GetComponentInChildren<CharacterController>(true);
            if (cc) cc.enabled = false; // 命中体积通常有独立 collider，不依赖 CC

            // 2) 刚体改为运动由我们驱动
            var rb = go.GetComponentInChildren<Rigidbody>(true);
            if (rb) { rb.isKinematic = true; rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

            // 3) 确保 Animator 不做 root motion（动画仍会更新）
            var anim = go.GetComponentInChildren<Animator>(true);
            if (anim) anim.applyRootMotion = false;

            // 其它你项目里会“推进角色”的脚本，可按名称做兜底反射关闭
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!mb) continue;
                var n = mb.GetType().Name;
                // 只关闭明显与移动/导航相关的
                if (n.Contains("Locomotion") || n.Contains("Movement") || n.Contains("Motor"))
                {
                    var beh = mb as Behaviour;
                    if (beh) beh.enabled = false;
                }
            }
        }

        private void Server_HandleSceneReady(NetPeer fromPeer, string playerId, string sceneId, Vector3 pos, Quaternion rot, string faceJson)
        {
            if (fromPeer != null) _srvPeerScene[fromPeer] = sceneId;

            // 1) 回给 fromPeer：同图的所有已知玩家
            foreach (var kv in _srvPeerScene)
            {
                var other = kv.Key; if (other == fromPeer) continue;
                if (kv.Value == sceneId)
                {
                    // 取 other 的快照（尽量从 playerStatuses 或远端对象抓取）
                    Vector3 opos = Vector3.zero; Quaternion orot = Quaternion.identity; string oface = "";
                    if (playerStatuses.TryGetValue(other, out var s) && s != null)
                    {
                        opos = s.Position; orot = s.Rotation; oface = s.CustomFaceJson ?? "";
                    }
                    var w = new NetDataWriter();
                    w.Put((byte)Op.REMOTE_CREATE);
                    w.Put(playerStatuses[other].EndPoint); // other 的 id
                    w.Put(sceneId);
                    w.PutVector3(opos);
                    w.PutQuaternion(orot);
                    w.Put(oface);
                    fromPeer?.Send(w, DeliveryMethod.ReliableOrdered);
                }
            }

            // 2) 广播给同图的其他人：创建 fromPeer
            foreach (var kv in _srvPeerScene)
            {
                var other = kv.Key; if (other == fromPeer) continue;
                if (kv.Value == sceneId)
                {
                    var w = new NetDataWriter();
                    w.Put((byte)Op.REMOTE_CREATE);
                    w.Put(playerId);
                    w.Put(sceneId);
                    w.PutVector3(pos);
                    w.PutQuaternion(rot);
                    string useFace = !string.IsNullOrEmpty(faceJson) ? faceJson: ((playerStatuses.TryGetValue(fromPeer, out var ss) && !string.IsNullOrEmpty(ss.CustomFaceJson)) ? ss.CustomFaceJson : "");
                    w.Put(useFace);
                    other.Send(w, DeliveryMethod.ReliableOrdered);
                }
            }

            // 3) 对不同图的人，互相 DESPAWN
            foreach (var kv in _srvPeerScene)
            {
                var other = kv.Key; if (other == fromPeer) continue;
                if (kv.Value != sceneId)
                {
                    var w1 = new NetDataWriter();
                    w1.Put((byte)Op.REMOTE_DESPAWN);
                    w1.Put(playerId);
                    other.Send(w1, DeliveryMethod.ReliableOrdered);

                    var w2 = new NetDataWriter();
                    w2.Put((byte)Op.REMOTE_DESPAWN);
                    w2.Put(playerStatuses[other].EndPoint);
                    fromPeer?.Send(w2, DeliveryMethod.ReliableOrdered);
                }
            }

            // 4) （可选）主机本地也显示客户端：在主机场景创建“该客户端”的远端克隆
            if (remoteCharacters.TryGetValue(fromPeer, out var exists) == false || exists == null)
            {
                CreateRemoteCharacterAsync(fromPeer, pos, rot, faceJson).Forget();
            }


        }

        // ========== 环境同步：主机广播 ==========
        private void Server_BroadcastEnvSync(NetPeer target = null)
        {
            if (!IsServer || netManager == null) return;

            // 1) 采样主机的“当前天数 + 当天秒数 + 时钟倍率”
            long day = GameClock.Day;                                      // 只读属性，取值 OK :contentReference[oaicite:6]{index=6}
            double secOfDay = GameClock.TimeOfDay.TotalSeconds;            // 当天秒数（0~86300） :contentReference[oaicite:7]{index=7}
            float timeScale = 60f;
            try { timeScale = GameClock.Instance.clockTimeScale; } catch { } // 公有字段 :contentReference[oaicite:8]{index=8}

            // 2) 采样天气：seed / 强制天气开关和值 / 当前天气（兜底）/（冗余）风暴等级
            var wm = Duckov.Weathers.WeatherManager.Instance;
            int seed = -1;
            bool forceWeather = false;
            int forceWeatherVal = (int)Duckov.Weathers.Weather.Sunny;
            int currentWeather = (int)Duckov.Weathers.Weather.Sunny;
            byte stormLevel = 0;

            if (wm != null)
            {
                try { seed = (int)AccessTools.Field(wm.GetType(), "seed").GetValue(wm); } catch { }
                try { forceWeather = (bool)AccessTools.Field(wm.GetType(), "forceWeather").GetValue(wm); } catch { } // 若字段名不同可改为属性读取
                try { forceWeatherVal = (int)AccessTools.Field(wm.GetType(), "forceWeatherValue").GetValue(wm); } catch { }
                try { currentWeather = (int)Duckov.Weathers.WeatherManager.GetWeather(); } catch { } // 公共静态入口 :contentReference[oaicite:9]{index=9}
                try { stormLevel = (byte)wm.Storm.GetStormLevel(GameClock.Now); } catch { } // 基于 Now 计算 :contentReference[oaicite:10]{index=10}
            }

            // 3) 打包并发出
            var w = new NetDataWriter();
            w.Put((byte)Op.ENV_SYNC_STATE);
            w.Put(day);
            w.Put(secOfDay);
            w.Put(timeScale);
            w.Put(seed);
            w.Put(forceWeather);
            w.Put(forceWeatherVal);
            w.Put(currentWeather);
            w.Put(stormLevel);

            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Duckov.Utilities.LootBoxLoader>(true);
                // 收集 (key, active)
                var tmp = new System.Collections.Generic.List<(int k, bool on)>(all.Length);
                foreach (var l in all)
                {
                    if (!l || !l.gameObject) continue;
                    int k = ComputeLootKey(l.transform);
                    bool on = l.gameObject.activeSelf; // 已经由 RandomActive 决定
                    tmp.Add((k, on));
                }

                w.Put(tmp.Count);
                for (int i = 0; i < tmp.Count; ++i)
                {
                    w.Put(tmp[i].k);
                    w.Put(tmp[i].on);
                }
            }
            catch
            {
                // 防守式：写一个 0，避免客户端读表时越界
                w.Put(0);
            }

            // Door

            bool includeDoors = (target != null);
            if (includeDoors)
            {
                var doors = UnityEngine.Object.FindObjectsOfType<Door>(true);
                var tmp = new System.Collections.Generic.List<(int key, bool closed)>(doors.Length);

                foreach (var d in doors)
                {
                    if (!d) continue;
                    int k = 0;
                    try { k = (int)AccessTools.Field(typeof(Door), "doorClosedDataKeyCached").GetValue(d); } catch { }
                    if (k == 0) k = ComputeDoorKey(d.transform);

                    bool closed;
                    try { closed = !d.IsOpen; } catch { closed = true; } // 兜底：没取到就当作关闭
                    tmp.Add((k, closed));
                }

                w.Put(tmp.Count);
                for (int i = 0; i < tmp.Count; ++i)
                {
                    w.Put(tmp[i].key);
                    w.Put(tmp[i].closed);
                }
            }
            else
            {
                w.Put(0); // 周期广播不带门清单
            }

            w.Put(_deadDestructibleIds.Count);
            foreach (var id in _deadDestructibleIds) w.Put(id);

            if (target != null) target.Send(w, DeliveryMethod.ReliableOrdered);
            else netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        // ========== 环境同步：客户端请求 ==========
        private void Client_RequestEnvSync()
        {
            if (IsServer || connectedPeer == null) return;
            var w = new NetDataWriter();
            w.Put((byte)Op.ENV_SYNC_REQUEST);
            connectedPeer.Send(w, DeliveryMethod.Sequenced);
        }

        // ========== 环境同步：客户端应用 ==========
        private void Client_ApplyEnvSync(long day, double secOfDay, float timeScale, int seed, bool forceWeather, int forceWeatherVal, int currentWeather /*兜底*/, byte stormLevel /*冗余*/)
        {
            // 1) 绝对对时：直接改 GameClock 的私有字段（避免 StepTimeTil 无法回拨的问题）
            try
            {
                var inst = GameClock.Instance;
                if (inst != null)
                {
                    AccessTools.Field(inst.GetType(), "days")?.SetValue(inst, day);
                    AccessTools.Field(inst.GetType(), "secondsOfDay")?.SetValue(inst, secOfDay);
                    try { inst.clockTimeScale = timeScale; } catch { }

                    // 触发一次 onGameClockStep（用 0 步长调用内部 Step，保证监听者能刷新）
                    typeof(GameClock).GetMethod("Step", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, new object[] { 0f });
                }
            }
            catch { }

            // 2) 天气随机种子：设到 WeatherManager，并让它把种子分发给子模块
            try
            {
                var wm = Duckov.Weathers.WeatherManager.Instance;
                if (wm != null && seed != -1)
                {
                    AccessTools.Field(wm.GetType(), "seed")?.SetValue(wm, seed);                // 写 seed :contentReference[oaicite:11]{index=11}
                    wm.GetType().GetMethod("SetupModules", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(wm, null); // 把 seed 带给 Storm/Precipitation :contentReference[oaicite:12]{index=12}
                    AccessTools.Field(wm.GetType(), "_weatherDirty")?.SetValue(wm, true);       // 标脏以便下帧重新取 GetWeather
                }
            }
            catch { }

            // 3) 强制天气（兜底）：若主机处于强制状态，则客户端也强制到同一值
            try
            {
                Duckov.Weathers.WeatherManager.SetForceWeather(forceWeather, (Duckov.Weathers.Weather)forceWeatherVal); // 公共静态入口 :contentReference[oaicite:13]{index=13}
            }
            catch { }

            // 4) 无需专门同步风暴 ETA：基于 Now+seed，Storm.* 会得到一致的结果（UI 每 0.5s 刷新，见 TimeOfDayDisplay） :contentReference[oaicite:14]{index=14}
        }

        private void PutLootId(NetDataWriter w, Inventory inv)
        {
            int scene = SceneManager.GetActiveScene().buildIndex;
            int posKey = -1;
            int instanceId = -1;

            var dict = InteractableLootbox.Inventories;
            if (inv != null && dict != null)
            {
                foreach (var kv in dict)
                {
                    if (kv.Value == inv) { posKey = kv.Key; break; }
                }
            }

            if (inv != null && (posKey < 0 || instanceId < 0))
            {
                var boxes = GameObject.FindObjectsOfType<InteractableLootbox>();
                foreach (var b in boxes)
                {
                    if (!b) continue;
                    if (b.Inventory == inv)
                    {
                        posKey = ComputeLootKey(b.transform);
                        instanceId = b.GetInstanceID();
                        break;
                    }
                }
            }

            // 稳定 ID（仅死亡箱子会命中，其它容器写 -1）
            int lootUid = -1;
            if (IsServer)
            {
                // 主机：从 _srvLootByUid 反查
                foreach (var kv in _srvLootByUid)
                {
                    if (kv.Value == inv) { lootUid = kv.Key; break; }
                }
            }
            else
            {
                // 客户端：从 _cliLootByUid 反查（关键修复）
                foreach (var kv in _cliLootByUid)
                {
                    if (kv.Value == inv) { lootUid = kv.Key; break; }
                }
            }

            w.Put(scene);
            w.Put(posKey);
            w.Put(instanceId);
            w.Put(lootUid);         
        }



        // 注意：保持你原有签名；内部先跨词典命中
        private bool TryResolveLootById(int scene, int posKey, int iid, out Inventory inv)
        {
            inv = null;

            // 先用 posKey 命中（跨词典）
            if (posKey != 0 && TryGetLootInvByKeyEverywhere(posKey, out inv)) return true;

            // 再按 iid 找 GameObject 上的 InteractableLootbox，取其 Inventory
            if (iid != 0)
            {
                try
                {
                    var all = UnityEngine.Object.FindObjectsOfType<InteractableLootbox>(true);
                    foreach (var b in all)
                    {
                        if (!b) continue;
                        if (b.GetInstanceID() == iid && (scene < 0 || b.gameObject.scene.buildIndex == scene))
                        {
                            inv = b.Inventory; // 走到这一步，get_Inventory 的兜底会触发
                            if (inv) return true;
                        }
                    }
                }
                catch { }
            }

            return false; // 交给 TryResolveLootByHint / Server_TryResolveLootAggressive
        }


        public void Client_RequestLootState(Inventory lootInv)
        {
            if (!networkStarted || IsServer || connectedPeer == null || lootInv == null) return;

            if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return;

            var w = writer;
            w.Reset();
            w.Put((byte)Op.LOOT_REQ_OPEN);

            // 原有三元标识（scene + posKey + instanceId）
            PutLootId(w, lootInv);

            // 请求版本 + 位置提示（cm 压缩）
            byte reqVer = 1;
            w.Put(reqVer);

            Vector3 pos;
            if (!TryGetLootboxWorldPos(lootInv, out pos)) pos = Vector3.zero;
            w.PutV3cm(pos);

            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
        }


        // 主机：应答快照（发给指定 peer 或广播）
        public void Server_SendLootboxState(NetPeer toPeer, Inventory inv)
        {
            // ★ 新增：仅当群发(toPeer==null)时才受静音窗口影响
            if (toPeer == null && Server_IsLootMuted(inv)) return;

            if (!IsServer || inv == null) return;
            if (!LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv))
                return;

            var w = new NetDataWriter();
            w.Put((byte)Op.LOOT_STATE);
            PutLootId(w, inv);

            int capacity = inv.Capacity;
            w.Put(capacity);

            // 统计非空格子数量
            int count = 0;
            var content = inv.Content;
            for (int i = 0; i < content.Count; ++i)
                if (content[i] != null) count++;
            w.Put(count);

            // 逐个写：位置 + 物品快照
            for (int i = 0; i < content.Count; ++i)
            {
                var it = content[i];
                if (it == null) continue;
                w.Put(i);
                WriteItemSnapshot(w, it);
            }

            if (toPeer != null) toPeer.Send(w, DeliveryMethod.ReliableOrdered);
            else BroadcastReliable(w);
        }


        public void Client_ApplyLootboxState(NetPacketReader r)
        {
            int scene = r.GetInt();
            int posKey = r.GetInt();
            int iid = r.GetInt();
            int lootUid = r.GetInt();               

            int capacity = r.GetInt();
            int count = r.GetInt();

            Inventory inv = null;

            // ★ 1) 优先用稳定 ID 解析
            if (lootUid >= 0 && _cliLootByUid.TryGetValue(lootUid, out var byUid) && byUid) inv = byUid;

            // 2) 失败再走旧逻辑（posKey / 扫场景）
            if (inv == null && (!TryResolveLootById(scene, posKey, iid, out inv) || inv == null))
            {
                if (LootboxDetectUtil.IsPrivateInventory(inv)) return;
                // ★ 若带了稳定 ID，则缓存到 uid 下；否则就按 posKey 缓存（次要）
                var list = new List<(int pos, ItemSnapshot snap)>(count);
                for (int k = 0; k < count; ++k)
                {
                    int p = r.GetInt();
                    var snap = ReadItemSnapshot(r);
                    list.Add((p, snap));
                }

                if (lootUid >= 0)
                    _pendingLootStatesByUid[lootUid] = (capacity, list);
                else
                {
                    // 旧路径的兜底（可选）：如果你之前已经做了 posKey 缓存，这里也可以顺手放一份
                }
                return;
            }

            if (LootboxDetectUtil.IsPrivateInventory(inv)) return;

            // ★ 容量安全阈值：防止因为误匹配把 UI 撑爆（真正根因是冲突/错配）
            capacity = Mathf.Clamp(capacity, 1, 128);

            _applyingLootState = true;
            try
            {
                inv.SetCapacity(capacity);
                inv.Loading = false;

                for (int i = inv.Content.Count - 1; i >= 0; --i)
                {
                    Item removed; inv.RemoveAt(i, out removed);
                    if (removed) Object.Destroy(removed.gameObject);
                }

                for (int k = 0; k < count; ++k)
                {
                    int pos = r.GetInt();
                    var snap = ReadItemSnapshot(r);
                    var item = BuildItemFromSnapshot(snap);
                    if (item == null) continue;
                    inv.AddAt(item, pos);
                }
            }
            finally { _applyingLootState = false; }


            try
            {
                var lv = Duckov.UI.LootView.Instance;
                if (lv && lv.open && ReferenceEquals(lv.TargetInventory, inv))
                {
                    // 轻量刷新：不强制重开，只更新细节/按钮与容量文本
                    AccessTools.Method(typeof(Duckov.UI.LootView), "RefreshDetails")?.Invoke(lv, null);
                    AccessTools.Method(typeof(Duckov.UI.LootView), "RefreshPickAllButton")?.Invoke(lv, null);
                    AccessTools.Method(typeof(Duckov.UI.LootView), "RefreshCapacityText")?.Invoke(lv, null);
                }
            }
            catch { }

        }



        // Mod.cs
        public void Client_SendLootPutRequest(Inventory lootInv, Item item, int preferPos)
        {
            if (!networkStarted || IsServer || connectedPeer == null || lootInv == null || item == null) return;

            if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return;


            // 同一物品的在途 PUT 防重
            foreach (var kv in _cliPendingPut)
            {
                var pending = kv.Value;
                if (pending && ReferenceEquals(pending, item))
                {
                    // 已经有一个在途请求了，丢弃重复点击
                    Debug.Log($"[LOOT] Duplicate PUT suppressed for item: {item.DisplayName}");
                    return;
                }
            }

            uint token = _nextLootToken++;
            _cliPendingPut[token] = item;

            var w = writer; w.Reset();
            w.Put((byte)Op.LOOT_REQ_PUT);
            PutLootId(w, lootInv);
            w.Put(preferPos);
            w.Put(token);
            WriteItemSnapshot(w, item);
            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
        }


        // 作用：发送 TAKE 请求（携带目标信息）；客户端暂不落位，等回包
        // 兼容旧调用：不带目的地
        public void Client_SendLootTakeRequest(ItemStatsSystem.Inventory lootInv, int position)
        {
            Client_SendLootTakeRequest(lootInv, position, null, -1, null);
        }

        // 新：带目的地（背包+格 或 装备槽）
        public uint Client_SendLootTakeRequest(
            ItemStatsSystem.Inventory lootInv,
            int position,
            ItemStatsSystem.Inventory destInv,
            int destPos,
            ItemStatsSystem.Items.Slot destSlot)
        {
            if (!networkStarted || IsServer || connectedPeer == null || lootInv == null) return 0;
            if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return 0;

            // 目标如果还是“容器”，就当作没指定（容器内换位由主机权威刷新）
            if (destInv != null && LootboxDetectUtil.IsLootboxInventory(destInv))
                destInv = null;

            uint token = _nextLootToken++;

            if (destInv != null || destSlot != null)
                _cliPendingTake[token] = new PendingTakeDest
                {
                    inv = destInv,
                    pos = destPos,
                    slot = destSlot,
                    //记录来源容器与来源格子（用于交换时回填）
                    srcLoot = lootInv,
                    srcPos = position
                };

            var w = writer; w.Reset();
            w.Put((byte)Op.LOOT_REQ_TAKE);
            PutLootId(w, lootInv); // 只写 inv 身份（scene/posKey/instance/uid）
            w.Put(position);
            w.Put(token);          // 附带 token
            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
            return token;
        }




        // 主机：处理 PUT（客户端 -> 主机）
        private void Server_HandleLootPutRequest(NetPeer peer, NetPacketReader r)
        {
            int scene = r.GetInt();
            int posKey = r.GetInt();
            int iid = r.GetInt();
            int lootUid = r.GetInt();   // 对齐 PutLootId 多写的稳定ID
            int prefer = r.GetInt();
            uint token = r.GetUInt();

            ItemSnapshot snap;
            try
            {
                snap = ReadItemSnapshot(r);
            }
            catch (DecoderFallbackException ex)
            {
                Debug.LogError($"[LOOT][PUT] snapshot decode failed: {ex.Message}");
                Server_SendLootDeny(peer, "bad_snapshot");
                return;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LOOT][PUT] snapshot parse failed: {ex}");
                Server_SendLootDeny(peer, "bad_snapshot");
                return;
            }

            // ★ 可选：如果未来客户端也会带有效的 lootUid，可优先用它定位
            Inventory inv = null;
            if (lootUid >= 0) _srvLootByUid.TryGetValue(lootUid, out inv);
            if (inv == null && !TryResolveLootById(scene, posKey, iid, out inv))
            {
                Server_SendLootDeny(peer, "no_inv"); return;
            }

            if (LootboxDetectUtil.IsPrivateInventory(inv)) { Server_SendLootDeny(peer, "no_inv"); return; }

            //if (!TryResolveLootById(scene, posKey, iid, out var inv) || inv == null)
            //{ Server_SendLootDeny(peer, "no_inv"); return; }

            var item = BuildItemFromSnapshot(snap);
            if (item == null) { Server_SendLootDeny(peer, "bad_item"); return; }

            _serverApplyingLoot = true;
            bool ok = false;
            try
            {
                ok = ItemUtilities.AddAndMerge(inv, item, prefer);
                if (!ok) Object.Destroy(item.gameObject);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LOOT][PUT] AddAndMerge exception: {ex}");
                ok = false;
            }
            finally { _serverApplyingLoot = false; }

            if (!ok) { Server_SendLootDeny(peer, "add_fail"); return; }

            var ack = new NetDataWriter();
            ack.Put((byte)Op.LOOT_PUT_OK);
            ack.Put(token);
            peer.Send(ack, DeliveryMethod.ReliableOrdered);

            Server_SendLootboxState(null, inv);
        }


        private void Server_HandleLootTakeRequest(NetPeer peer, NetPacketReader r)
        {
            int scene = r.GetInt();
            int posKey = r.GetInt();
            int iid = r.GetInt();
            int lootUid = r.GetInt();      // 对齐 PutLootId
            int position = r.GetInt();
            uint token = r.GetUInt();      // 读取 token

            Inventory inv = null;
            if (lootUid >= 0) _srvLootByUid.TryGetValue(lootUid, out inv);
            if (inv == null && !TryResolveLootById(scene, posKey, iid, out inv))
            { Server_SendLootDeny(peer, "no_inv"); return; }

            if (LootboxDetectUtil.IsPrivateInventory(inv)) { Server_SendLootDeny(peer, "no_inv"); return; }


            _serverApplyingLoot = true;
            bool ok = false; Item removed = null;
            try
            {
                if (position >= 0 && position < inv.Capacity)
                {
                    try { ok = inv.RemoveAt(position, out removed); }
                    catch (ArgumentOutOfRangeException) { ok = false; removed = null; }
                }
            }
            finally { _serverApplyingLoot = false; }

            if (!ok || removed == null)
            {
                Server_SendLootDeny(peer, "rm_fail");
                Server_SendLootboxState(peer, inv); // ⬅️ 刷新请求方 UI 的索引认知
                return;
            }

            var wCli = new NetDataWriter();
            wCli.Put((byte)Op.LOOT_TAKE_OK);
            wCli.Put(token);                 // ★ 回 token
            WriteItemSnapshot(wCli, removed);
            peer.Send(wCli, DeliveryMethod.ReliableOrdered);

            try { UnityEngine.Object.Destroy(removed.gameObject); } catch { }
            Server_SendLootboxState(null, inv);
        }






        private void Server_SendLootDeny(NetPeer peer, string reason)
        {
            var w = new NetDataWriter();
            w.Put((byte)Op.LOOT_DENY);
            w.Put(reason ?? "");
            peer?.Send(w, DeliveryMethod.ReliableOrdered);
        }

        // 客户端：收到 PUT_OK -> 把“本地发起的那件物品”从自己背包删掉
        private void Client_OnLootPutOk(NetPacketReader r)
        {
            uint token = r.GetUInt();

            if (_cliPendingSlotPlug.TryGetValue(token, out var victim) && victim)
            {
                try
                {
                    var srcInv = victim.InInventory;
                    if (srcInv) { try { srcInv.RemoveItem(victim); } catch { } }
                    UnityEngine.Object.Destroy(victim.gameObject);
                }
                catch {  }
                finally
                {
                    _cliPendingSlotPlug.Remove(token);
                }
                return; // 不再继续走“普通 PUT”流程
            }

            if (_cliPendingPut.TryGetValue(token, out var localItem) && localItem)
            {
                _cliPendingPut.Remove(token);

                // —— 交换路径：这次 PUT 的 localItem 是否正是我们等待交换的 victim？——
                if (_cliSwapByVictim.TryGetValue(localItem, out var ctx))
                {
                    _cliSwapByVictim.Remove(localItem);

                    // 1) victim 已经成功 PUT 到容器：本地把它清理掉
                    try { localItem.Detach(); } catch { }
                    try { UnityEngine.Object.Destroy(localItem.gameObject); } catch { }

                    // 2) 把“新物”真正落位（槽或背包格）
                    try
                    {
                        if (ctx.destSlot != null)
                        {
                            if (ctx.destSlot.CanPlug(ctx.newItem))
                                ctx.destSlot.Plug(ctx.newItem, out var _);
                        }
                        else if (ctx.destInv != null && ctx.destPos >= 0)
                        {
                            // 目标格此时应为空（victim 已被 PUT 走）
                            ctx.destInv.AddAt(ctx.newItem, ctx.destPos);
                        }
                    }
                    catch { }

                    // 3) 清理可能遗留的同物品 pending
                    var toRemove = new List<uint>();
                    foreach (var kv in _cliPendingPut)
                        if (!kv.Value || ReferenceEquals(kv.Value, localItem)) toRemove.Add(kv.Key);
                    foreach (var k in toRemove) _cliPendingPut.Remove(k);

                    return; // 交换流程结束
                }

                // —— 普通 PUT 成功：维持你原有的清理逻辑 —— 
                try { localItem.Detach(); } catch { }
                try { UnityEngine.Object.Destroy(localItem.gameObject); } catch { }

                var stale = new List<uint>();
                foreach (var kv in _cliPendingPut)
                    if (!kv.Value || ReferenceEquals(kv.Value, localItem)) stale.Add(kv.Key);
                foreach (var k in stale) _cliPendingPut.Remove(k);
            }
        }


        private void Client_OnLootTakeOk(NetPacketReader r)
        {
            uint token = r.GetUInt();

            // 1) 还原物品
            var snap = ReadItemSnapshot(r);
            var newItem = BuildItemFromSnapshot(snap);
            if (newItem == null) return;

            // —— 取出期望目的地（可能为空）——
            PendingTakeDest dest;
            if (_cliPendingTake.TryGetValue(token, out dest))
                _cliPendingTake.Remove(token);
            else
                dest = default;

            // —— 小工具A：不入队、不打 token 的“放回来源容器”——
            // 注意参数名用 srcInfo，避免与上面的 dest 冲突（修复 CS0136）
            void PutBackToSource_NoTrack(ItemStatsSystem.Item item, PendingTakeDest srcInfo)
            {
                var loot = srcInfo.srcLoot != null ? srcInfo.srcLoot
                          : (Duckov.UI.LootView.Instance ? Duckov.UI.LootView.Instance.TargetInventory : null);
                int preferPos = srcInfo.srcPos >= 0 ? srcInfo.srcPos : -1;

                try
                {
                    if (networkStarted && !IsServer && connectedPeer != null && loot != null && item != null)
                    {
                        var w = writer; w.Reset();
                        w.Put((byte)Op.LOOT_REQ_PUT);
                        PutLootId(w, loot);
                        w.Put(preferPos);
                        w.Put((uint)0);              // 不占用 _cliPendingPut，避免 Duplicate PUT
                        WriteItemSnapshot(w, item);
                        connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
                    }
                }
                catch { }

                // 本地立刻清掉临时实例，防止“幽灵物品”
                try { item.Detach(); } catch { }
                try { UnityEngine.Object.Destroy(item.gameObject); } catch { }

                // 请求刷新容器状态
                try
                {
                    var lv = Duckov.UI.LootView.Instance;
                    var inv = lv ? lv.TargetInventory : null;
                    if (inv) Client_RequestLootState(inv);
                }
                catch { }
            }

            // 2) 容器内“重排/换位”：有标记则直接 PUT 回目标格
            if (_cliPendingReorder.TryGetValue(token, out var reo))
            {
                _cliPendingReorder.Remove(token);
                Client_SendLootPutRequest(reo.inv, newItem, reo.pos);
                return;
            }

            // 3) 目标是装备槽：尝试直插或交换；失败则拒绝（放回来源容器）
            if (dest.slot != null)
            {
                ItemStatsSystem.Item victim = null;
                try { victim = dest.slot.Content; } catch { }

                if (victim != null)
                {
                    _cliSwapByVictim[victim] = (newItem, null, -1, dest.slot);
                    var srcLoot = dest.srcLoot ?? (Duckov.UI.LootView.Instance ? Duckov.UI.LootView.Instance.TargetInventory : null);
                    Client_SendLootPutRequest(srcLoot, victim, dest.srcPos);
                    return;
                }
                else
                {
                    try
                    {
                        if (dest.slot.CanPlug(newItem) && dest.slot.Plug(newItem, out var _))
                            return; // 穿戴成功
                    }
                    catch { }

                    // 插槽不兼容/失败：拒绝并放回
                    PutBackToSource_NoTrack(newItem, dest);
                    return;
                }
            }

            // 4) 目标是具体背包：AddAt/合并/普通加入；失败则拒绝并放回
            if (dest.inv != null)
            {
                ItemStatsSystem.Item victim = null;
                try { if (dest.pos >= 0) victim = dest.inv.GetItemAt(dest.pos); } catch { }

                if (dest.pos >= 0 && victim != null)
                {
                    _cliSwapByVictim[victim] = (newItem, dest.inv, dest.pos, null);
                    var srcLoot = dest.srcLoot ?? (Duckov.UI.LootView.Instance ? Duckov.UI.LootView.Instance.TargetInventory : null);
                    Client_SendLootPutRequest(srcLoot, victim, dest.srcPos);
                    return;
                }

                try { if (dest.pos >= 0 && dest.inv.AddAt(newItem, dest.pos)) return; } catch { }
                try { if (global::ItemUtilities.AddAndMerge(dest.inv, newItem, UnityEngine.Mathf.Max(0, dest.pos))) return; } catch { }
                try { if (dest.inv.AddItem(newItem)) return; } catch { }

                // 背包放不下：拒绝并放回来源容器（绝不落地）
                PutBackToSource_NoTrack(newItem, dest);
                return;
            }

            // 5) 未指定目的地：尝试主背包；失败则拒绝并放回
            var mc = global::LevelManager.Instance ? global::LevelManager.Instance.MainCharacter : null;
            var backpack = mc ? (mc.CharacterItem != null ? mc.CharacterItem.Inventory : null) : null;

            if (backpack != null)
            {
                try { if (global::ItemUtilities.AddAndMerge(backpack, newItem, 0)) return; } catch { }
                try { if (backpack.AddItem(newItem)) return; } catch { }
            }

            // 主背包也塞不进：拒绝并放回
            PutBackToSource_NoTrack(newItem, dest);
        }

        private static void Client_ApplyLootVisibility(Dictionary<int, bool> vis)
        {
            try
            {
                var core = Duckov.Scenes.MultiSceneCore.Instance;
                if (core == null || vis == null) return;

                foreach (var kv in vis)
                    core.inLevelData[kv.Key] = kv.Value; // 没有就加，有就覆盖

                // 刷新当前场景已存在的 LootBoxLoader 显示
                var loaders = UnityEngine.Object.FindObjectsOfType<Duckov.Utilities.LootBoxLoader>(true);
                foreach (var l in loaders)
                {
                    try
                    {
                        int k = ModBehaviour.Instance.ComputeLootKey(l.transform);
                        if (vis.TryGetValue(k, out bool on))
                            l.gameObject.SetActive(on);
                    }
                    catch { }
                }
            }
            catch { }
        }


        // Mod.cs
        void Server_SendAiSeeds(NetPeer target = null)
        {
            if (!IsServer) return;

            aiRootSeeds.Clear();
            // 场景种子：时间戳 XOR Unity 随机
            sceneSeed = Environment.TickCount ^ UnityEngine.Random.Range(int.MinValue, int.MaxValue);

            var roots = UnityEngine.Object.FindObjectsOfType<CharacterSpawnerRoot>(true);

            // 先算出待发送的 (id,seed) 对；对每个 root 同时加入 “主ID(可能用guid)” 和 “兼容ID(强制忽略guid)”
            var pairs = new System.Collections.Generic.List<(int id, int seed)>(roots.Length * 2);
            foreach (var r in roots)
            {
                int idA = StableRootId(r);         // 现有策略：SpawnerGuid!=0 就用 guid，否则哈希
                int idB = StableRootId_Alt(r);     // 兼容策略：强制忽略 guid

                int seed = DeriveSeed(sceneSeed, idA);
                aiRootSeeds[idA] = seed;           // 主机本地记录（可用于调试）

                pairs.Add((idA, seed));
                if (idB != idA) pairs.Add((idB, seed));  // 双映射，客户端无论算到哪条 id 都能命中
            }

            var w = writer; w.Reset();
            w.Put((byte)Op.AI_SEED_SNAPSHOT);
            w.Put(sceneSeed);
            w.Put(pairs.Count);                     // 注意：这里是 “(id,seed) 对”的总数

            foreach (var pr in pairs)
            {
                w.Put(pr.id);
                w.Put(pr.seed);
            }

            if (target == null) BroadcastReliable(w);
            else target.Send(w, DeliveryMethod.ReliableOrdered);

            Debug.Log($"[AI-SEED] 已发送 {pairs.Count} 条 Root 映射（原 Root 数={roots.Length}）目标={(target == null ? "ALL" : target.EndPoint.ToString())}");
        }

        public int StableRootId_Alt(CharacterSpawnerRoot r)
        {
            if (r == null) return 0;

            // 不看 SpawnerGuid，强制用 场景索引 + 名称 + 量化坐标
            int sceneIndex = -1;
            try
            {
                var fi = typeof(CharacterSpawnerRoot).GetField("relatedScene", BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null) sceneIndex = (int)fi.GetValue(r);
            }
            catch { }
            if (sceneIndex < 0)
                sceneIndex = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;

            Vector3 p = r.transform.position;
            int qx = Mathf.RoundToInt(p.x * 10f);
            int qy = Mathf.RoundToInt(p.y * 10f);
            int qz = Mathf.RoundToInt(p.z * 10f);

            string key = $"{sceneIndex}:{r.name}:{qx},{qy},{qz}";
            return StableHash(key);
        }


        void HandleAiSeedSnapshot(NetPacketReader r)
        {
            sceneSeed = r.GetInt();
            aiRootSeeds.Clear();
            int n = r.GetInt();
            for (int i = 0; i < n; i++)
            {
                int id = r.GetInt();
                int seed = r.GetInt();
                aiRootSeeds[id] = seed;
            }
            Debug.Log($"[AI-SEED] 收到 {n} 个 Root 的种子");
        }

        public int StableHash(string s)
        {
            unchecked { uint h = 2166136261; for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; } return (int)h; }
        }
        public string TransformPath(Transform t)
        {
            var stack = new System.Collections.Generic.Stack<string>();
            while (t != null) { stack.Push(t.name); t = t.parent; }
            return string.Join("/", stack);
        }

        public int DeriveSeed(int a, int b)
        {
            unchecked { uint h = 2166136261; h ^= (uint)a; h *= 16777619; h ^= (uint)b; h *= 16777619; return (int)h; }
        }

        // —— 冻结AI（生成后一键静止）——
        public void TryFreezeAI(CharacterMainControl cmc)
        {
            if (!cmc) return;

            if (!IsRealAI(cmc)) return;

            var all = UnityEngine.Object.FindObjectsOfType<AICharacterController>(true);
            foreach (var aic in all)
            {
                if (!aic) continue;
                aic.enabled = false;
            }
            var all1 = UnityEngine.Object.FindObjectsOfType<AI_PathControl>(true);
            foreach (var aic in all1)
            {
                if (!aic) continue;
                aic.enabled = false;
            }
            var all2 = UnityEngine.Object.FindObjectsOfType<NodeCanvas.StateMachines.FSMOwner>(true);
            foreach (var aic in all2)
            {
                if (!aic) continue;
                aic.enabled = false;
            }
            var all3 = UnityEngine.Object.FindObjectsOfType<NodeCanvas.Framework.Blackboard>(true);
            foreach (var aic in all3)
            {
                if (!aic) continue;
                aic.enabled = false;
            }
        }

        public void RegisterAi(int aiId, CharacterMainControl cmc)
        {
            if (!IsRealAI(cmc)) return;
            aiById[aiId] = cmc;

            float pendCur = -1f, pendMax = -1f;
            if (_cliPendingAiHealth.TryGetValue(aiId, out var pc)) { pendCur = pc; _cliPendingAiHealth.Remove(aiId); }
            if (_cliPendingAiMax.TryGetValue(aiId, out var pm)) { pendMax = pm; _cliPendingAiMax.Remove(aiId); }

            var h = cmc.Health;
            if (h)
            {
                if (pendMax > 0f)
                {
                    _cliAiMaxOverride[h] = pendMax;
                    try { FI_defaultMax?.SetValue(h, Mathf.RoundToInt(pendMax)); } catch { }
                    try { FI_lastMax?.SetValue(h, -12345f); } catch { }
                    try { h.OnMaxHealthChange?.Invoke(h); } catch { }
                }

                if (pendCur >= 0f || pendMax > 0f)
                {
                    float applyMax = (pendMax > 0f) ? pendMax : h.MaxHealth;
                    ForceSetHealth(h, applyMax, Mathf.Max(0f, pendCur >= 0f ? pendCur : h.CurrentHealth), ensureBar: true);
                }
            }

            if (IsServer && cmc)
                Server_BroadcastAiLoadout(aiId, cmc);

            if (!IsServer && cmc)
            {
                var follower = cmc.GetComponent<NetAiFollower>();
                if (!follower) follower = cmc.gameObject.AddComponent<NetAiFollower>();

                // 客户端兴趣圈可见性
                if (!cmc.GetComponent<NetAiVisibilityGuard>())
                    cmc.gameObject.AddComponent<NetAiVisibilityGuard>();

                try
                {
                    var tag = cmc.GetComponent<NetAiTag>();
                    if (tag == null) tag = cmc.gameObject.AddComponent<NetAiTag>();
                    if (tag.aiId != aiId) tag.aiId = aiId;
                }
                catch { }

                if (!cmc.GetComponent<RemoteReplicaTag>()) cmc.gameObject.AddComponent<RemoteReplicaTag>();

                if (_pendingAiAnims.TryGetValue(aiId, out var st))
                {
                    if (!follower) follower = cmc.gameObject.AddComponent<NetAiFollower>();
                    follower.SetAnim(st.speed, st.dirX, st.dirY, st.hand, st.gunReady, st.dashing);
                    _pendingAiAnims.Remove(aiId);
                }
            }

            // 消化 pending（装备/武器/脸/模型名/图标/是否显示名）
            if (pendingAiLoadouts.TryGetValue(aiId, out var data))
            {
                pendingAiLoadouts.Remove(aiId);
                Client_ApplyAiLoadout(aiId, data.equips, data.weapons, data.faceJson, data.modelName, data.iconType, data.showName, data.displayName).Forget();

            }
        }

        private List<EquipmentSyncData> GetLocalAIEquipment(CharacterMainControl cmc)
        {
            var equipmentList = new List<EquipmentSyncData>();
            var equipmentController = cmc?.EquipmentController;
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
                    Debug.LogError($"获取槽位 {slotNames[i]} 时发生错误: {ex.Message}");
                }
            }

            return equipmentList;
        }


        public void Server_BroadcastAiLoadout(int aiId, CharacterMainControl cmc)
        {
            if (!IsServer || cmc == null) return;

            writer.Reset();
            writer.Put((byte)Op.AI_LOADOUT_SNAPSHOT);
            writer.Put(AI_LOADOUT_VER); // v4
            writer.Put(aiId);

            // ---- 装备（5 槽）----
            var eqList = GetLocalAIEquipment(cmc);   // 新方法，已枚举 armor/helmat/faceMask/backpack/headset
            writer.Put(eqList.Count);
            foreach (var eq in eqList)
            {
                writer.Put(eq.SlotHash);

                // 线上的老协议依然是 int tid，这里从 string ItemId 安全转换
                int tid = 0;
                if (!string.IsNullOrEmpty(eq.ItemId))
                    int.TryParse(eq.ItemId, out tid);

                writer.Put(tid);
            }

            // ---- 武器 ----
            var listW = new List<(int slot, int tid)>();
            var gun = cmc.GetGun();
            var melee = cmc.GetMeleeWeapon();
            if (gun != null) listW.Add(((int)gun.handheldSocket, gun.Item ? gun.Item.TypeID : 0));
            if (melee != null) listW.Add(((int)melee.handheldSocket, melee.Item ? melee.Item.TypeID : 0));
            writer.Put(listW.Count);
            foreach (var p in listW) { writer.Put(p.slot); writer.Put(p.tid); }

            // ---- 脸 JSON（主机权威）----
            string faceJson = null;
            //try
            //{
            //    var preset = cmc.characterPreset;
            //    if (preset)
            //    {
            //        if (FR_UsePlayerPreset(preset))
            //        {
            //            var data = LevelManager.Instance.CustomFaceManager.LoadMainCharacterSetting();
            //            faceJson = JsonUtility.ToJson(data);
            //        }
            //        else
            //        {
            //            var fp = FR_FacePreset(preset);
            //            if (fp != null) faceJson = JsonUtility.ToJson(fp.settings);
            //        }
            //    }
            //}
            //catch { }
            writer.Put(!string.IsNullOrEmpty(faceJson));
            if (!string.IsNullOrEmpty(faceJson)) writer.Put(faceJson);


            // ---- 模型名 + 图标类型 + showName(主机裁决) ----
            string modelName = NormalizePrefabName(cmc.characterModel ? cmc.characterModel.name : null);

            int iconType = 0;
            bool showName = false;
            try
            {
                var pr = cmc.characterPreset;
                if (pr)
                {
                    var e = (global::CharacterIconTypes)iconType;
                    // 1) 若是 none，尝试用本地预设再取一次（有些预设在运行时被填充）
                    if (e == global::CharacterIconTypes.none && pr.GetCharacterIcon() != null)
                        iconType = (int)FR_IconType(pr);

                    // 2) 对 boss / elete 强制 showName=true，避免客户端再兜底
                    e = (global::CharacterIconTypes)iconType;
                    if (!showName && (e == global::CharacterIconTypes.boss || e == global::CharacterIconTypes.elete))
                        showName = true;
                }
            }
            catch { /* 忽略兜底异常 */ }

            writer.Put(!string.IsNullOrEmpty(modelName));
            if (!string.IsNullOrEmpty(modelName)) writer.Put(modelName);
            writer.Put(iconType);
            writer.Put(showName); // v4 字段

            // v5：名字文本（主机裁决）
            string displayName = null;
            try
            {
                var preset = cmc.characterPreset;
                if (preset) displayName = preset.Name; // 或者你自己的名字来源
            }
            catch { }

            writer.Put(!string.IsNullOrEmpty(displayName)); // hasName
            if (!string.IsNullOrEmpty(displayName))
                writer.Put(displayName);


            if (LogAiLoadoutDebug)
                Debug.Log($"[AI-SEND] ver={AI_LOADOUT_VER} aiId={aiId} model='{modelName}' icon={iconType} showName={showName}");

            BroadcastReliable(writer);

            if (iconType == (int)global::CharacterIconTypes.none)
                Server_TryRebroadcastIconLater(aiId, cmc);
        }

        private UniTask Client_ApplyAiLoadout(
    int aiId,
    List<(int slot, int tid)> equips,
    List<(int slot, int tid)> weapons,
    string faceJson,
    string modelName,
    int iconType,
    bool showName)
        {
            return Client_ApplyAiLoadout(
                aiId, equips, weapons, faceJson, modelName, iconType, showName, null);
        }

        private async UniTask Client_ApplyAiLoadout(
           int aiId,
           List<(int slot, int tid)> equips,
           List<(int slot, int tid)> weapons,
           string faceJson,
           string modelName,
           int iconType,
           bool showName,
           string displayNameFromHost)
        {
            if (!aiById.TryGetValue(aiId, out var cmc) || !cmc) return;

            // 1) 必要时切换模型（保持你原来的逻辑）
            CharacterModel prefab = null;
            if (!string.IsNullOrEmpty(modelName))
                prefab = FindCharacterModelByName_Any(modelName);
            if (!prefab)
            {
                try { var pr = cmc.characterPreset; if (pr) prefab = FR_CharacterModel(pr); } catch { }
            }

            try
            {
                var cur = cmc.characterModel;
                string curName = NormalizePrefabName(cur ? cur.name : null);
                string tgtName = NormalizePrefabName(prefab ? prefab.name : null);
                if (prefab && !string.Equals(curName, tgtName, StringComparison.OrdinalIgnoreCase))
                {
                    var inst = UnityEngine.Object.Instantiate(prefab);
                    if (LogAiLoadoutDebug) Debug.Log($"[AI-APPLY] aiId={aiId} SetCharacterModel -> '{tgtName}' (cur='{curName}')");
                    cmc.SetCharacterModel(inst);
                }
            }
            catch { }

            // 等待模型就绪
            var model = cmc.characterModel;
            int guard = 0;
            while (!model && guard++ < 120) { await UniTask.Yield(); model = cmc.characterModel; }
            if (!model) return;

            // 2) 名字 & 图标：以主机为主，客户端兜底 + 对特殊类型强制显示名字
            try
            {
                // 仅为了生态一致性，可选地把字段回写到 preset；但展示完全不用它
                var preset = cmc.characterPreset;
                if (preset)
                {
                    try { FR_IconType(preset) = (global::CharacterIconTypes)iconType; } catch { }
                    try { preset.showName = showName; } catch { }
                }

                // 1) 通过统一样式解析枚举 → Sprite（不从本地 preset 拿）
                var sprite = ResolveIconSprite(iconType);

                // UIStyle 可能尚未 ready；若是 null，延迟几帧重试兜底
                int tries = 0;
                while (sprite == null && tries++ < 5)
                {
                    await UniTask.Yield();
                    sprite = ResolveIconSprite(iconType);
                }

                // 2) 名字就是主机下发的文本；不做本地推导
                string displayName = showName ? displayNameFromHost : null;

                await RefreshNameIconWithRetries(cmc, iconType, showName, displayNameFromHost);

                if (LogAiLoadoutDebug)
                    Debug.Log($"[AI-APPLY] aiId={aiId} icon={(CharacterIconTypes)iconType} showName={showName} name='{displayName ?? "(null)"}'");
                Debug.Log($"[NOW AI] aiId={aiId} icon={Traverse.Create(cmc.characterPreset).Field<CharacterIconTypes>("characterIconType").Value} showName={showName} name='{Traverse.Create(cmc.characterPreset).Field<string>("nameKey").Value ?? "(null)"}'");
            }
            catch { }

            // 3) 服装（保持你原来的逻辑）
            foreach (var (slotHash, typeId) in equips)
            {
                if (typeId <= 0) continue;

                var item = await COOPManager.GetItemAsync(typeId);
                if (!item) continue;

                if (slotHash == CharacterEquipmentController.armorHash || slotHash == 100)
                    COOPManager.ChangeArmorModel(model, item);
                else if (slotHash == CharacterEquipmentController.helmatHash || slotHash == 200)
                    COOPManager.ChangeHelmatModel(model, item);
                else if (slotHash == CharacterEquipmentController.faceMaskHash || slotHash == 300)
                    COOPManager.ChangeFaceMaskModel(model, item);
                else if (slotHash == CharacterEquipmentController.backpackHash || slotHash == 400)
                    COOPManager.ChangeBackpackModel(model, item);
                else if (slotHash == CharacterEquipmentController.headsetHash || slotHash == 500)
                    COOPManager.ChangeHeadsetModel(model, item);
            }

            // 4)（如果你原来这里还有其它步骤，保持不动）

            // 5) 武器 —— ★ 修复“创建 pickup agent 失败，已有 agent”
            // 先清三处，再等一帧，让 Destroy 真正生效
            COOPManager.ChangeWeaponModel(model, null, HandheldSocketTypes.normalHandheld);
            COOPManager.ChangeWeaponModel(model, null, HandheldSocketTypes.meleeWeapon);
            COOPManager.ChangeWeaponModel(model, null, HandheldSocketTypes.leftHandSocket);
            await UniTask.NextFrame(); // 关键：等一帧等待销毁完成

            foreach (var (slotHash, typeId) in weapons)
            {
                if (typeId <= 0) continue;

                var item = await COOPManager.GetItemAsync(typeId);
                if (!item) continue;

                // 解析插槽：未知值统一右手
                HandheldSocketTypes socket = Enum.IsDefined(typeof(HandheldSocketTypes), slotHash)
                    ? (HandheldSocketTypes)slotHash
                    : HandheldSocketTypes.normalHandheld;

                // —— 在挂载前，确保 Item 自身没有残留 ActiveAgent —— 
                try
                {
                    var ag = item.ActiveAgent;
                    if (ag && ag.gameObject) UnityEngine.Object.Destroy(ag.gameObject);
                }
                catch { /* ignore */ }
                try { item.Detach(); } catch { /* ignore */ }

                // 保险：目标槽再清一次，并等到帧尾
                COOPManager.ChangeWeaponModel(model, null, socket);
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);

                // 尝试挂载，若仍因“已有 agent”失败，则兜底重试一次
                try
                {
                    COOPManager.ChangeWeaponModel(model, item, socket);
                }
                catch (Exception e)
                {
                    var msg = e.Message ?? string.Empty;
                    if (msg.Contains("已有agent") || msg.IndexOf("pickup agent", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // 再杀一次残留 + 再清 + 再等一帧，然后重试一次
                        try
                        {
                            var ag2 = item.ActiveAgent;
                            if (ag2 && ag2.gameObject) UnityEngine.Object.Destroy(ag2.gameObject);
                        }
                        catch { }
                        try { item.Detach(); } catch { }

                        COOPManager.ChangeWeaponModel(model, null, socket);
                        await UniTask.NextFrame();

                        COOPManager.ChangeWeaponModel(model, item, socket);
                    }
                    else
                    {
                        throw; // 其它错误别吞
                    }
                }
            }

            EnsureMagicBlendBound(cmc);

            if (!string.IsNullOrEmpty(faceJson)) ApplyFaceJsonToModel(model, faceJson);
        }


        public void Server_BroadcastAiTransforms()
        {
            if (!IsServer || aiById.Count == 0) return;

            writer.Reset();
            writer.Put((byte)Op.AI_TRANSFORM_SNAPSHOT);
            // 统计有效数量
            int cnt = 0;
            foreach (var kv in aiById) if (kv.Value) cnt++;
            writer.Put(cnt);
            foreach (var kv in aiById)
            {
                var cmc = kv.Value; if (!cmc) continue;
                var t = cmc.transform;
                writer.Put(kv.Key);                 // aiId
                writer.PutV3cm(t.position);         // 压缩位置
                Vector3 fwd = cmc.characterModel.transform.rotation * Vector3.forward;
                writer.PutDir(fwd);
            }
            BroadcastReliable(writer);
        }



        private CharacterMainControl TryAutoBindAi(int aiId, Vector3 snapPos)
        {
            float best = 30f; // 原 5f -> 放宽，必要时可调到 40f
            CharacterMainControl bestCmc = null;

            var all = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>(true);
            foreach (var c in all)
            {
                if (!c || LevelManager.Instance.MainCharacter == c) continue;
                if (aiById.ContainsValue(c)) continue;

                Vector2 a = new Vector2(c.transform.position.x, c.transform.position.z);
                Vector2 b = new Vector2(snapPos.x, snapPos.z);
                float d = Vector2.Distance(a, b);

                if (d < best) { best = d; bestCmc = c; }
            }

            if (bestCmc != null)
            {
                RegisterAi(aiId, bestCmc);        // ↓ 第②步里我们会让 RegisterAi 在客户端同时挂 Follower
                if (freezeAI) TryFreezeAI(bestCmc);
            }
            return bestCmc;
        }


        private void Client_ForceFreezeAllAI()
        {
            if (!networkStarted || IsServer) return;
            var all = UnityEngine.Object.FindObjectsOfType<AICharacterController>(true);
            foreach (var aic in all)
            {
                if (!aic) continue;
                aic.enabled = false;
                var cmc = aic.GetComponentInParent<CharacterMainControl>();
                if (cmc) TryFreezeAI(cmc); // 会关 BehaviourTreeOwner + NavMeshAgent + AICtrl
            }
        }

        public int NextAiSerial(int rootId)
        {
            if (!_aiSerialPerRoot.TryGetValue(rootId, out var n)) n = 0;
            n++;
            _aiSerialPerRoot[rootId] = n;
            return n;
        }

        public void ResetAiSerials() => _aiSerialPerRoot.Clear();

        public void MarkAiSceneReady() => _aiSceneReady = true;


        void ApplyAiTransform(int aiId, Vector3 p, Vector3 f)
        {
            if (!aiById.TryGetValue(aiId, out var cmc) || !cmc)
            {
                cmc = TryAutoBindAiWithBudget(aiId, p); // 新版：窄范围 + 限频
                if (!cmc) return; // 等下一帧
            }
            if (!IsRealAI(cmc)) return;

            var follower = cmc.GetComponent<NetAiFollower>() ?? cmc.gameObject.AddComponent<NetAiFollower>();
            follower.SetTarget(p, f);
        }

        private CharacterMainControl TryAutoBindAiWithBudget(int aiId, Vector3 snapPos)
        {

            // 1) 限频：同一 aiId 在冷却期内直接跳过
            if (_lastAutoBindTryTime.TryGetValue(aiId, out var last) && (Time.time - last) < AUTOBIND_COOLDOWN)
                return null;
            _lastAutoBindTryTime[aiId] = Time.time;

            // 2) 近场搜索：用 OverlapSphere 缩小枚举规模
            CharacterMainControl best = null;
            float bestSqr = float.MaxValue;

            var cols = Physics.OverlapSphere(snapPos, AUTOBIND_RADIUS, AUTOBIND_LAYERMASK, AUTOBIND_QTI);
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (!c) continue;

                var cmc = c.GetComponentInParent<CharacterMainControl>();
                if (!cmc) continue;
                if (LevelManager.Instance && LevelManager.Instance.MainCharacter == cmc) continue; // 跳过玩家本体
                if (!cmc.gameObject.activeInHierarchy) continue;
                if (!IsRealAI(cmc)) continue;

                if (aiById.ContainsValue(cmc)) continue; // 已被别的 aiId 占用

                float d2 = (cmc.transform.position - snapPos).sqrMagnitude;
                if (d2 < bestSqr) { bestSqr = d2; best = cmc; }
            }

            if (best != null)
            {
                if (!IsRealAI(best)) return null;

                RegisterAi(aiId, best);                 // 已有：登记 &（在客户端）自动挂 NetAiFollower
                if (freezeAI) TryFreezeAI(best);        // 你已有的“冻结”辅助（可选）
                return best;
            }

            // 3) 罕见兜底：偶尔扫一次 NetAiTag 做精确匹配（低频触发）
            if ((Time.frameCount % 20) == 0) // 大约每 20 帧才做一次全局查看
            {
                var tags = UnityEngine.Object.FindObjectsOfType<NetAiTag>(true);
                for (int i = 0; i < tags.Length; i++)
                {
                    var tag = tags[i];
                    if (!tag || tag.aiId != aiId) continue;
                    var cmc = tag.GetComponentInParent<CharacterMainControl>();
                    if (cmc && !aiById.ContainsValue(cmc))
                    {
                        RegisterAi(aiId, cmc);
                        if (freezeAI) TryFreezeAI(cmc);
                        return cmc;
                    }
                }
            }

            return null; // 这帧没命中，就等下一帧/下一次快照
        }

        private void Server_BroadcastAiAnimations()
        {
            if (!IsServer || aiById == null || aiById.Count == 0) return;

            var list = new List<(int id, AiAnimState st)>(aiById.Count);
            foreach (var kv in aiById)
            {
                int id = kv.Key;
                var cmc = kv.Value;
                if (!cmc) continue;

                // ① 必须是真正的 AI，且存活
                if (!IsRealAI(cmc)) continue;      // 你工程里已有这个工具方法

                // ② GameObject/组件必须处于激活状态
                if (!cmc.gameObject.activeInHierarchy || !cmc.enabled) continue;

                var magic = cmc.GetComponentInChildren<CharacterAnimationControl_MagicBlend>(true);
                var anim = magic ? magic.animator : cmc.GetComponentInChildren<Animator>(true);
                if (!anim || !anim.isActiveAndEnabled || !anim.gameObject.activeInHierarchy) continue;

                var st = new AiAnimState
                {
                    speed = anim.GetFloat(Animator.StringToHash("MoveSpeed")),
                    dirX = anim.GetFloat(Animator.StringToHash("MoveDirX")),
                    dirY = anim.GetFloat(Animator.StringToHash("MoveDirY")),
                    hand = anim.GetInteger(Animator.StringToHash("HandState")),
                    gunReady = anim.GetBool(Animator.StringToHash("GunReady")),
                    dashing = anim.GetBool(Animator.StringToHash("Dashing")),
                };
                list.Add((id, st));
            }
            if (list.Count == 0) return;

            // —— 发送（保持你原来的分包逻辑）——
            const DeliveryMethod METHOD = DeliveryMethod.Unreliable;
            int maxSingle = 1200;
            try { maxSingle = (connectedPeer != null) ? connectedPeer.GetMaxSinglePacketSize(METHOD) : maxSingle; } catch { }
            const int HEADER = 16;
            const int ENTRY = 24;

            int budget = Math.Max(256, maxSingle - HEADER);
            int perPacket = Math.Max(1, budget / ENTRY);

            for (int i = 0; i < list.Count; i += perPacket)
            {
                int n = Math.Min(perPacket, list.Count - i);

                writer.Reset();
                writer.Put((byte)Op.AI_ANIM_SNAPSHOT);
                writer.Put(n);
                for (int j = 0; j < n; ++j)
                {
                    var e = list[i + j];
                    writer.Put(e.id);
                    writer.Put(e.st.speed);
                    writer.Put(e.st.dirX);
                    writer.Put(e.st.dirY);
                    writer.Put(e.st.hand);
                    writer.Put(e.st.gunReady);
                    writer.Put(e.st.dashing);
                }
                netManager.SendToAll(writer, METHOD);
            }
        }



        private bool Client_ApplyAiAnim(int id, AiAnimState st)
        {
            if (aiById.TryGetValue(id, out var cmc) && cmc)
            {
                if (!IsRealAI(cmc)) return false;  // 保险
                // 确保 AI 代理上有 NetAiFollower 与 RemoteReplicaTag（禁用本地 MagicBlend.Update）
                var follower = cmc.GetComponent<NetAiFollower>();
                if (!follower) follower = cmc.gameObject.AddComponent<NetAiFollower>();
                if (!cmc.GetComponent<RemoteReplicaTag>()) cmc.gameObject.AddComponent<RemoteReplicaTag>();

                follower.SetAnim(st.speed, st.dirX, st.dirY, st.hand, st.gunReady, st.dashing);
                return true;
            }
            return false;
        }

        public bool IsRealAI(CharacterMainControl cmc)
        {
            if (cmc == null) return false;

            // 过滤主角
            if (cmc == CharacterMainControl.Main)
                return false;

            if (cmc.Team == Teams.player)
            {
                return false;
            }

            var lm = LevelManager.Instance;
            if (lm != null)
            {
                if (cmc == lm.PetCharacter) return false;
                if (lm.PetProxy != null && cmc.gameObject == lm.PetProxy.gameObject) return false;
            }

            // 过滤远程玩家（remoteCharacters 管理的对象）
            foreach (var go in remoteCharacters.Values)
            {
                if (go != null && cmc.gameObject == go)
                    return false;
            }
            foreach (var go in clientRemoteCharacters.Values)
            {
                if (go != null && cmc.gameObject == go)
                    return false;
            }

            return true;
        }


        static string NormalizePrefabName(string n)
        {
            if (string.IsNullOrEmpty(n)) return n;
            n = n.Trim();
            const string clone = "(Clone)";
            if (n.EndsWith(clone)) n = n.Substring(0, n.Length - clone.Length).Trim();
            return n;
        }

        static CharacterModel FindCharacterModelByName_Any(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            name = NormalizePrefabName(name);

            // A. 从所有已加载的“资源对象”里找 prefab（不隶属任何 Scene）
            //    注意：FindObjectsOfTypeAll 能拿到隐藏对象/资产，但要过滤掉场景实例
            foreach (var m in Resources.FindObjectsOfTypeAll<CharacterModel>())
            {
                if (!m) continue;
                if (m.gameObject.scene.IsValid()) continue; // 这是场景实例，跳过
                if (string.Equals(NormalizePrefabName(m.name), name, StringComparison.OrdinalIgnoreCase))
                    return m;
            }

            // B. 再从 Resources 目录里加载一次（项目若没用 Addressables，这步很有用）
            try
            {
                foreach (var m in Resources.LoadAll<CharacterModel>(""))
                {
                    if (!m) continue;
                    if (string.Equals(NormalizePrefabName(m.name), name, StringComparison.OrdinalIgnoreCase))
                        return m;
                }
            }
            catch { /* 项目可能没放 Resources；忽略 */ }

            // C. 最后才扫描场景中的“已存在实例”（极端兜底）
            foreach (var m in UnityEngine.GameObject.FindObjectsOfType<CharacterModel>())
            {
                if (!m) continue;
                if (string.Equals(NormalizePrefabName(m.name), name, StringComparison.OrdinalIgnoreCase))
                    return m;
            }

            return null;
        }

        // ---------- 工具：把主机下发的脸 JSON 套到模型 ----------
        public static void ApplyFaceJsonToModel(CharacterModel model, string faceJson)
        {
            if (model == null || string.IsNullOrEmpty(faceJson)) return;
            try
            {
                CustomFaceSettingData data;
                bool ok = CustomFaceSettingData.JsonToData(faceJson, out data);
                if (!ok) data = JsonUtility.FromJson<CustomFaceSettingData>(faceJson);
                model.SetFaceFromData(data);
            }
            catch { /* 忽略异常避免中断 */ }
        }

        public void ReapplyFaceIfKnown(CharacterMainControl cmc)
        {

            if (!cmc || IsServer) return;
            int aiId = -1;
            foreach (var kv in aiById) { if (kv.Value == cmc) { aiId = kv.Key; break; } }
            if (aiId < 0) return;

            if (_aiFaceJsonById.TryGetValue(aiId, out var json) && !string.IsNullOrEmpty(json))
                ApplyFaceJsonToModel(cmc.characterModel, json);
        }


        private readonly HashSet<int> _nameIconSealed = new HashSet<int>();

        // 进入新关卡时清空封存（你已有 Level 初始化回调就放那里）
        private void Client_ResetNameIconSeal_OnLevelInit()
        {
            if (!IsServer) _nameIconSealed.Clear();
            if (IsServer) return;
            foreach (var tag in GameObject.FindObjectsOfType<NetAiTag>())
            {
                var cmc = tag ? tag.GetComponent<CharacterMainControl>() : null;
                if (!cmc) { Destroy(tag); continue; }
                if (!IsRealAI(cmc)) Destroy(tag);
            }
        }

        static UnityEngine.Sprite ResolveIconSprite(int iconType)
        {
            switch ((global::CharacterIconTypes)iconType)
            {
                case global::CharacterIconTypes.none: return null;
                case global::CharacterIconTypes.elete: return Duckov.Utilities.GameplayDataSettings.UIStyle.EleteCharacterIcon;
                case global::CharacterIconTypes.pmc: return Duckov.Utilities.GameplayDataSettings.UIStyle.PmcCharacterIcon;
                case global::CharacterIconTypes.boss: return Duckov.Utilities.GameplayDataSettings.UIStyle.BossCharacterIcon;
                case global::CharacterIconTypes.merchant: return Duckov.Utilities.GameplayDataSettings.UIStyle.MerchantCharacterIcon;
                case global::CharacterIconTypes.pet: return Duckov.Utilities.GameplayDataSettings.UIStyle.PetCharacterIcon;
                default: return null;
            }
        }

        // —— 客户端：确保血条后，多帧重试刷新图标与名字 —— 
        private async UniTask RefreshNameIconWithRetries(CharacterMainControl cmc, int iconType, bool showName, string displayNameFromHost)
        {
            if (!cmc) return;

            //global::Duckov.UI.HealthBar hb1 = null;
            //foreach (var kv in aiById)
            //{
            //    if (kv.Value != null)
            //    {

            //        var preset = kv.Value.characterPreset;
            //        if (preset)
            //        {
            //            try { FR_IconType(preset) = (global::CharacterIconTypes)iconType; } catch { }
            //            try { preset.showName = showName; } catch { }
            //            try { Traverse.Create(preset).Field<string>("nameKey").Value = displayNameFromHost ?? string.Empty; } catch { }
            //        }

            //        var h1 = cmc.Health;
            //        if (h1 != null)
            //        {
            //            MethodInfo miGet1 = AccessTools.DeclaredMethod(typeof(HealthBarManager), "GetActiveHealthBar", new[] { typeof(global::Health) });
            //            MethodInfo miRefresh1 = AccessTools.DeclaredMethod(typeof(global::Duckov.UI.HealthBar), "RefreshCharacterIcon", Type.EmptyTypes);
            //            if (miGet1 != null && HealthBarManager.Instance != null && h1 != null)
            //                hb1 = (global::Duckov.UI.HealthBar)miGet1.Invoke(HealthBarManager.Instance, new object[] { h1 });

            //            Traverse.Create(hb1).Field<Image>("levelIcon").Value.sprite = null;
            //            Traverse.Create(hb1).Field<TextMeshProUGUI>("nameText").Value.text = "";

            //            Traverse.Create(hb1).Field<Image>("levelIcon").Value.gameObject.SetActive(false);
            //            Traverse.Create(hb1).Field<TextMeshProUGUI>("nameText").Value.gameObject.SetActive(false);

            //            var tag = cmc.GetComponent<NetAiTag>() ?? cmc.gameObject.AddComponent<NetAiTag>();
            //            tag.iconTypeOverride = null;
            //            tag.showNameOverride = showName
            //                || ((CharacterIconTypes)iconType == CharacterIconTypes.boss
            //                  || (CharacterIconTypes)iconType == CharacterIconTypes.elete);
            //            tag.nameOverride = string.Empty;

            //            if (hb1 != null)
            //            {
            //                miRefresh1?.Invoke(hb1, null);
            //                continue;
            //            }
            //        }
            //        else
            //        {
            //            continue;
            //        }
            //    }
            //}

            try
            {
                var preset = cmc.characterPreset;
                if (preset)
                {
                    try { FR_IconType(preset) = (global::CharacterIconTypes)iconType; } catch { }
                    try { preset.showName = showName; } catch { }
                    try { Traverse.Create(preset).Field<string>("nameKey").Value = displayNameFromHost ?? string.Empty; } catch { }
                }
            }
            catch { }

            // 2) 确保血条被请求并生成（已有 EnsureBarRoutine 可复用）
            var h = cmc.Health;

            // 3) 多帧重试拿 HealthBar 并调用私有 RefreshCharacterIcon()
            MethodInfo miGet = AccessTools.DeclaredMethod(typeof(HealthBarManager), "GetActiveHealthBar", new[] { typeof(global::Health) });
            MethodInfo miRefresh = AccessTools.DeclaredMethod(typeof(global::Duckov.UI.HealthBar), "RefreshCharacterIcon", Type.EmptyTypes);

            global::Duckov.UI.HealthBar hb = null;
            for (int i = 0; i < 30; i++) // 最多重试 ~30 帧
            {
                try
                {
                    if (miGet != null && HealthBarManager.Instance != null && h != null)
                        hb = (global::Duckov.UI.HealthBar)miGet.Invoke(HealthBarManager.Instance, new object[] { h });

                    Traverse.Create(hb).Field<Image>("levelIcon").Value.gameObject.SetActive(true);
                    Traverse.Create(hb).Field<TextMeshProUGUI>("nameText").Value.gameObject.SetActive(true);

                    Traverse.Create(hb).Field<Image>("levelIcon").Value.sprite = ResolveIconSprite(iconType);
                    Traverse.Create(hb).Field<TextMeshProUGUI>("nameText").Value.text = displayNameFromHost;

                    if (hb != null)
                    {
                        var tag = cmc.GetComponent<NetAiTag>() ?? cmc.gameObject.AddComponent<NetAiTag>();
                        tag.iconTypeOverride = iconType;
                        tag.showNameOverride = showName
                            || ((CharacterIconTypes)iconType == CharacterIconTypes.boss
                              || (CharacterIconTypes)iconType == CharacterIconTypes.elete);
                        tag.nameOverride = displayNameFromHost ?? string.Empty;

                        Debug.Log($"[AI_icon_Name 10s] {cmc.GetComponent<NetAiTag>().aiId} {cmc.characterPreset.Name} {cmc.characterPreset.GetCharacterIcon().name}");
                        break; // 成功一次即可
                    }
                }
                catch { }
            }
        }

        // —— 主机：icon 为空时，延迟一次复查并重播 —— 
        private readonly HashSet<int> _iconRebroadcastScheduled = new HashSet<int>();

        private void Server_TryRebroadcastIconLater(int aiId, CharacterMainControl cmc)
        {
            if (!IsServer || aiId == 0 || !cmc) return;
            if (!_iconRebroadcastScheduled.Add(aiId)) return; // 只安排一次

            StartCoroutine(IconRebroadcastRoutine(aiId, cmc));
        }

        private IEnumerator IconRebroadcastRoutine(int aiId, CharacterMainControl cmc)
        {
            yield return new WaitForSeconds(0.6f); // 等 UIStyle/预设就绪

            try
            {
                if (!IsServer || !cmc) yield break;

                var pr = cmc.characterPreset;
                int iconType = 0;
                bool showName = false;

                if (pr)
                {
                    try { iconType = (int)FR_IconType(pr); } catch { }
                    try
                    {
                        // 运行期很多预设会把 icon 补上：再试一次
                        if (iconType == 0 && pr.GetCharacterIcon() != null)
                            iconType = (int)FR_IconType(pr);
                    }
                    catch { }
                }

                var e = (global::CharacterIconTypes)iconType;
                if (e == global::CharacterIconTypes.boss || e == global::CharacterIconTypes.elete)
                    showName = true;

                // 现在拿到非 none 或识别为特殊类型，就再广播一遍
                if (iconType != 0 || showName)
                    Server_BroadcastAiNameIcon(aiId, cmc);
            }
            finally { _iconRebroadcastScheduled.Remove(aiId); }
        }



        /// <summary>
        /// /////////////AI血量同步//////////////AI血量同步//////////////AI血量同步//////////////AI血量同步//////////////AI血量同步////////
        /// </summary>

        public void Server_BroadcastAiHealth(int aiId, float maxHealth, float currentHealth)
        {
            if (!networkStarted || !IsServer) return;
            var w = new NetDataWriter();
            w.Put((byte)Op.AI_HEALTH_SYNC);
            w.Put(aiId);
            w.Put(maxHealth);
            w.Put(currentHealth);
            netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }



        private void Client_ApplyAiHealth(int aiId, float max, float cur)
        {
            if (IsServer) return;

            // AI 尚未注册：缓存 max/cur，等 RegisterAi 时一起冲
            if (!aiById.TryGetValue(aiId, out var cmc) || !cmc)
            {
                _cliPendingAiHealth[aiId] = cur;
                if (max > 0f) _cliPendingAiMax[aiId] = max;
                if (LogAiHpDebug) Debug.Log($"[AI-HP][CLIENT] pending aiId={aiId} max={max} cur={cur}");
                return;
            }

            var h = cmc.Health;
            if (!h) return;

            try
            {
                float prev = 0f;
                _cliLastAiHp.TryGetValue(aiId, out prev);
                _cliLastAiHp[aiId] = cur;

                float delta = prev - cur;                     // 掉血为正
                if (delta > 0.01f)
                {
                    var pos = cmc.transform.position + Vector3.up * 1.1f;
                    var di = new global::DamageInfo();
                    di.damagePoint = pos;
                    di.damageNormal = Vector3.up;
                    di.damageValue = delta;
                    // 如果运行库里有 finalDamage 字段就能显示更准的数值（A 节已经做了优先显示）
                    try { di.finalDamage = delta; } catch { }
                    LocalHitKillFx.PopDamageText(pos, di);
                }
            }
            catch { }

            // 写入/更新 Max 覆盖（只在给到有效 max 时）
            if (max > 0f)
            {
                _cliAiMaxOverride[h] = max;
                // 顺便把 defaultMaxHealth 调大，触发一次 OnMaxHealthChange（即使有 item stat，我也同步一下，保险）
                try { FI_defaultMax?.SetValue(h, Mathf.RoundToInt(max)); } catch { }
                try { FI_lastMax?.SetValue(h, -12345f); } catch { }
                try { h.OnMaxHealthChange?.Invoke(h); } catch { }
            }

            // 读一下当前 client 视角的 Max（注意：此时 get_MaxHealth 已有 Harmony 覆盖，能拿到“权威 max”）
            float nowMax = 0f; try { nowMax = h.MaxHealth; } catch { }

            // ——避免被 SetHealth() 按“旧 Max”夹住：当 cur>nowMax 时，直接反射写 _currentHealth —— 
            if (nowMax > 0f && cur > nowMax + 0.0001f)
            {
                try { FI__current?.SetValue(h, cur); } catch { }
                try { h.OnHealthChange?.Invoke(h); } catch { }
            }
            else
            {
                // 常规路径
                try { h.SetHealth(Mathf.Max(0f, cur)); } catch { try { FI__current?.SetValue(h, Mathf.Max(0f, cur)); } catch { } }
                try { h.OnHealthChange?.Invoke(h); } catch { }
            }

            // 起血条兜底
            try { h.showHealthBar = true; } catch { }
            try { h.RequestHealthBar(); } catch { }

            // 死亡则本地立即隐藏，防“幽灵AI”
            if (cur <= 0f)
            {
                try
                {
                    var ai = cmc.GetComponent<AICharacterController>();
                    if (ai) ai.enabled = false;

                    // 释放/隐藏血条
                    try
                    {
                        var miGet = AccessTools.DeclaredMethod(typeof(HealthBarManager), "GetActiveHealthBar", new[] { typeof(global::Health) });
                        var hb = miGet?.Invoke(HealthBarManager.Instance, new object[] { h }) as Duckov.UI.HealthBar;
                        if (hb != null)
                        {
                            var miRel = AccessTools.DeclaredMethod(typeof(global::Duckov.UI.HealthBar), "Release", Type.EmptyTypes);
                            if (miRel != null) miRel.Invoke(hb, null);
                            else hb.gameObject.SetActive(false);
                        }
                    }
                    catch { }

                    cmc.gameObject.SetActive(false);
                }
                catch { }


                if (_cliAiDeathFxOnce.Add(aiId))
                    Client_PlayAiDeathFxAndSfx(cmc);
            }
        }


        public void Server_OnDeadLootboxSpawned(InteractableLootbox box)
        {
            if (!IsServer || box == null) return;
            try
            {
                int lootUid = _nextLootUid++;
                var inv = box.Inventory;
                if (inv) _srvLootByUid[lootUid] = inv;

                // ★ 新增：抑制“填充期间”的 AddItem 广播
                if (inv) Server_MuteLoot(inv, 2.0f);

                writer.Reset();
                writer.Put((byte)Op.DEAD_LOOT_SPAWN);
                writer.Put(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                writer.PutV3cm(box.transform.position);
                writer.PutQuaternion(box.transform.rotation);
                netManager.SendToAll(writer, LiteNetLib.DeliveryMethod.ReliableOrdered);

                // 2) 可选：是否立刻广播整箱内容（默认不广播，等客户端真正打开时再按需请求）
                if (EAGER_BROADCAST_LOOT_STATE_ON_SPAWN)
                {
                    Server_SendLootboxState(null, box.Inventory); // 如需老行为，打开上面的开关即可
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[LOOT] Server_OnDeadLootboxSpawned failed: " + e);
            }
        }


        private void SpawnDeadLootboxAt(int aiId, int lootUid, Vector3 pos, Quaternion rot)
        {
            try
            {
                TryClientRemoveNearestAICorpse(pos, 3.0f);

                var prefab = GetDeadLootPrefabOnClient(aiId);
                if (!prefab) { Debug.LogWarning("[LOOT] DeadLoot prefab not found on client, spawn aborted."); return; }

                var go = UnityEngine.Object.Instantiate(prefab, pos, rot);
                var box = go ? go.GetComponent<InteractableLootbox>() : null;
                if (!box) return;

                var inv = box.Inventory;
                if (!inv) { Debug.LogWarning("[Client DeadLootBox Spawn] Inventory is null!"); return; }

                WorldLootPrime.PrimeIfClient(box);

                // 用主机广播的 pos 注册 posKey → inv（旧兜底仍保留）
                var dict = InteractableLootbox.Inventories;
                if (dict != null)
                {
                    int correctKey = ComputeLootKeyFromPos(pos);
                    int wrongKey = -1;
                    foreach (var kv in dict)
                        if (kv.Value == inv && kv.Key != correctKey) { wrongKey = kv.Key; break; }
                    if (wrongKey != -1) dict.Remove(wrongKey);
                    dict[correctKey] = inv;
                }

                //稳定 ID → inv
                if (lootUid >= 0) _cliLootByUid[lootUid] = inv;

                // 若快照先到，这里优先吃缓存
                if (lootUid >= 0 && _pendingLootStatesByUid.TryGetValue(lootUid, out var pack))
                {
                    _pendingLootStatesByUid.Remove(lootUid);

                    _applyingLootState = true;
                    try
                    {
                        int cap = Mathf.Clamp(pack.capacity, 1, 128);
                        inv.Loading = true;               // ★ 进入批量
                        inv.SetCapacity(cap);

                        for (int i = inv.Content.Count - 1; i >= 0; --i)
                        {
                            Item removed; inv.RemoveAt(i, out removed);
                            try { if (removed) UnityEngine.Object.Destroy(removed.gameObject); } catch { }
                        }
                        foreach (var (p, snap) in pack.Item2)
                        {
                            var item = BuildItemFromSnapshot(snap);
                            if (item) inv.AddAt(item, p);
                        }
                    }
                    finally
                    {
                        inv.Loading = false;              // ★ 结束批量
                        _applyingLootState = false;
                    }
                    WorldLootPrime.PrimeIfClient(box);
                    return; // 吃完缓存就不再发请求
                }

                // 正常路径：请求一次状态 + 超时兜底
                Client_RequestLootState(inv);
                StartCoroutine(ClearLootLoadingTimeout(inv, 1.5f));
            }
            catch (System.Exception e)
            {
                Debug.LogError("[LOOT] SpawnDeadLootboxAt failed: " + e);
            }
        }


        private GameObject GetDeadLootPrefabOnClient(int aiId)
        {
            // 1) 首选：死亡 CMC 上的 private deadLootBoxPrefab
            try
            {
                if (aiId > 0 && aiById.TryGetValue(aiId, out var cmc) && cmc)
                {
                    Debug.LogWarning($"[SpawnDeadloot] AiID:{cmc.GetComponent<NetAiTag>().aiId}");
                    if (cmc.deadLootBoxPrefab.gameObject == null)
                    {
                        Debug.LogWarning("[SPawnDead] deadLootBoxPrefab.gameObject null!");
                    }


                    if (cmc != null)
                    {
                        var obj = cmc.deadLootBoxPrefab.gameObject;
                        if (obj) return obj;
                    }
                    else
                    {
                        Debug.LogWarning("[SPawnDead] cmc is null!");
                    }
                }
            }
            catch { }

            // 2) 兜底：沿用你现有逻辑（Main 或任意 CMC）
            try
            {
                var main = CharacterMainControl.Main;
                if (main)
                {
                    var obj = main.deadLootBoxPrefab.gameObject;
                    if (obj) return obj;
                }
            }
            catch { }

            try
            {
                var any = UnityEngine.GameObject.FindObjectOfType<CharacterMainControl>();
                if (any)
                {
                    var obj = any.deadLootBoxPrefab.gameObject;
                    if (obj) return obj;
                }
            }
            catch { }
            return null;
        }
        public void Server_OnDeadLootboxSpawned(InteractableLootbox box, CharacterMainControl whoDied)
        {
            if (!IsServer || box == null) return;
            try
            {
                // 生成稳定 ID 并登记
                int lootUid = _nextLootUid++;
                var inv = box.Inventory;
                if (inv) _srvLootByUid[lootUid] = inv;

                int aiId = 0;
                if (whoDied)
                {
                    var tag = whoDied.GetComponent<NetAiTag>();
                    if (tag != null) aiId = tag.aiId;
                    if (aiId == 0) foreach (var kv in aiById) if (kv.Value == whoDied) { aiId = kv.Key; break; }
                }

                // >>> 放在 writer.Reset() 之前 <<<
                if (inv != null)
                {
                    inv.NeedInspection = true;
                    // 尝试把“这个箱子以前被搜过”的标记也清空（有的版本有这个字段）
                    try { Traverse.Create(inv).Field<bool>("hasBeenInspectedInLootBox").Value = false; } catch { }

                    // 把当前内容全部标记为“未鉴定”
                    for (int i = 0; i < inv.Content.Count; ++i)
                    {
                        var it = inv.GetItemAt(i);
                        if (it) it.Inspected = false;
                    }
                }


                // 稳定 ID
                writer.Reset();
                writer.Put((byte)Op.DEAD_LOOT_SPAWN);
                writer.Put(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                writer.Put(aiId);
                writer.Put(lootUid);                              // 稳定 ID
                writer.PutV3cm(box.transform.position);
                writer.PutQuaternion(box.transform.rotation);
                netManager.SendToAll(writer, LiteNetLib.DeliveryMethod.ReliableOrdered);

                if (EAGER_BROADCAST_LOOT_STATE_ON_SPAWN)
                    StartCoroutine(RebroadcastDeadLootStateAfterFill(box));
            }
            catch (System.Exception e)
            {
                Debug.LogError("[LOOT] Server_OnDeadLootboxSpawned failed: " + e);
            }
        }

        private System.Collections.IEnumerator RebroadcastDeadLootStateAfterFill(InteractableLootbox box)
        {
            if (!EAGER_BROADCAST_LOOT_STATE_ON_SPAWN) yield break;

            yield return null; // 给原版填充时间
            yield return null;
            if (box && box.Inventory) Server_SendLootboxState(null, box.Inventory);
        }

        public int ComputeLootKey(Transform t)
        {
            if (!t) return -1;
            var v = t.position * 10f;
            int x = Mathf.RoundToInt(v.x);
            int y = Mathf.RoundToInt(v.y);
            int z = Mathf.RoundToInt(v.z);
            return new Vector3Int(x, y, z).GetHashCode();
        }


        private void Server_PeriodicNameIconSync()
        {
            foreach (var kv in aiById)  // aiId -> cmc
            {
                int aiId = kv.Key;
                var cmc = kv.Value;
                if (!cmc) continue;

                var pr = cmc.characterPreset;
                if (!pr) continue;

                int iconType = 0;
                bool showName = false;

                try { iconType = (int)FR_IconType(pr); } catch { }
                try { showName = pr.showName; } catch { }

                var e = (global::CharacterIconTypes)iconType;
                // 老规矩：boss/elete 强制显示名字，避免客户端再兜一次
                if (!showName && (e == global::CharacterIconTypes.boss || e == global::CharacterIconTypes.elete))
                    showName = true;

                // 仅当“有图标”或“需要显示名字”时才重播，避免无意义带宽
                if (e != global::CharacterIconTypes.none || showName)
                {
                    if (LogAiLoadoutDebug)
                        UnityEngine.Debug.Log($"[AI-REBROADCAST-10s] aiId={aiId} icon={e} showName={showName}");
                    Server_BroadcastAiLoadout(aiId, cmc); // 你现有的方法，里头会把名字一起下发
                }
            }
        }

        // 客户端：强制让血条读 preset 并刷新一次名字/图标
        private void Client_PeriodicNameIconRefresh()
        {
            foreach (var kv in aiById)
            {
                var cmc = kv.Value;
                if (!cmc) continue;

                var pr = cmc.characterPreset;
                if (!pr) continue;

                int iconType = 0;
                bool showName = false;
                string displayName = null;

                try { iconType = (int)FR_IconType(pr); } catch { }
                try { showName = pr.showName; } catch { }
                try { displayName = pr.DisplayName; } catch { }

                var e = (global::CharacterIconTypes)iconType;
                if (!showName && (e == global::CharacterIconTypes.boss || e == global::CharacterIconTypes.elete))
                    showName = true;

                // 仅刷新“有图标或需要显示名字”的对象，避免白做工
                if (e == global::CharacterIconTypes.none && !showName) continue;

                // 利用你现有的多帧兜底：确保拿到 HealthBar 后反射调私有 RefreshCharacterIcon()
                RefreshNameIconWithRetries(cmc, iconType, showName, displayName).Forget();
            }
        }

        public void Server_BroadcastAiNameIcon(int aiId, CharacterMainControl cmc)
        {
            if (!networkStarted || !IsServer || aiId == 0 || !cmc) return;

            int iconType = 0;
            bool showName = false;
            string displayName = null;

            try
            {
                var pr = cmc.characterPreset;
                if (pr)
                {
                    // 读取/兜底 iconType
                    try { iconType = (int)FR_IconType(pr); } catch { }
                    try
                    {
                        if (iconType == 0 && pr.GetCharacterIcon() != null) // 运行期补上后的兜底
                            iconType = (int)FR_IconType(pr);
                    }
                    catch { }

                    // showName 按预设 + 特殊类型强制
                    try { showName = pr.showName; } catch { }
                    var e = (global::CharacterIconTypes)iconType;
                    if (!showName && (e == global::CharacterIconTypes.boss || e == global::CharacterIconTypes.elete))
                        showName = true;

                    // 名字文本用主机裁决（你之前就是从 preset.Name 拿的）
                    try { displayName = pr.Name; } catch { }
                }
            }
            catch { }
            Debug.Log($"[Server AIIcon_Name 10s] AI:{aiId} {cmc.characterPreset.Name} Icon{ (CharacterIconTypes)FR_IconType(cmc.characterPreset)}");
            var w = new NetDataWriter();
            w.Put((byte)Op.AI_NAME_ICON);
            w.Put(aiId);
            w.Put(iconType);
            w.Put(showName);
            w.Put(!string.IsNullOrEmpty(displayName));
            if (!string.IsNullOrEmpty(displayName)) w.Put(displayName);

            BroadcastReliable(w);
        }


        // 兜底协程：超时自动清 Loading
        private System.Collections.IEnumerator ClearLootLoadingTimeout(ItemStatsSystem.Inventory inv, float seconds)
        {
            float t = 0f;
            while (inv && inv.Loading && t < seconds) { t += UnityEngine.Time.deltaTime; yield return null; }
            if (inv && inv.Loading) inv.Loading = false;
        }
        private static int ComputeLootKeyFromPos(Vector3 pos)
        {
            var v = pos * 10f;
            int x = Mathf.RoundToInt(v.x);
            int y = Mathf.RoundToInt(v.y);
            int z = Mathf.RoundToInt(v.z);
            return new Vector3Int(x, y, z).GetHashCode();
        }



        // 通过 inv 找到它对应的 Lootbox 世界坐标；找不到则返回 false
        private bool TryGetLootboxWorldPos(Inventory inv, out Vector3 pos)
        {
            pos = default;
            if (!inv) return false;
            var boxes = GameObject.FindObjectsOfType<InteractableLootbox>();
            foreach (var b in boxes)
            {
                if (!b) continue;
                if (b.Inventory == inv) { pos = b.transform.position; return true; }
            }
            return false;
        }

        // 根据位置提示在半径内兜底解析对应的 lootbox（主机端用）
        private bool TryResolveLootByHint(Vector3 posHint, out Inventory inv, float radius = 2.5f)
        {
            inv = null;
            float best = float.MaxValue;
            var boxes = GameObject.FindObjectsOfType<InteractableLootbox>();
            foreach (var b in boxes)
            {
                if (!b || b.Inventory == null) continue;
                float d = Vector3.Distance(b.transform.position, posHint);
                if (d < radius && d < best) { best = d; inv = b.Inventory; }
            }
            return inv != null;
        }

        // 每次开箱都拉起一次“解卡”兜底，避免第二次打开卡死
        public void KickLootTimeout(Inventory inv, float seconds = 1.5f)
        {
            StartCoroutine(ClearLootLoadingTimeout(inv, seconds));
        }

        // 当前 LootView 是否就是这个容器（用它来识别“战利品容器”）
        public static bool IsCurrentLootInv(ItemStatsSystem.Inventory inv)
        {
            var lv = Duckov.UI.LootView.Instance;
            return lv && inv && object.ReferenceEquals(inv, lv.TargetInventory);
        }

        private bool Server_TryResolveLootAggressive(int scene, int posKey, int iid, Vector3 posHint, out ItemStatsSystem.Inventory inv)
        {
            inv = null;

            // 1) 你原有的两条路径
            if (TryResolveLootById(scene, posKey, iid, out inv)) return true;
            if (TryResolveLootByHint(posHint, out inv)) return true;

            // 2) 兜底：在 posHint 附近 3m 扫一圈，强制确保并注册
            float best = 9f; // 3m^2
            InteractableLootbox bestBox = null;
            foreach (var b in UnityEngine.Object.FindObjectsOfType<InteractableLootbox>())
            {
                if (!b || !b.gameObject.activeInHierarchy) continue;
                if (scene >= 0 && b.gameObject.scene.buildIndex != scene) continue;
                float d2 = (b.transform.position - posHint).sqrMagnitude;
                if (d2 < best) { best = d2; bestBox = b; }
            }
            if (!bestBox) return false;

            // 触发/强制创建 Inventory（原游戏逻辑会注册到 LevelManager.LootBoxInventories）
            inv = bestBox.Inventory;  // 等价于 GetOrCreateInventory(b)
            if (!inv) return false;

            // 保险：把 posKey→inv 显式写入一次
            var dict = InteractableLootbox.Inventories;
            if (dict != null)
            {
                int key = ComputeLootKey(bestBox.transform);
                dict[key] = inv;
            }
            return true;
        }

        private void Server_HandleLootOpenRequest(NetPeer peer, NetPacketReader r)
        {
            if (!IsServer) return;

            // 旧三元标识
            int scene = r.GetInt();
            int posKey = r.GetInt();
            int iid = r.GetInt();

            // 对齐 PutLootId：可能还带了稳定ID
            int lootUid = -1;
            if (r.AvailableBytes >= 4) lootUid = r.GetInt();

            // 请求版本（向后兼容）
            byte reqVer = 0;
            if (r.AvailableBytes >= 1) reqVer = r.GetByte();

            // 位置提示（厘米压缩），防御式读取
            Vector3 posHint = Vector3.zero;
            if (r.AvailableBytes >= 12) posHint = r.GetV3cm();

            // 先用稳定ID命中（AI掉落箱优先命中这里）
            ItemStatsSystem.Inventory inv = null;
            if (lootUid >= 0) _srvLootByUid.TryGetValue(lootUid, out inv);

            if (LootboxDetectUtil.IsPrivateInventory(inv)) { Server_SendLootDeny(peer, "no_inv"); return; }

            // 命不中再走你原有“激进解析”：三元标识 + 附近3米扫描并注册
            if (inv == null && !Server_TryResolveLootAggressive(scene, posKey, iid, posHint, out inv))
            {
                Server_SendLootDeny(peer, "no_inv");
                return;
            }

            // 只回给发起的这个 peer（不要广播）
            Server_SendLootboxState(peer, inv);
        }


        private readonly System.Collections.Generic.Dictionary<uint, (Inventory inv, int pos)> _cliPendingReorder
    = new System.Collections.Generic.Dictionary<uint, (Inventory inv, int pos)>();

        public void NoteLootReorderPending(uint token, Inventory inv, int targetPos)
        {
            if (token != 0 && inv) _cliPendingReorder[token] = (inv, targetPos);
        }

        private static bool TryGetLootInvByKeyEverywhere(int posKey, out Inventory inv)
        {
            inv = null;

            // A) InteractableLootbox.Inventories
            var dictA = InteractableLootbox.Inventories;
            if (dictA != null && dictA.TryGetValue(posKey, out inv) && inv) return true;

            // B) LevelManager.LootBoxInventories
            try
            {
                var lm = LevelManager.Instance;
                var dictB = lm != null ? LevelManager.LootBoxInventories : null;
                if (dictB != null && dictB.TryGetValue(posKey, out inv) && inv)
                {
                    // 顺手回填 A，保持一致
                    try { if (dictA != null) dictA[posKey] = inv; } catch { }
                    return true;
                }
            }
            catch { }

            inv = null;
            return false;
        }

        private readonly Dictionary<int, float> _cliLastAiHp = new Dictionary<int, float>();

        // 小工具：仅做UI表现，不改数值与事件
        static void TryShowDamageBarUI(Health h, float damage)
        {
            if (h == null || damage <= 0f) return;

            try
            {
                // 1) 找到当前 HealthBar
                var hbm = HealthBarManager.Instance;
                if (hbm == null) return;

                var miGet = AccessTools.DeclaredMethod(typeof(HealthBarManager), "GetActiveHealthBar", new[] { typeof(global::Health) });
                var hb = miGet?.Invoke(hbm, new object[] { h });
                if (hb == null) return;

                // 2) 取得 fill 的 rect 宽度（像素）
                var fiFill = AccessTools.Field(typeof(global::Duckov.UI.HealthBar), "fill");
                var fillImg = fiFill?.GetValue(hb) as UnityEngine.UI.Image;
                float width = 0f;
                if (fillImg != null)
                {
                    // 注意：rect 是本地空间宽度，足够用于“最小像素宽度”
                    width = fillImg.rectTransform.rect.width;
                }

                // 3) 计算“最小可见伤害”
                //    - minPixels: 小伤害条至少显示这么宽
                //    - minPercent: 即使宽度没取到，也保证一个极小百分比
                const float minPixels = 2f;
                const float minPercent = 0.0015f; // 0.15%

                float maxHp = Mathf.Max(1f, h.MaxHealth);
                float minByPixels = (width > 0f) ? (minPixels / width) * maxHp : 0f;
                float minByPercent = minPercent * maxHp;
                float minDamageToShow = Mathf.Max(minByPixels, minByPercent);

                // 4) 以“实际伤害 or 最小可见伤害”的较大者来显示受击条（仅视觉，不改真实血量）
                float visualDamage = Mathf.Max(damage, minDamageToShow);

                // 5) 反射调用 HealthBar.ShowDamageBar(float)
                var miShow = AccessTools.DeclaredMethod(typeof(global::Duckov.UI.HealthBar), "ShowDamageBar", new[] { typeof(float) });
                miShow?.Invoke(hb, new object[] { visualDamage });
            }
            catch
            {
                // 静默失败，避免 UI 缺失导致报错
            }
        }

        // 客户端：AI 血量 pending（cur 已有，这里补 max）
        private readonly Dictionary<int, float> _cliPendingAiMax = new Dictionary<int, float>();

        private readonly Dictionary<Health, float> _cliAiMaxOverride = new Dictionary<Health, float>();
        internal bool TryGetClientMaxOverride(Health h, out float v) => _cliAiMaxOverride.TryGetValue(h, out v);

        private void Client_HookSelfHealth()
        {
            if (_cliHookedSelf) return;
            var main = CharacterMainControl.Main;
            var h = main ? main.GetComponentInChildren<Health>(true) : null;
            if (!h) return;

            _cbSelfHpChanged = _ => Client_SendSelfHealth(h, force: false);
            _cbSelfMaxChanged = _ => Client_SendSelfHealth(h, force: true);
            _cbSelfHurt = di =>
            {
                _cliLastSelfHurtAt = Time.time;              // 记录受击时间
                try { _cliLastSelfHpLocal = h.CurrentHealth; } catch { }
                Client_SendSelfHealth(h, force: true);       // 受击当帧强制上报，跳过 20Hz 节流
            };
            _cbSelfDead = _ => Client_SendSelfHealth(h, force: true);

            h.OnHealthChange.AddListener(_cbSelfHpChanged);
            h.OnMaxHealthChange.AddListener(_cbSelfMaxChanged);
            h.OnHurtEvent.AddListener(_cbSelfHurt);
            h.OnDeadEvent.AddListener(_cbSelfDead);

            _cliHookedSelf = true;

            // 初次钩上也主动发一次，作为双保险
            Client_SendSelfHealth(h, force: true);
        }

        private void Client_UnhookSelfHealth()
        {
            if (!_cliHookedSelf) return;
            var main = CharacterMainControl.Main;
            var h = main ? main.GetComponentInChildren<Health>(true) : null;
            if (h)
            {
                if (_cbSelfHpChanged != null) h.OnHealthChange.RemoveListener(_cbSelfHpChanged);
                if (_cbSelfMaxChanged != null) h.OnMaxHealthChange.RemoveListener(_cbSelfMaxChanged);
                if (_cbSelfHurt != null) h.OnHurtEvent.RemoveListener(_cbSelfHurt);
                if (_cbSelfDead != null) h.OnDeadEvent.RemoveListener(_cbSelfDead);
            }
            _cliHookedSelf = false;
            _cbSelfHpChanged = _cbSelfMaxChanged = null;
            _cbSelfHurt = _cbSelfDead = null;
        }

        // 发送自身血量（带 20Hz 节流 & 值未变不发）
        private void Client_SendSelfHealth(Health h, bool force)
        {
            if (_cliApplyingSelfSnap || Time.time < _cliEchoMuteUntil) return;

            if (!networkStarted || IsServer || connectedPeer == null || h == null) return;

            float max = 0f, cur = 0f;
            try { max = h.MaxHealth; } catch { }
            try { cur = h.CurrentHealth; } catch { }

            // 去抖：值相同直接跳过
            if (!force && Mathf.Approximately(max, _cliLastSentHp.max) && Mathf.Approximately(cur, _cliLastSentHp.cur))
                return;

            // 节流：20Hz
            if (!force && Time.time < _cliNextSendHp) return;

            var w = new NetDataWriter();
            w.Put((byte)Op.PLAYER_HEALTH_REPORT);
            w.Put(max);
            w.Put(cur);
            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);

            _cliLastSentHp = (max, cur);
            _cliNextSendHp = Time.time + 0.05f;
        }

        public uint AllocateDropId()
        {
            uint id = nextDropId++;
            while (serverDroppedItems.ContainsKey(id))
                id = nextDropId++;
            return id;
        }

        private InteractableLootbox ResolveDeadLootPrefabOnServer()
        {
            var any = GameplayDataSettings.Prefabs;
            try
            {      
                if (any != null && any.LootBoxPrefab_Tomb != null) return any.LootBoxPrefab_Tomb;
            }
            catch { }

            if(any != null)
            {
                return any.LootBoxPrefab;
            }

            return null; // 客户端收到 DEAD_LOOT_SPAWN 时也有兜底寻找预制体的逻辑
        }


        public void Net_ReportPlayerDeadTree(CharacterMainControl who)
        {
            // 仅客户端上报；主机不需要发
            if (!networkStarted || IsServer || connectedPeer == null || who == null) return;

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

            // 把整棵物品“快照”写进包里
            WriteItemSnapshot(writer, item);

            connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        public void Client_SendLootSplitRequest(ItemStatsSystem.Inventory lootInv, int srcPos, int count, int preferPos)
        {
            if (!networkStarted || IsServer || connectedPeer == null || lootInv == null) return;
            if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return;
            if (count <= 0) return;

            var w = writer; w.Reset();
            w.Put((byte)Op.LOOT_REQ_SPLIT);
            PutLootId(w, lootInv);   // scene/posKey/iid/lootUid
            w.Put(srcPos);
            w.Put(count);
            w.Put(preferPos);        // -1 可让主机自行找空格
            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
        }

        //  ====== Sans看到这这这，明天你把这下面的方法修了目前loot的拆分会复制问题 (已解决) =======
        //  ====== Sans看到这这这，明天你把这下面的方法修了目前loot的拆分会复制问题 (已解决) =======
        //  ====== Sans看到这这这，明天你把这下面的方法修了目前loot的拆分会复制问题 (已解决) =======

        private void Server_HandleLootSplitRequest(NetPeer peer, NetPacketReader r)
        {
            int scene = r.GetInt();
            int posKey = r.GetInt();
            int iid = r.GetInt();
            int lootUid = r.GetInt();
            int srcPos = r.GetInt();
            int count = r.GetInt();
            int prefer = r.GetInt();

            // 定位容器（优先用 lootUid）
            ItemStatsSystem.Inventory inv = null;
            if (lootUid >= 0) _srvLootByUid.TryGetValue(lootUid, out inv);
            if (inv == null && !TryResolveLootById(scene, posKey, iid, out inv))
            { Server_SendLootDeny(peer, "no_inv"); return; }

            if (LootboxDetectUtil.IsPrivateInventory(inv))
            { Server_SendLootDeny(peer, "no_inv"); return; }

            var srcItem = inv.GetItemAt(srcPos);
            if (!srcItem || count <= 0 || !srcItem.Stackable || count >= srcItem.StackCount)
            { Server_SendLootDeny(peer, "split_bad"); return; }

            Server_DoSplitAsync(inv, srcPos, count, prefer).Forget();
        }


        private async Cysharp.Threading.Tasks.UniTaskVoid Server_DoSplitAsync(
            ItemStatsSystem.Inventory inv, int srcPos, int count, int prefer)
        {
            _serverApplyingLoot = true;
            try
            {
                var srcItem = inv.GetItemAt(srcPos);
                if (!srcItem) return;

                // 1) 主机执行真正的拆分（源堆 -count）
                var newItem = await srcItem.Split(count);
                if (!newItem) return;

                // 2) 优先按 prefer 落到空格；没有空位才允许合并
                int dst = prefer;
                if (dst < 0 || inv.GetItemAt(dst)) dst = inv.GetFirstEmptyPosition(srcPos + 1);
                if (dst < 0) dst = inv.GetFirstEmptyPosition(0);

                bool ok = false;
                if (dst >= 0) ok = inv.AddAt(newItem, dst);                   // 不合并
                if (!ok) ok = ItemUtilities.AddAndMerge(inv, newItem, srcPos + 1); // 兜底

                if (!ok)
                {
                    try { UnityEngine.Object.Destroy(newItem.gameObject); } catch { }
                    if (srcItem) srcItem.StackCount = srcItem.StackCount + count;
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[LOOT][SPLIT] exception: {ex}");
            }
            finally
            {
                _serverApplyingLoot = false;
                Server_SendLootboxState(null, inv);   
            }
        }

        public void Server_HandlePlayerDeadTree(Vector3 pos, Quaternion rot, ItemSnapshot snap)
        {
            if (!IsServer) return;

            var tmpRoot = BuildItemFromSnapshot(snap);
            if (!tmpRoot) { Debug.LogWarning("[LOOT] HostDeath BuildItemFromSnapshot failed."); return; }

            var deadPfb = ResolveDeadLootPrefabOnServer();                     // → LootBoxPrefab_Tomb
            var box = InteractableLootbox.CreateFromItem(tmpRoot, pos + Vector3.up * 0.10f, rot, true, deadPfb, false);
            if (box) Server_OnDeadLootboxSpawned(box, null);                   // whoDied=null → aiId=0 → 客户端走“玩家坟碑盒”

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

            var snap = MakeSnapshot(item);                                     // 本地版“WriteItemSnapshot”
            Server_HandlePlayerDeadTree(pos, rot, snap);
        }

        private ItemSnapshot MakeSnapshot(ItemStatsSystem.Item item)
        {
            ItemSnapshot s;
            s.typeId = item.TypeID;
            s.stack = item.StackCount;
            s.durability = item.Durability;
            s.durabilityLoss = item.DurabilityLoss;
            s.inspected = item.Inspected;
            s.slots = new System.Collections.Generic.List<(string, ItemSnapshot)>();
            s.inventory = new System.Collections.Generic.List<ItemSnapshot>();

            var slots = item.Slots;
            if (slots != null && slots.list != null)
            {
                foreach (var slot in slots.list)
                    if (slot != null && slot.Content != null)
                        s.slots.Add((slot.Key ?? string.Empty, MakeSnapshot(slot.Content)));
            }

            var invItems = TryGetInventoryItems(item.Inventory);             
            if (invItems != null)
                foreach (var child in invItems)
                    if (child != null) s.inventory.Add(MakeSnapshot(child));

            return s;
        }

        public int StableRootId(CharacterSpawnerRoot r)
        {
            if (r == null) return 0;
            if (r.SpawnerGuid != 0) return r.SpawnerGuid;

            // 取 relatedScene（Init 会设置）；拿不到就退化为当前场景索引
            int sceneIndex = -1;
            try
            {
                var fi = typeof(CharacterSpawnerRoot).GetField("relatedScene", BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null) sceneIndex = (int)fi.GetValue(r);
            }
            catch { }
            if (sceneIndex < 0) sceneIndex = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;

            // 世界坐标量化到 0.1m，避免浮点抖动
            Vector3 p = r.transform.position;
            int qx = Mathf.RoundToInt(p.x * 10f);
            int qy = Mathf.RoundToInt(p.y * 10f);
            int qz = Mathf.RoundToInt(p.z * 10f);

            // 名称 + 位置 + 场景索引 → FNV1a
            string key = $"{sceneIndex}:{r.name}:{qx},{qy},{qz}";
            return StableHash(key);
        }

        // 主机：针对单个 Root 发送增量种子（包含 guid 与兼容 id 两条映射）
        public void Server_SendRootSeedDelta(CharacterSpawnerRoot r, NetPeer target = null)
        {
            if (!IsServer || r == null) return;

            int idA = StableRootId(r);      // 现有策略：优先用 SpawnerGuid
            int idB = StableRootId_Alt(r);  // 兼容策略：忽略 guid，用 名称+位置+场景

            int seed = DeriveSeed(sceneSeed, idA);
            aiRootSeeds[idA] = seed;        // 主机本地记录，便于调试

            var w = writer; w.Reset();
            w.Put((byte)Op.AI_SEED_PATCH);
            int count = (idA == idB) ? 1 : 2;
            w.Put(count);
            w.Put(idA); w.Put(seed);
            if (count == 2) { w.Put(idB); w.Put(seed); }

            if (target == null) BroadcastReliable(w);
            else target.Send(w, DeliveryMethod.ReliableOrdered);
        }

        // 客户端：应用增量，不清空，直接补/改
        void HandleAiSeedPatch(NetPacketReader r)
        {
            int n = r.GetInt();
            for (int i = 0; i < n; i++)
            {
                int id = r.GetInt();
                int seed = r.GetInt();
                aiRootSeeds[id] = seed;
            }
            Debug.Log("[AI-SEED] 应用增量 Root 种子数: " + n);
        }

        static void EnsureMagicBlendBound(CharacterMainControl cmc)
        {
            if (!cmc) return;
            var model = cmc.characterModel;
            if (!model) return;

            var blend = model.GetComponent<CharacterAnimationControl_MagicBlend>();
            if (!blend) blend = model.gameObject.AddComponent<CharacterAnimationControl_MagicBlend>();

            if (cmc.GetGun() != null)
            {
                blend.animator.SetBool(Animator.StringToHash("GunReady"), true);
                Traverse.Create(blend).Field<ItemAgent_Gun>("gunAgent").Value = cmc.GetGun();
                Traverse.Create(blend).Field<DuckovItemAgent>("holdAgent").Value = cmc.GetGun();
            }

            if (cmc.GetMeleeWeapon() != null)
            {
                blend.animator.SetBool(Animator.StringToHash("GunReady"), true);
                Traverse.Create(blend).Field<DuckovItemAgent>("holdAgent").Value = cmc.GetMeleeWeapon();
            }

            blend.characterModel = model;
            blend.characterMainControl = cmc;

            if (!blend.animator || blend.animator == null)
                blend.animator = model.GetComponentInChildren<Animator>(true);

            var anim = blend.animator;
            if (anim)
            {
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                anim.updateMode = AnimatorUpdateMode.Normal;
                int idx = anim.GetLayerIndex("MeleeAttack");
                if (idx >= 0) anim.SetLayerWeight(idx, 0f);
            }
        }

        public void Server_ForceAuthSelf(Health h)
        {
            if (!networkStarted || !IsServer || h == null) return;
            if (!_srvHealthOwner.TryGetValue(h, out var ownerPeer) || ownerPeer == null) return;

            var w = writer; w.Reset();
            w.Put((byte)Op.AUTH_HEALTH_SELF);
            float max = 0f, cur = 0f;
            try { max = h.MaxHealth; cur = h.CurrentHealth; } catch { }
            w.Put(max);
            w.Put(cur);
            ownerPeer.Send(w, LiteNetLib.DeliveryMethod.ReliableOrdered);
        }

        [ThreadStatic] public static bool _applyingDoor;  // 客户端正在应用网络下发，避免误触发本地拦截

        // 与 Door.GetKey 一致的稳定 Key：Door_{round(pos*10)} 的 GetHashCode
        public int ComputeDoorKey(Transform t)
        {
            if (!t) return 0;
            var p = t.position * 10f;
            var k = new Vector3Int(
                Mathf.RoundToInt(p.x),
                Mathf.RoundToInt(p.y),
                Mathf.RoundToInt(p.z)
            );
            return $"Door_{k}".GetHashCode();
        }

        // 通过 key 找场景里的 Door（优先用其缓存字段 doorClosedDataKeyCached）
        private Door FindDoorByKey(int key)
        {
            if (key == 0) return null;
            var doors = UnityEngine.Object.FindObjectsOfType<Door>(true);
            var fCache = AccessTools.Field(typeof(Door), "doorClosedDataKeyCached");
            var mGetKey = AccessTools.Method(typeof(Door), "GetKey");

            foreach (var d in doors)
            {
                if (!d) continue;
                int k = 0;
                try { k = (int)fCache.GetValue(d); } catch { }
                if (k == 0)
                {
                    try { k = (int)mGetKey.Invoke(d, null); } catch { }
                }
                if (k == key) return d;
            }
            return null;
        }

        // 客户端：请求把某门设为 closed/open
        public void Client_RequestDoorSetState(Door d, bool closed)
        {
            if (IsServer || connectedPeer == null || d == null) return;

            int key = 0;
            try
            {
                // 优先用缓存字段；无则重算（与 Door.GetKey 一致）
                key = (int)AccessTools.Field(typeof(Door), "doorClosedDataKeyCached").GetValue(d);
            }
            catch { }
            if (key == 0) key = ComputeDoorKey(d.transform);
            if (key == 0) return;

            var w = writer; w.Reset();
            w.Put((byte)Op.DOOR_REQ_SET);
            w.Put(key);
            w.Put(closed);
            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
        }

        // 主机：处理客户端的设门请求
        private void Server_HandleDoorSetRequest(LiteNetLib.NetPeer peer, NetPacketReader reader)
        {
            if (!IsServer) return;
            int key = reader.GetInt();
            bool closed = reader.GetBool();

            var door = FindDoorByKey(key);
            if (!door) return;

            // 调原生 API，走动画/存档/切 NavMesh
            if (closed) door.Close();
            else door.Open();
            // Postfix 里会统一广播；为保险也可在此再广播一次（双发也没坏处）
            // Server_BroadcastDoorState(key, closed);
        }

        // 主机：广播一条门状态
        public void Server_BroadcastDoorState(int key, bool closed)
        {
            if (!IsServer) return;
            var w = writer; w.Reset();
            w.Put((byte)Op.DOOR_STATE);
            w.Put(key);
            w.Put(closed);
            netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }

        // 客户端：应用门状态（反射调用 SetClosed，确保 NavMeshCut/插值/存档一致）
        private void Client_ApplyDoorState(int key, bool closed)
        {
            if (IsServer) return;
            var door = FindDoorByKey(key);
            if (!door) return;

            try
            {
                _applyingDoor = true;

                var mSetClosed2 = AccessTools.Method(typeof(Door), "SetClosed",
                                   new[] { typeof(bool), typeof(bool) });
                if (mSetClosed2 != null)
                {
                    mSetClosed2.Invoke(door, new object[] { closed, true });
                }
                else
                {
                    if (closed)
                        AccessTools.Method(typeof(Door), "Close")?.Invoke(door, null);
                    else
                        AccessTools.Method(typeof(Door), "Open")?.Invoke(door, null);
                }
            }
            finally
            {
                _applyingDoor = false;
            }
        }

        // 用来避免 dangerFx 重复播放
        private readonly HashSet<uint> _dangerDestructibleIds = new HashSet<uint>();

        // 客户端：用于 ENV 快照应用，静默切换到“已破坏”外观（不放爆炸特效）
        private void Client_ApplyDestructibleDead_Snapshot(uint id)
        {
            if (_deadDestructibleIds.Contains(id)) return;
            var hs = FindDestructible(id);
            if (!hs) return;

            // Breakable：关正常/危险外观，开破坏外观，关主碰撞体
            var br = hs.GetComponent<Breakable>();
            if (br)
            {
                try
                {
                    if (br.normalVisual) br.normalVisual.SetActive(false);
                    if (br.dangerVisual) br.dangerVisual.SetActive(false);
                    if (br.breakedVisual) br.breakedVisual.SetActive(true);
                    if (br.mainCollider) br.mainCollider.SetActive(false);
                }
                catch { }
            }

            // HalfObsticle：走它自带的 Dead 一下，避免残留交互
            var half = hs.GetComponent<HalfObsticle>();
            if (half) { try { half.Dead(new DamageInfo()); } catch { } }

            // 彻底关掉所有 Collider
            try
            {
                foreach (var c in hs.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            }
            catch { }

            _deadDestructibleIds.Add(id);
        }

        private static Transform FindBreakableWallRoot(Transform t)
        {
            var p = t;
            while (p != null)
            {
                string nm = p.name;
                if (!string.IsNullOrEmpty(nm) &&
                    nm.IndexOf("BreakableWall", StringComparison.OrdinalIgnoreCase) >= 0)
                    return p;
                p = p.parent;
            }
            return null;
        }

        private static uint ComputeStableIdForDestructible(HealthSimpleBase hs)
        {
            if (!hs) return 0u;
            Transform root = FindBreakableWallRoot(hs.transform);
            if (root == null) root = hs.transform;
            try { return NetDestructibleTag.ComputeStableId(root.gameObject); }
            catch { return 0u; }
        }
        private void ScanAndMarkInitiallyDeadDestructibles()
        {
            if (_deadDestructibleIds == null) return;
            if (_serverDestructibles == null || _serverDestructibles.Count == 0) return;

            foreach (var kv in _serverDestructibles)
            {
                uint id = kv.Key;
                var hs = kv.Value;
                if (!hs) continue;
                if (_deadDestructibleIds.Contains(id)) continue;

                bool isDead = false;

                // 1) HP 兜底（部分 HSB 有 HealthValue）
                try { if (hs.HealthValue <= 0f) isDead = true; } catch { }

                // 2) Breakable：breaked 外观/主碰撞体关闭 => 视为“已破坏”
                if (!isDead)
                {
                    try
                    {
                        var br = hs.GetComponent<Breakable>();
                        if (br)
                        {
                            bool brokenView = (br.breakedVisual && br.breakedVisual.activeInHierarchy);
                            bool mainOff = (br.mainCollider && !br.mainCollider.activeSelf);
                            if (brokenView || mainOff) isDead = true;
                        }
                    }
                    catch { }
                }

                // 3) HalfObsticle：如果存在 isDead 字段，读一下（没有就忽略）
                if (!isDead)
                {
                    try
                    {
                        var half = hs.GetComponent("HalfObsticle"); // 避免编译期硬引用
                        if (half != null)
                        {
                            var t = half.GetType();
                            var fi = HarmonyLib.AccessTools.Field(t, "isDead");
                            if (fi != null)
                            {
                                object v = fi.GetValue(half);
                                if (v is bool && (bool)v) isDead = true;
                            }
                        }
                    }
                    catch {}
                }

                if (isDead) _deadDestructibleIds.Add(id);
            }
        }

        // 客户端：是否把远端 AI 全部常显（默认 true）
        public bool Client_ForceShowAllRemoteAI = true;

        static bool HasParam(Animator anim, string name, AnimatorControllerParameterType type)
        {
            foreach (var p in anim.parameters)
                if (p.name == name && p.type == type) return true;
            return false;
        }

        static void TrySetBool(Animator anim, string name, bool v)
        {
            if (HasParam(anim, name, AnimatorControllerParameterType.Bool))
                anim.SetBool(name, v);
        }

        static void TrySetInt(Animator anim, string name, int v)
        {
            if (HasParam(anim, name, AnimatorControllerParameterType.Int))
                anim.SetInteger(name, v);
        }

        static void TrySetFloat(Animator anim, string name, float v)
        {
            if (HasParam(anim, name, AnimatorControllerParameterType.Float))
                anim.SetFloat(name, v);
        }

        static void TryTrigger(Animator anim, string name)
        {
            if (HasParam(anim, name, AnimatorControllerParameterType.Trigger))
                anim.SetTrigger(name);
        }

        private void Client_ApplyFaceIfAvailable(string playerId, GameObject instance, string faceOverride = null)
        {
            try
            {
                // 先挑一个 JSON
                string face = faceOverride;
                if (string.IsNullOrEmpty(face))
                {
                    if (_cliPendingFace.TryGetValue(playerId, out var pf) && !string.IsNullOrEmpty(pf))
                        face = pf;
                    else if (clientPlayerStatuses.TryGetValue(playerId, out var st) && !string.IsNullOrEmpty(st.CustomFaceJson))
                        face = st.CustomFaceJson;
                }

                // 没 JSON 就先不涂，等后续状态更新再补
                if (string.IsNullOrEmpty(face))
                    return;

                // 反序列化成结构体（struct 永远非 null）
                var data = JsonUtility.FromJson<CustomFaceSettingData>(face);

                // 找到 CustomFaceInstance 并应用
                var cm = instance != null ? instance.GetComponentInChildren<CharacterModel>(true) : null;
                var cf = cm != null ? cm.CustomFace : null;
                if (cf != null)
                {
                    HardApplyCustomFace(cf, data);
                    _cliPendingFace[playerId] = face; // 记住成功涂过的 JSON
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[COOP][FACE] Apply failed for {playerId}: {e}");
            }
        }

        static void HardApplyCustomFace(CustomFaceInstance cf, in CustomFaceSettingData data)
        {
            if (cf == null) return;
            try { StripAllCustomFaceParts(cf.gameObject); } catch { }
            try { cf.LoadFromData(data); } catch { }
            try { cf.RefreshAll(); } catch { }
        }

        static void StripAllCustomFaceParts(GameObject root)
        {
            try
            {
                var all = root.GetComponentsInChildren<global::CustomFacePart>(true);
                int n = 0;
                foreach (var p in all)
                {
                    if (!p) continue;
                    n++;
                    UnityEngine.Object.Destroy(p.gameObject);
                }
                 Debug.Log($"[COOP][FACE] stripped {n} CustomFacePart");
            }
            catch {  }
        }

        private readonly HashSet<int> _cliAiDeathFxOnce = new HashSet<int>();

        private void Client_PlayAiDeathFxAndSfx(CharacterMainControl cmc)
        {
            if (!cmc) return;
            var model = cmc.characterModel;
            if (!model) return;
          
            object hv = null;
            try
            {
                var fi = model.GetType().GetField("hurtVisual",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fi != null) hv = fi.GetValue(model);
            }
            catch { }

            if (hv == null)
            {
                try { hv = model.GetComponentInChildren(typeof(global::HurtVisual), true); } catch { }
            }

            if (hv != null)
            {
                try
                {
                    var miDead = hv.GetType().GetMethod("OnDead",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (miDead != null)
                    {
                        var di = new global::DamageInfo
                        {
                            // OnDead 本身只需要一个 DamageInfo；就传个位置即可
                            damagePoint = cmc.transform.position,
                            damageNormal = Vector3.up
                        };
                        miDead.Invoke(hv, new object[] { di });

                        if (FmodEventExists("event:/e_KillMarker"))
                        {
                            AudioManager.Post("e_KillMarker");
                        }
                    }
                }
                catch {  }
            }
        }

        private void Client_EnsureSelfDeathEvent(global::Health h, global::CharacterMainControl cmc)
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


        //  主机：客户端请求给容器里的武器插配件
        //private void Server_HandleLootSlotUnplugRequest(NetPeer peer, NetPacketReader r)
        //{
        //    int scene = r.GetInt(); int posKey = r.GetInt(); int iid = r.GetInt(); int lootUid = r.GetInt();
        //    int weaponPos = r.GetInt(); int slotIndex = r.GetInt(); // 客户端告诉要拆哪把枪、哪个槽

        //    Inventory inv = null;
        //    if (lootUid >= 0) _srvLootByUid.TryGetValue(lootUid, out inv);
        //    if (inv == null && !TryResolveLootById(scene, posKey, iid, out inv)) { Server_SendLootDeny(peer, "no_inv"); return; }
        //    if (LootboxDetectUtil.IsPrivateInventory(inv)) { Server_SendLootDeny(peer, "no_inv"); return; }  // 跟 PUT/TAKE 一致

        //    var weapon = inv.GetItemAt(weaponPos);                // 读容器里武器
        //    if (!weapon) { Server_SendLootDeny(peer, "bad_weapon"); return; }

        //    bool ok = false;
        //    _serverApplyingLoot = true;
        //    try
        //    {
        //        // 定位到要拆的槽，然后 Unplug；被拆下的部件要塞回 inv（AddAndMerge 或找空位）
        //        var slots = weapon.Slots;
        //        var slot = slots != null ? slots[slotIndex] : null;
        //        var removed = slot != null ? slot.Unplug() : null;
        //        if (removed) ok = ItemUtilities.AddAndMerge(inv, removed, 0);
        //    }
        //    catch (Exception ex) { Debug.LogError($"[LOOT][UNPLUG] {ex}"); }
        //    finally { _serverApplyingLoot = false; }

        //    if (!ok) { Server_SendLootDeny(peer, "slot_unplug_fail"); Server_SendLootboxState(peer, inv); return; }
        //    Server_SendLootboxState(null, inv);                   //  改完后广播给所有人
        //}

        private void Server_HandleLootSlotPlugRequest(NetPeer peer, NetPacketReader r)
        {
            // 1) 容器定位
            int scene = r.GetInt();
            int posKey = r.GetInt();
            int iid = r.GetInt();
            int lootUid = r.GetInt();
            var inv = ResolveLootInv(scene, posKey, iid, lootUid);
            if (inv == null || LootboxDetectUtil.IsPrivateInventory(inv)) { Server_SendLootDeny(peer, "no_inv"); return; }

            // 2) 目标主件 + 槽位
            var master = ReadItemRef(r, inv);
            string slotKey = r.GetString();
            if (!master) { Server_SendLootDeny(peer, "bad_weapon"); Server_SendLootboxState(peer, inv); return; }
            var dstSlot = master?.Slots?.GetSlot(slotKey);
            if (dstSlot == null) { Server_SendLootDeny(peer, "bad_slot"); Server_SendLootboxState(peer, inv); return; }

            // 3) 源
            bool srcInLoot = r.GetBool();
            Item srcItem = null;
            uint token = 0;
            ItemSnapshot snap = default;

            if (srcInLoot)
            {
                 srcItem = ReadItemRef(r, inv);
                if (!srcItem)
                {
                    Server_SendLootDeny(peer, "bad_src");
                    Server_SendLootboxState(peer, inv);   // 便于客户端立刻对齐
                    return;
                }
            }
            else
            {
                token = r.GetUInt();
                snap = ReadItemSnapshot(r);
            }

            // 4) 执行
            _serverApplyingLoot = true;
            bool ok = false;
            Item unplugged = null;
            try
            {
                Item child = srcItem;
                if (!srcInLoot)
                {
                    // 从 snapshot 重建对象
                    child = BuildItemFromSnapshot(snap);
                    if (!child)
                    {
                        Server_SendLootDeny(peer, "build_fail");
                        Server_SendLootboxState(peer, inv);
                        return;
                    }
                }
                else
                {
                    // 从容器树/格子中摘出来
                    try { child.Detach(); } catch { }
                }

                ok = dstSlot.Plug(child, out unplugged);

                if (ok)
                {
                    // 背包来源：给发起者一个回执，让对方删除本地背包配件
                    if (!srcInLoot)
                    {
                        var ack = new NetDataWriter();
                        ack.Put((byte)Op.LOOT_PUT_OK);  // 复用 PUT 的 OK 回执
                        ack.Put(token);
                        peer.Send(ack, DeliveryMethod.ReliableOrdered);
                    }

                    // 一如既往广播最新容器快照
                    Server_SendLootboxState(null, inv);
                }
                else
                {
                    Server_SendLootDeny(peer, "slot_plug_fail");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LOOT][PLUG] {ex}");
                ok = false;
            }
            finally { _serverApplyingLoot = false; }

            if (!ok)
            {
                // 回滚：如果是 snapshot 创建的 child，需要销毁以免泄露
                if (!srcInLoot) { try { /* child 在 Plug 失败时仍在内存里 */ } catch { } }
                Server_SendLootDeny(peer, "plug_fail");
                Server_SendLootboxState(peer, inv);
                return;
            }

            // 若顶掉了原先的一个附件，把它放回容器格子
            if (unplugged)
            {
                if (!ItemUtilities.AddAndMerge(inv, unplugged, 0))
                {
                    try { if (unplugged) UnityEngine.Object.Destroy(unplugged.gameObject); } catch { }
                }
            }

            // (B) 源自玩家背包的情况：下发 LOOT_PUT_OK 让发起者删除本地那件
            if (!srcInLoot && token != 0)
            {
                var w2 = new NetDataWriter();
                w2.Put((byte)Op.LOOT_PUT_OK);
                w2.Put(token);
                peer.Send(w2, DeliveryMethod.ReliableOrdered);
            }

            // 5) 广播容器新状态
            Server_SendLootboxState(null, inv);
        }

        private uint _cliLocalToken
        {
            get => _nextLootToken;
            set => _nextLootToken = value;
        }

        public void Client_RequestLootSlotPlug(Inventory inv, Item master, string slotKey, Item child)
        {
            if (!networkStarted || IsServer || connectedPeer == null) return;

            var w = new NetDataWriter();
            w.Put((byte)Op.LOOT_REQ_SLOT_PLUG);

            // 容器定位
            PutLootId(w, inv);
            WriteItemRef(w, inv, master);
            w.Put(slotKey);

            bool srcInLoot = LootboxDetectUtil.IsLootboxInventory(child ? child.InInventory : null);
            w.Put(srcInLoot);

            if (srcInLoot)
            {
                // 源自容器：发容器内 Item 引用
                WriteItemRef(w, child.InInventory, child);
            }
            else
            {
                // 源自背包：发 token + 快照，并在本地登记“待删”
                uint token = ++_cliLocalToken;           // 你项目里已有递增 token 的字段/方法就用现成的
                _cliPendingSlotPlug[token] = child;
                w.Put(token);
                WriteItemSnapshot(w, child);
            }

            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
        }





        // 发送端：把 inv 内 item 的“路径”写进包里
        private void WriteItemRef(NetDataWriter w, Inventory inv, Item item)
        {
            // 找到 inv 中的“根物品”（顶层，不在任何槽位里）
            var root = item;
            while (root != null && root.PluggedIntoSlot != null) root = root.PluggedIntoSlot.Master;
            int rootIndex = inv != null ? inv.GetIndex(root) : -1;
            w.Put(rootIndex);

            // 从 item 逆向收集到根的槽位key，再反转写出
            var keys = new List<string>();
            var cur = item;
            while (cur != null && cur.PluggedIntoSlot != null) { var s = cur.PluggedIntoSlot; keys.Add(s.Key ?? ""); cur = s.Master; }
            keys.Reverse();
            w.Put(keys.Count);
            foreach (var k in keys) w.Put(k ?? "");
        }


        // 接收端：用“路径”从 inv 找回 item
        private Item ReadItemRef(NetPacketReader r, Inventory inv)
        {
            int rootIndex = r.GetInt();
            int keyCount = r.GetInt();
            var it = inv.GetItemAt(rootIndex);
            for (int i = 0; i < keyCount && it != null; i++)
            {
                string key = r.GetString();
                var slot = it.Slots?.GetSlot(key);
                it = slot != null ? slot.Content : null;
            }
            return it;
        }


        // 统一解析容器 Inventory：优先稳定ID，再回落到三元标识
        private ItemStatsSystem.Inventory ResolveLootInv(int scene, int posKey, int iid, int lootUid)
        {
            ItemStatsSystem.Inventory inv = null;

            // 先用稳定ID（主机用 _srvLootByUid；客户端用 _cliLootByUid）
            if (lootUid >= 0)
            {
                if (IsServer)
                {
                    if (_srvLootByUid != null && _srvLootByUid.TryGetValue(lootUid, out inv) && inv)
                        return inv;
                }
                else
                {
                    if (_cliLootByUid != null && _cliLootByUid.TryGetValue(lootUid, out inv) && inv)
                        return inv;
                }
            }

            // 回落到 scene/posKey/iid 三元定位
            if (TryResolveLootById(scene, posKey, iid, out inv) && inv)
                return inv;

            return null;
        }
        internal uint Client_RequestSlotUnplugToBackpack(Inventory lootInv, Item master, string slotKey, Inventory destInv, int destPos)
        {
            if (!networkStarted || IsServer || connectedPeer == null) return 0;
            if (!lootInv || !master || string.IsNullOrEmpty(slotKey)) return 0;
            if (!LootboxDetectUtil.IsLootboxInventory(lootInv) || LootboxDetectUtil.IsPrivateInventory(lootInv)) return 0;
            if (destInv && LootboxDetectUtil.IsLootboxInventory(destInv)) destInv = null; // 兜底用的😮sans

            // 1) 分配 token 并登记“TAKE_OK 的落位目的地”
            uint token = _nextLootToken++;
            if (destInv)
            {
                _cliPendingTake[token] = new PendingTakeDest
                {
                    inv = destInv,
                    pos = destPos,
                    slot = null,
                    srcLoot = lootInv,
                    srcPos = -1
                };
            }

            // 2) 发送“卸下 + 直落背包”的请求（在旧负载末尾追加 takeToBackpack + token）
            Client_RequestLootSlotUnplug(lootInv, master, slotKey, true, token);
            return token;
        }

        internal void Client_RequestLootSlotUnplug(Inventory inv, Item master, string slotKey)
        {
            if (!networkStarted || IsServer || connectedPeer == null) return;
            if (!inv || !master || string.IsNullOrEmpty(slotKey)) return;

            var w = writer; w.Reset();
            w.Put((byte)Op.LOOT_REQ_SLOT_UNPLUG);
            PutLootId(w, inv);            // 容器标识（scene/posKey/iid 或 uid）
            WriteItemRef(w, inv, master); // 在该容器里“主件”的路径
            w.Put(slotKey ?? string.Empty); // 要拔的 slot key
                                            // —— 旧负载到此为止（不带 takeToBackpack / token）——
            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
        }

        internal void Client_RequestLootSlotUnplug(Inventory inv, Item master, string slotKey, bool takeToBackpack, uint token)
        {
            if (!networkStarted || IsServer || connectedPeer == null) return;
            if (!inv || !master || string.IsNullOrEmpty(slotKey)) return;

            var w = writer; w.Reset();
            w.Put((byte)Op.LOOT_REQ_SLOT_UNPLUG);
            PutLootId(w, inv);             // 容器标识
            WriteItemRef(w, inv, master);  // 主件路径
            w.Put(slotKey ?? string.Empty);// slot key
                                           
            w.Put(takeToBackpack);
            w.Put(token);
            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
        }

        void Server_HandleLootSlotUnplugRequest(NetPeer peer, NetPacketReader r)
        {
            // 1) 容器定位
            int scene = r.GetInt();
            int posKey = r.GetInt();
            int iid = r.GetInt();
            int lootUid = r.GetInt();

            var inv = ResolveLootInv(scene, posKey, iid, lootUid);
            if (inv == null || LootboxDetectUtil.IsPrivateInventory(inv))
            {
                Server_SendLootDeny(peer, "no_inv");
                return;
            }

            // 2) 主件与槽位（新格式）
            var master = ReadItemRef(r, inv);
            string slotKey = r.GetString();
            if (!master)
            {
                Server_SendLootDeny(peer, "bad_weapon");
                return;
            }
            var slot = master?.Slots?.GetSlot(slotKey);
            if (slot == null)
            {
                Server_SendLootDeny(peer, "bad_slot");
                Server_SendLootboxState(peer, inv); // 只回请求方刷新
                return;
            }

            // 3) 追加字段（向后兼容：旧包没有这俩字段）
            bool takeToBackpack = false;
            uint token = 0;
            if (r.AvailableBytes >= 5) // 1(bool) + 4(uint) 
            {
                try
                {
                    takeToBackpack = r.GetBool();
                    token = r.GetUInt();
                }
                catch {}
            }

            // 4) 执行卸下
            Item removed = null;
            bool ok = false;
            _serverApplyingLoot = true; // 抑制服务端自己触发的后续广播/后处理
            try
            {
                removed = slot.Unplug();
                ok = removed != null;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LOOT][UNPLUG] {ex}");
                ok = false;
            }
            finally { _serverApplyingLoot = false; }

            if (!ok || !removed)
            {
                Server_SendLootDeny(peer, "slot_unplug_fail");
                Server_SendLootboxState(peer, inv); // 只回请求方刷新
                return;
            }

            // 5) 分支：回容器 或 直落背包
            if (!takeToBackpack)
            {
                if (!ItemUtilities.AddAndMerge(inv, removed, 0))
                {
                    try { if (removed) UnityEngine.Object.Destroy(removed.gameObject); } catch { }
                    Server_SendLootDeny(peer, "add_fail");
                    Server_SendLootboxState(peer, inv);
                    return;
                }
                Server_SendLootboxState(null, inv); // 广播：武器该槽已空，容器新添一件
                return;
            }

            // 让客户端在 Client_OnLootTakeOk 中落袋
            var wCli = new NetDataWriter();
            wCli.Put((byte)Op.LOOT_TAKE_OK);
            wCli.Put(token);
            WriteItemSnapshot(wCli, removed);
            peer.Send(wCli, DeliveryMethod.ReliableOrdered);

            try { if (removed) UnityEngine.Object.Destroy(removed.gameObject); } catch { }
            Server_SendLootboxState(null, inv);
        }

        private static readonly Collider[] _corpseScanBuf = new Collider[64];
        private const QueryTriggerInteraction QTI = QueryTriggerInteraction.Collide;
        private const int LAYER_MASK_ANY = ~0;


        private void TryClientRemoveNearestAICorpse(Vector3 pos, float radius)
        {
            if (!networkStarted || IsServer) return;

            try
            {
                CharacterMainControl best = null;
                float bestSqr = radius * radius;

                // 1) 先用你已有的 aiById 做 O(n) 精确筛选（不扫全场）
                try
                {
                    foreach (var kv in aiById)
                    {
                        var cmc = kv.Value;
                        if (!cmc || cmc.IsMainCharacter) continue;

                        bool isAI = cmc.GetComponent<AICharacterController>() != null
                                 || cmc.GetComponent<NetAiTag>() != null;
                        if (!isAI) continue;

                        var p = cmc.transform.position; p.y = 0f;
                        var q = pos; q.y = 0f;
                        float d2 = (p - q).sqrMagnitude;
                        if (d2 < bestSqr) { best = cmc; bestSqr = d2; }
                    }
                }
                catch { }

                // 2) 仍未命中时，用“局部物理探测”替代全场景扫描（无 GC）
                if (!best)
                {
                    int n = Physics.OverlapSphereNonAlloc(pos, radius, _corpseScanBuf, LAYER_MASK_ANY, QTI);
                    for (int i = 0; i < n; i++)
                    {
                        var c = _corpseScanBuf[i];
                        if (!c) continue;

                        var cmc = c.GetComponentInParent<CharacterMainControl>();
                        if (!cmc || cmc.IsMainCharacter) continue;

                        bool isAI = cmc.GetComponent<AICharacterController>() != null
                                 || cmc.GetComponent<NetAiTag>() != null;
                        if (!isAI) continue;

                        var p = cmc.transform.position; p.y = 0f;
                        var q = pos; q.y = 0f;
                        float d2 = (p - q).sqrMagnitude;
                        if (d2 < bestSqr) { best = cmc; bestSqr = d2; }
                    }
                }

      
                if (best)
                {
                    DamageInfo DamageInfo = new DamageInfo { armorBreak = 999f, damageValue = 9999f, fromWeaponItemID = CharacterMainControl.Main.CurrentHoldItemAgent.Item.TypeID, damageType = DamageTypes.normal, fromCharacter = CharacterMainControl.Main, finalDamage = 9999f, toDamageReceiver = best.mainDamageReceiver };
                    EXPManager.AddExp(Traverse.Create(best.Health).Field<Item>("item").Value.GetInt("Exp", 0));
                    
                    // 经验共享获取，共享击杀lol

                    //best.Health.Hurt(DamageInfo);
                    best.Health.OnDeadEvent.Invoke(DamageInfo);
                    TryFireOnDead(best.Health, DamageInfo);

                    try
                    {
                        var tag = best.GetComponent<NetAiTag>();
                        if (tag != null)
                        {
                            if (_cliAiDeathFxOnce.Add(tag.aiId))
                                Client_PlayAiDeathFxAndSfx(best);
                        }
                    }
                    catch {  }

                    UnityEngine.Object.Destroy(best.gameObject);
                }
            }
            catch { }
        }

        public static bool TryFireOnDead(Health health, DamageInfo di)
        {
            try
            {
                // OnDead 是 static event<Action<Health, DamageInfo>>
                var fi = AccessTools.Field(typeof(Health), "OnDead");
                if (fi == null)
                {
                    UnityEngine.Debug.LogError("[HEALTH] 找不到 OnDead 字段（可能是自定义 add/remove 事件）");
                    return false;
                }

                var del = fi.GetValue(null) as Action<Health, DamageInfo>;
                if (del == null)
                {
                    // 没有任何订阅者就不会触发
                    return false;
                }

                del.Invoke(health, di);
                return true;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[HEALTH] 触发 OnDead 失败: " + e);
                return false;
            }
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

            if (sceneVoteActive)
                _spectatorEndOnVotePending = true;

            return true; // 告诉前缀：拦截结算，启用观战
        }

        private void BuildSpectateList(CharacterMainControl exclude)
        {
            _spectateList.Clear();

            string mySceneId = localPlayerStatus != null ? localPlayerStatus.SceneId : null;
            if (string.IsNullOrEmpty(mySceneId))
                ComputeIsInGame(out mySceneId);
            var myK = CanonicalizeSceneId(mySceneId);

            int cand = 0, kept = 0;

            if (IsServer)
            {
                foreach (var kv in remoteCharacters)
                {
                    var go = kv.Value;
                    var cmc = go ? go.GetComponent<CharacterMainControl>() : null;
                    if (!IsAlive(cmc) || cmc == exclude) continue;
                    cand++;

                    string peerScene = null;
                    if (!_srvPeerScene.TryGetValue(kv.Key, out peerScene) && playerStatuses.TryGetValue(kv.Key, out var st))
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
                    if (!IsAlive(cmc) || cmc == exclude) continue;
                    cand++;

                    //  先从 clientPlayerStatuses 拿 SceneId
                    string peerScene = null;
                    if (clientPlayerStatuses.TryGetValue(kv.Key, out var st))
                        peerScene = st?.SceneId;

                    //  兜底：再从 _cliLastSceneIdByPlayer 回忆一次
                    if (string.IsNullOrEmpty(peerScene))
                        _cliLastSceneIdByPlayer.TryGetValue(kv.Key, out peerScene);

                    if (AreSameMap(mySceneId, peerScene))
                    {
                        _spectateList.Add(cmc);
                        kept++;
                    }
                }
            }

            Debug.Log($"[SPECTATE] 候选={cand}, 同图保留={kept}, mySceneId={mySceneId} (canon={myK})");
        }

        //说实话这个方法没多大用   //说实话这个方法没多大用   //说实话这个方法没多大用   //说实话这个方法没多大用
        private static string CanonicalizeSceneId(string id)
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

        private static bool AreSameMap(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return a == b;
            return CanonicalizeSceneId(a) == CanonicalizeSceneId(b);
        }

        // —— 用已有字典反查该 CMC 是否属于“本地图”的远端玩家 —— 
        private bool IsInSameScene(CharacterMainControl cmc)
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
                        if (clientPlayerStatuses.TryGetValue(kv.Key, out var st) && st != null)
                            return st.SceneId == mySceneId;
                        return false;
                    }
                }
            }
            return false;
        }



        public bool IsAlive(CharacterMainControl cmc)
        {
            if (!cmc) return false;
            try { return cmc.Health != null && cmc.Health.CurrentHealth > 0.001f; } catch { return false; }
        }

        private void SpectateNext()
        {
            if (_spectateList.Count == 0) return;
            _spectateIdx = (_spectateIdx + 1) % _spectateList.Count;
            if (GameCamera.Instance) GameCamera.Instance.SetTarget(_spectateList[_spectateIdx]);
        }

        private void SpectatePrev()
        {
            if (_spectateList.Count == 0) return;
            _spectateIdx = (_spectateIdx - 1 + _spectateList.Count) % _spectateList.Count;
            if (GameCamera.Instance) GameCamera.Instance.SetTarget(_spectateList[_spectateIdx]);
        }

        // —— 仅统计“本地图”的存活玩家；本机已死时就看同图是否还有活人 —— 
        private bool AllPlayersDead()
        {
            // 自己的 SceneId（拿不到就 Compute 一次）懂了吗sans看到这
            string mySceneId = localPlayerStatus != null ? localPlayerStatus.SceneId : null;
            if (string.IsNullOrEmpty(mySceneId))
                ComputeIsInGame(out mySceneId);

            // 拿不到 SceneId 的极端情况：沿用旧逻辑（不按同图过滤），避免误杀
            if (string.IsNullOrEmpty(mySceneId))
            {
                int alive = 0;
                if (IsAlive(CharacterMainControl.Main)) alive++;
                if (IsServer)
                    foreach (var kv in remoteCharacters) { var cmc = kv.Value ? kv.Value.GetComponent<CharacterMainControl>() : null; if (IsAlive(cmc)) alive++; }
                else
                    foreach (var kv in clientRemoteCharacters) { var cmc = kv.Value ? kv.Value.GetComponent<CharacterMainControl>() : null; if (IsAlive(cmc)) alive++; }
                return alive == 0;
            }

            int aliveSameScene = 0;

            // 本机（通常观战时本机已死，这里自然为 0）
            if (IsAlive(CharacterMainControl.Main)) aliveSameScene++;

            if (IsServer)
            {
                foreach (var kv in remoteCharacters)
                {
                    var cmc = kv.Value ? kv.Value.GetComponent<CharacterMainControl>() : null;
                    if (!IsAlive(cmc)) continue;

                    string peerScene = null;
                    if (!_srvPeerScene.TryGetValue(kv.Key, out peerScene) && playerStatuses.TryGetValue(kv.Key, out var st))
                        peerScene = st?.SceneId;

                    if (AreSameMap(mySceneId, peerScene)) aliveSameScene++;
                }
            }
            else
            {
                foreach (var kv in clientRemoteCharacters)
                {
                    var cmc = kv.Value ? kv.Value.GetComponent<CharacterMainControl>() : null;
                    if (!IsAlive(cmc)) continue;

                    string peerScene = clientPlayerStatuses.TryGetValue(kv.Key, out var st) ? st?.SceneId : null;
                    if (AreSameMap(mySceneId, peerScene)) aliveSameScene++;
                }
            }

            bool none = (aliveSameScene <= 0);
            if (none)
                Debug.Log("[SPECTATE] 本地图无人存活 → 退出观战并触发结算");
            return none;
        }



        private void EndSpectatorAndShowClosure()
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

        public void Client_RequestBeginSceneVote(
        string targetId, string curtainGuid,
        bool notifyEvac, bool saveToFile,
        bool useLocation, string locationName)
        {
            if (!networkStarted || IsServer || connectedPeer == null) return;

            var w = new NetDataWriter();
            w.Put((byte)Op.SCENE_VOTE_REQ);
            w.Put(targetId);
            w.Put(PackFlags(!string.IsNullOrEmpty(curtainGuid), useLocation, notifyEvac, saveToFile));
            if (!string.IsNullOrEmpty(curtainGuid)) w.Put(curtainGuid);
            w.Put(locationName ?? string.Empty);

            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
        }

        static bool FmodEventExists(string path)
        {
            try
            {
                var sys = FMODUnity.RuntimeManager.StudioSystem;
                if (!sys.isValid()) return false;
                FMOD.Studio.EventDescription desc;
                var r = sys.getEvent(path, out desc);
                return r == FMOD.RESULT.OK && desc.isValid();
            }
            catch { return false; }
        }
        private void Client_PlayLocalShotFx(ItemAgent_Gun gun, Transform muzzleTf, int weaponType)
        {
            if (!muzzleTf) return;

            GameObject ResolveMuzzlePrefab()
            {
                GameObject fxPfb = null;
                _muzzleFxCacheByWeaponType.TryGetValue(weaponType, out fxPfb);
                if (!fxPfb && gun && gun.GunItemSetting) fxPfb = gun.GunItemSetting.muzzleFxPfb;
                if (!fxPfb) fxPfb = defaultMuzzleFx;
                return fxPfb;
            }

            void PlayFxGameObject(GameObject go)
            {
                if (!go) return;
                var ps = go.GetComponent<ParticleSystem>();
                if (ps)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play(true);
                }
                else
                {
                    go.SetActive(false);
                    go.SetActive(true);
                }
            }

            // ========== 有“枪实例”→ 走池化，零 GC 可能？==========
            if (gun != null)
            {
                if (!_muzzleFxByGun.TryGetValue(gun, out var fxGo) || !fxGo)
                {
                    var fxPfb = ResolveMuzzlePrefab();
                    if (fxPfb)
                    {
                        fxGo = Instantiate(fxPfb, muzzleTf, false);
                        fxGo.transform.localPosition = Vector3.zero;
                        fxGo.transform.localRotation = Quaternion.identity;
                        _muzzleFxByGun[gun] = fxGo;
                    }
                }
                PlayFxGameObject(fxGo);

                if (!_shellPsByGun.TryGetValue(gun, out var shellPs) || shellPs == null)
                {
                    try { shellPs = (ParticleSystem)FI_ShellParticle?.GetValue(gun); } catch { shellPs = null; }
                    _shellPsByGun[gun] = shellPs;
                }
                try { if (shellPs) shellPs.Emit(1); } catch { }

                TryStartVisualRecoil_NoAlloc(gun);
                return;
            }

            // ========== 没有“枪实例”（比如远端首包）→ 一次性临时 FX（低频） ==========
            var pfb = ResolveMuzzlePrefab();
            if (pfb)
            {
                var tempFx = Instantiate(pfb, muzzleTf, false);
                tempFx.transform.localPosition = Vector3.zero;
                tempFx.transform.localRotation = Quaternion.identity;

                var ps = tempFx.GetComponent<ParticleSystem>();
                if (ps) ps.Play(true);
                else { tempFx.SetActive(false); tempFx.SetActive(true); }

                Destroy(tempFx, 0.5f);
            }
        }

        private void TryStartVisualRecoil_NoAlloc(ItemAgent_Gun gun)
        {
            if (!gun) return;
            try
            {
                MI_StartVisualRecoil?.Invoke(gun, null);
                return;
            }
            catch { }

            try { FI_RecoilBack?.SetValue(gun, true); } catch { }
        }

        sealed class RefEq<T> : IEqualityComparer<T> where T : class
        {
            public bool Equals(T a, T b) => ReferenceEquals(a, b);
            public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        // 服务器：容器快照广播的“抑制窗口”表 sans可用
        private readonly Dictionary<ItemStatsSystem.Inventory, float> _srvLootMuteUntil
            = new Dictionary<ItemStatsSystem.Inventory, float>(new RefEq<ItemStatsSystem.Inventory>());

        public bool Server_IsLootMuted(ItemStatsSystem.Inventory inv)
        {
            if (!inv) return false;
            if (_srvLootMuteUntil.TryGetValue(inv, out var until))
            {
                if (Time.time < until) return true;
                _srvLootMuteUntil.Remove(inv); // 过期清理
            }
            return false;
        }

        public void Server_MuteLoot(ItemStatsSystem.Inventory inv, float seconds)
        {
            if (!inv) return;
            _srvLootMuteUntil[inv] = Time.time + Mathf.Max(0.01f, seconds);
        }

        // 是否已经上报过“本轮生命”的尸体/战利品（= 主机已可生成，不要再上报）
        internal bool _cliCorpseTreeReported = false;

        // 正在执行“补发死亡”的 OnDead 触发（仅作上下文标记，便于补丁识别来源）
        internal bool _cliInEnsureSelfDeathEmit = false;

        //Scene Gate 
        private volatile bool _cliSceneGateReleased = false;
        private string _cliGateSid = null;
        private float _cliGateDeadline = 0f;
        private float _cliGateSeverDeadline = 0f;

        private bool _srvSceneGateOpen = false;
        private string _srvGateSid = null;
        // 记录已经“举手”的客户端（用 EndPoint 字符串，与现有 PlayerStatus 保持一致）
        private readonly HashSet<string> _srvGateReadyPids = new HashSet<string>();


        public UniTask AppendSceneGate(UniTask original)
        {
            return Internal();

            async UniTask Internal()
            {
                // 先等待原本的其他初始化
                await original;

                try
                {
                    if (!networkStarted) return;

                    // 只在“关卡场景”里做门控；LevelManager 在关卡中才存在
                    // （这里不去使用 waitForInitializationList / LoadScene）
 
                        await Client_SceneGateAsync();
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[SCENE-GATE] " + e);
                }
            }
        }

        public async UniTask Client_SceneGateAsync()
        {
            if (!networkStarted || IsServer) return;

            // 1) 等到握手建立（高性能机器上 StartInit 可能早于握手）
            float connectDeadline = Time.realtimeSinceStartup + 8f;
            while (connectedPeer == null && Time.realtimeSinceStartup < connectDeadline)
                await Cysharp.Threading.Tasks.UniTask.Delay(100);

            // 2) 重置释放标记
            _cliSceneGateReleased = false;

            string sid = _cliGateSid;
            if (string.IsNullOrEmpty(sid))
                sid = TryGuessActiveSceneId();
            _cliGateSid = sid; 

            // 4) 尝试上报 READY（握手稍晚的情况，后面会重试一次）
            if (connectedPeer != null)
            {
                writer.Reset();
                writer.Put((byte)Op.SCENE_GATE_READY);
                writer.Put(localPlayerStatus != null ? localPlayerStatus.EndPoint : "");
                writer.Put(sid ?? "");
                connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
            }

            // 5) 若此时仍未连上，后台短暂轮询直到拿到 peer 后补发 READY（最多再等 5s）
            float retryDeadline = Time.realtimeSinceStartup + 5f;
            while (connectedPeer == null && Time.realtimeSinceStartup < retryDeadline)
            {
                await Cysharp.Threading.Tasks.UniTask.Delay(200);
                if (connectedPeer != null)
                {
                    writer.Reset();
                    writer.Put((byte)Op.SCENE_GATE_READY);
                    writer.Put(localPlayerStatus != null ? localPlayerStatus.EndPoint : "");
                    writer.Put(sid ?? "");
                    connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
                    break;
                }
            }

            _cliGateDeadline = Time.realtimeSinceStartup + 100f; // 可调超时（防死锁）吃保底

            while (!_cliSceneGateReleased && Time.realtimeSinceStartup < _cliGateDeadline)
            {
                try { SceneLoader.LoadingComment = "[Coop] 等待主机完成加载… (如迟迟没有进入等待100秒后自动进入)"; } catch { }
                await UniTask.Delay(100);
            }


            Client_ReportSelfHealth_IfReadyOnce();
            try { SceneLoader.LoadingComment = "主机已完成，正在进入…"; } catch { }
        }

        // 主机：自身初始化完成 → 开门；已举手的立即放行；之后若有迟到的 READY，也会单放行
        public async UniTask Server_SceneGateAsync()
        {
            if (!IsServer || !networkStarted) return;

            _srvGateSid = TryGuessActiveSceneId();
            _srvSceneGateOpen = false;
            _cliGateSeverDeadline = Time.realtimeSinceStartup + 15f;

            while (Time.realtimeSinceStartup < _cliGateSeverDeadline)
            {
                await UniTask.Delay(100);
            }

            _srvSceneGateOpen = true;

            // 放行已经举手的所有客户端
            if (playerStatuses != null && playerStatuses.Count > 0)
            {
                foreach (var kv in playerStatuses)
                {
                    var peer = kv.Key;
                    var st = kv.Value;
                    if (peer == null || st == null) continue;
                    if (_srvGateReadyPids.Contains(st.EndPoint))
                        Server_SendGateRelease(peer, _srvGateSid);
                }
            }

            // 主机不阻塞：之后若有 SCENE_GATE_READY 迟到，就在接收处即刻单独放行 目前不想去写也没啥毛病
        }

        private void Server_SendGateRelease(NetPeer peer, string sid)
        {
            if (peer == null) return;
            var w = new NetDataWriter();
            w.Put((byte)Op.SCENE_GATE_RELEASE);
            w.Put(sid ?? "");
            peer.Send(w, DeliveryMethod.ReliableOrdered);
        }

        // 根据当前激活场景估计 SceneID（不调用 LoadScene）
        private string TryGuessActiveSceneId()
        {
            return sceneTargetId;
        }

        // 清空角色手上/手边 socket 下的所有 ItemAgent，避免复制主机的武器外观 这个清理物品或者装备已经写过很多地方了已经开始事山雏形
        private static void StripAllHandItems(CharacterMainControl cmc)
        {
            if (!cmc) return;
            var model = cmc.characterModel;
            if (!model) return;

            void KillChildren(Transform root)
            {
                if (!root) return;
                try
                {
                    foreach (var g in root.GetComponentsInChildren<ItemAgent_Gun>(true))
                        if (g && g.gameObject) UnityEngine.Object.Destroy(g.gameObject);

                    foreach (var m in root.GetComponentsInChildren<ItemAgent_MeleeWeapon>(true))
                        if (m && m.gameObject) UnityEngine.Object.Destroy(m.gameObject);

                    foreach (var x in root.GetComponentsInChildren<DuckovItemAgent>(true))
                        if (x && x.gameObject) UnityEngine.Object.Destroy(x.gameObject);

                    var baseType = typeof(Component).Assembly.GetType("ItemAgent");
                    if (baseType != null)
                    {
                        foreach (var c in root.GetComponentsInChildren(baseType, true))
                            if (c is Component comp && comp.gameObject) UnityEngine.Object.Destroy(comp.gameObject);
                    }
                }
                catch { }
            }

            try { KillChildren(model.RightHandSocket); } catch { }
            try { KillChildren(model.LefthandSocket); } catch { }
            try { KillChildren(model.MeleeWeaponSocket); } catch { }

        }

        // 2 秒内重复轻量尝试，把血条/UI竞态压掉（每 0.2s 一次，共 10 次） 基本废弃掉了啊哈哈哈啊哈
        private IEnumerator Server_EnsureBarRoutine(NetPeer peer, GameObject go)
        {
            const int attempts = 10;
            const float interval = 0.2f;
            for (int i = 0; i < attempts; i++)
            {
                if (!IsServer || !networkStarted || !go) yield break;
                // 这里面会：绑定 Health↔CMC、兜底 Max=40、确保 showHealthBar + RequestHealthBar，
                // 并在有 _srvPendingHp 时用真实值覆写，然后广播一帧
                Server_HookOneHealth(peer, go); 
                yield return new WaitForSeconds(interval);
            }
        }

        // ==== 客户端“进图后 6 秒无敌”本地计时 ==== 基本废弃掉了啊哈哈哈啊哈
        private float _cliSpawnProtectUntil = 0f;

        // 启动保护（只改本地客户端计时，不与服务器/peer 交互）
        public void Client_ArmSpawnProtection(float seconds)
        {
            if (seconds <= 0f) return;
            _cliSpawnProtectUntil = Time.realtimeSinceStartup + seconds;
        }

        // 是否仍在保护期内
        public bool Client_IsSpawnProtected()
        {
            return Time.realtimeSinceStartup < _cliSpawnProtectUntil;
        }

        public NetPeer Server_FindOwnerPeerByHealth(Health h)
        {
            if (h == null) return null;
            CharacterMainControl cmc = null;
            try { cmc = h.TryGetCharacter(); } catch { }
            if (!cmc) { try { cmc = h.GetComponentInParent<CharacterMainControl>(); } catch { } }
            if (!cmc) return null;

            foreach (var kv in remoteCharacters) // remoteCharacters: NetPeer -> GameObject（主机维护）
            {
                if (kv.Value == cmc.gameObject) return kv.Key;
            }
            return null;
        }

        // 主机：把 DamageInfo（简化字段）发给拥有者客户端，让其本地执行 Hurt
        public void Server_ForwardHurtToOwner(NetPeer owner, global::DamageInfo di)
        {
            if (!IsServer || owner == null) return;

            var w = new NetDataWriter();
            w.Put((byte)Op.PLAYER_HURT_EVENT);

            // 参照你现有近战上报字段进行对称序列化
            w.Put(di.damageValue);
            w.Put(di.armorPiercing);
            w.Put(di.critDamageFactor);
            w.Put(di.critRate);
            w.Put(di.crit);
            w.PutV3cm(di.damagePoint);
            w.PutDir(di.damageNormal.sqrMagnitude < 1e-6f ? Vector3.up : di.damageNormal.normalized);
            w.Put(di.fromWeaponItemID);
            w.Put(di.bleedChance);
            w.Put(di.isExplosion);
   
            owner.Send(w, DeliveryMethod.ReliableOrdered);
        }



        private void Client_ApplySelfHurtFromServer(NetPacketReader r)
        {
            try
            {
                // 反序列化与上面写入顺序保持一致
                float dmg = r.GetFloat();
                float ap = r.GetFloat();
                float cdf = r.GetFloat();
                float cr = r.GetFloat();
                int crit = r.GetInt();
                Vector3 hit = r.GetV3cm();
                Vector3 nrm = r.GetDir();
                int wid = r.GetInt();
                float bleed = r.GetFloat();
                bool boom = r.GetBool();

                var main = LevelManager.Instance ? LevelManager.Instance.MainCharacter : null;
                if (!main || main.Health == null) return;

                // 构造 DamageInfo（攻击者此处可不给/或给 main，自身并不影响结算核心）
                var di = new DamageInfo(main)
                {
                    damageValue = dmg,
                    armorPiercing = ap,
                    critDamageFactor = cdf,
                    critRate = cr,
                    crit = crit,
                    damagePoint = hit,
                    damageNormal = nrm,
                    fromWeaponItemID = wid,
                    bleedChance = bleed,
                    isExplosion = boom
                };

                // 记录“最近一次本地受击时间”，便于你已有的 echo 抑制逻辑
                _cliLastSelfHurtAt = Time.time;

                main.Health.Hurt(di);

                Client_ReportSelfHealth_IfReadyOnce(); 

            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[CLIENT] apply self hurt from server failed: " + e);
            }
        }













    }

}

