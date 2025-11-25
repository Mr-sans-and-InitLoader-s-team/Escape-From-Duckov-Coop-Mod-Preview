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

public class HurtM
{
    private static NetService Service => NetService.Instance;

    private static bool IsServer => Service != null && Service.IsServer;
    private static NetManager netManager => Service?.netManager;
    private static NetDataWriter writer => Service?.writer;
    private static NetPeer connectedPeer => Service?.connectedPeer;
    private static PlayerStatus localPlayerStatus => Service?.localPlayerStatus;

    private static bool networkStarted => Service != null && Service.networkStarted;

    // 主机：收到客户端请求，按本地规则结算，再广播受击，这是个建筑物的伤害处理
    public void Server_HandleEnvHurtRequest(NetPeer sender, NetPacketReader r)
    {
        var id = r.GetUInt();
        var payload = r.GetDamagePayload(); // (dmg, ap, cdf, cr, crit, point, normal, wid, bleed, boom, range)

        var hs = COOPManager.destructible.FindDestructible(id);
        if (!hs) return;

        // 组 DamageInfo（用服务端权威；必要时可以做白名单/射线校验）
        var info = new DamageInfo
        {
            damageValue = payload.dmg * ServerTuning.RemoteMeleeEnvScale, // 建议倍率
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
        try
        {
            hs.dmgReceiver.Hurt(info);
        }
        catch
        {
        }
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
}