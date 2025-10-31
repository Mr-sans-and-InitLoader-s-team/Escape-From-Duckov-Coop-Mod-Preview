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

namespace EscapeFromDuckovCoopMod;

// 量化工具
public static class NetPack
{
    private const float POS_SCALE = 100f;

    public static void PutV3cm(this NetDataWriter w, Vector3 v)
    {
        w.Put((int)Mathf.Round(v.x * POS_SCALE));
        w.Put((int)Mathf.Round(v.y * POS_SCALE));
        w.Put((int)Mathf.Round(v.z * POS_SCALE));
    }

    public static Vector3 GetV3cm(this NetPacketReader r)
    {
        var inv = 1f / POS_SCALE;
        return new Vector3(r.GetInt() * inv, r.GetInt() * inv, r.GetInt() * inv);
    }


    // 方向：yaw/pitch 各 2 字节（yaw:0..360，pitch:-90..90）
    public static void PutDir(this NetDataWriter w, Vector3 dir)
    {
        if (dir.sqrMagnitude < 1e-8f) dir = Vector3.forward;
        dir.Normalize();
        var pitch = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg; // -90..90
        var yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; // -180..180
        if (yaw < 0) yaw += 360f;

        var qYaw = (ushort)Mathf.Clamp(Mathf.RoundToInt(yaw / 360f * 65535f), 0, 65535);
        var qPitch = (ushort)Mathf.Clamp(Mathf.RoundToInt((pitch + 90f) / 180f * 65535f), 0, 65535);
        w.Put(qYaw);
        w.Put(qPitch);
    }

    public static Vector3 GetDir(this NetPacketReader r)
    {
        var yaw = r.GetUShort() / 65535f * 360f; // 0..360
        var pitch = r.GetUShort() / 65535f * 180f - 90f; // -90..90
        var cy = Mathf.Cos(yaw * Mathf.Deg2Rad);
        var sy = Mathf.Sin(yaw * Mathf.Deg2Rad);
        var cp = Mathf.Cos(pitch * Mathf.Deg2Rad);
        var sp = Mathf.Sin(pitch * Mathf.Deg2Rad);
        var d = new Vector3(sy * cp, sp, cy * cp);
        if (d.sqrMagnitude < 1e-8f) d = Vector3.forward;
        return d;
    }

    // 小范围浮点压缩（可用于 MoveDir/速度等），范围 [-8,8]，分辨率 1/16 可能的sans自己算
    public static void PutSNorm16(this NetDataWriter w, float v)
    {
        var q = Mathf.RoundToInt(Mathf.Clamp(v, -8f, 8f) * 16f);
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
        var dmg = r.GetFloat();
        var ap = r.GetFloat();
        var cdf = r.GetFloat();
        var cr = r.GetFloat();
        var crit = r.GetInt();
        var p = r.GetV3cm();
        var n = r.GetDir();
        var wid = r.GetInt();
        var bleed = r.GetFloat();
        var boom = r.GetBool();
        var rng = r.GetFloat();
        return (dmg, ap, cdf, cr, crit, p, n, wid, bleed, boom, rng);
    }
}