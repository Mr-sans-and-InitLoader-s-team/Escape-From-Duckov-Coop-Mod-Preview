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

public static class NetPackProjectile
{
    public static void PutProjectilePayload(this NetDataWriter w, in ProjectileContext c)
    {
        w.Put(true); // hasPayload
        // 基础
        w.Put(c.damage);
        w.Put(c.critRate);
        w.Put(c.critDamageFactor);
        w.Put(c.armorPiercing);
        w.Put(c.armorBreak);
        // 元素
        w.Put(c.element_Physics);
        w.Put(c.element_Fire);
        w.Put(c.element_Poison);
        w.Put(c.element_Electricity);
        w.Put(c.element_Space);
        // 爆炸/状态
        w.Put(c.explosionRange);
        w.Put(c.explosionDamage);
        w.Put(c.buffChance);
        w.Put(c.bleedChance);
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

        c.damage = r.GetFloat();
        c.critRate = r.GetFloat();
        c.critDamageFactor = r.GetFloat();
        c.armorPiercing = r.GetFloat();
        c.armorBreak = r.GetFloat();

        c.element_Physics = r.GetFloat();
        c.element_Fire = r.GetFloat();
        c.element_Poison = r.GetFloat();
        c.element_Electricity = r.GetFloat();
        c.element_Space = r.GetFloat();

        c.explosionRange = r.GetFloat();
        c.explosionDamage = r.GetFloat();
        c.buffChance = r.GetFloat();
        c.bleedChance = r.GetFloat();

        c.penetrate = r.GetInt();
        c.fromWeaponItemID = r.GetInt();
        return true;
    }
}