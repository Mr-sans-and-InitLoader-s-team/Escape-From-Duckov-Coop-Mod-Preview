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

using LiteNetLib;

namespace EscapeFromDuckovCoopMod.Net;

/// <summary>
/// Op 操作码优先级映射系统（参考 Fika 架构）
/// </summary>
public static class OpPriority
{
    /// <summary>
    /// 获取 Op 对应的优先级
    /// </summary>
    public static PacketPriority GetPriority(this Op op)
    {
        return op switch
        {
            // ====== Critical (关键功能 - ReliableOrdered) ======

            // 投票系统 - 必须送达且有序
            Op.SCENE_VOTE_START => PacketPriority.Critical,
            Op.SCENE_VOTE_REQ => PacketPriority.Critical,
            Op.SCENE_BEGIN_LOAD => PacketPriority.Critical,
            Op.SCENE_CANCEL => PacketPriority.Critical,
            Op.SCENE_READY_SET => PacketPriority.Critical,
            Op.SCENE_GATE_READY => PacketPriority.Critical,
            Op.SCENE_GATE_RELEASE => PacketPriority.Critical,

            // 伤害和死亡 - 必须送达且有序
            Op.PLAYER_HURT_EVENT => PacketPriority.Critical,
            Op.PLAYER_DEAD_TREE => PacketPriority.Critical,
            Op.AI_HEALTH_REPORT => PacketPriority.Critical,
            Op.AI_HEALTH_SYNC => PacketPriority.Critical,
            Op.MELEE_HIT_REPORT => PacketPriority.Critical,
            Op.ENV_HURT_REQUEST => PacketPriority.Critical,
            Op.ENV_HURT_EVENT => PacketPriority.Critical,
            Op.ENV_DEAD_EVENT => PacketPriority.Critical,

            // 武器射击和手榴弹 - 必须送达且有序
            Op.FIRE_REQUEST => PacketPriority.Critical,
            Op.FIRE_EVENT => PacketPriority.Critical,
            Op.GRENADE_THROW_REQUEST => PacketPriority.Critical,
            Op.GRENADE_SPAWN => PacketPriority.Critical,
            Op.GRENADE_EXPLODE => PacketPriority.Critical,
            Op.MELEE_ATTACK_REQUEST => PacketPriority.Critical,
            Op.MELEE_ATTACK_SWING => PacketPriority.Critical,

            // 物品交互 - 必须送达且有序
            Op.ITEM_PICKUP_REQUEST => PacketPriority.Critical,
            Op.ITEM_DROP_REQUEST => PacketPriority.Critical,
            Op.ITEM_DESPAWN => PacketPriority.Critical,
            Op.LOOT_REQ_OPEN => PacketPriority.Critical,
            Op.LOOT_REQ_PUT => PacketPriority.Critical,
            Op.LOOT_REQ_TAKE => PacketPriority.Critical,
            Op.LOOT_PUT_OK => PacketPriority.Critical,
            Op.LOOT_TAKE_OK => PacketPriority.Critical,
            Op.LOOT_DENY => PacketPriority.Critical,
            Op.LOOT_REQ_SLOT_PLUG => PacketPriority.Critical,
            Op.LOOT_REQ_SLOT_UNPLUG => PacketPriority.Critical,
            Op.LOOT_REQ_SPLIT => PacketPriority.Critical,

            // 门交互 - 必须送达且有序
            Op.DOOR_REQ_SET => PacketPriority.Critical,
            Op.DOOR_STATE => PacketPriority.Critical,

            // 玩家连接状态 - 必须送达且有序
            Op.REMOTE_CREATE => PacketPriority.Critical,
            Op.REMOTE_DESPAWN => PacketPriority.Critical,
            Op.DISCOVER_REQUEST => PacketPriority.Critical,
            Op.DISCOVER_RESPONSE => PacketPriority.Critical,

            // 🎨 玩家外观 - 可能包含大 JSON，必须用 ReliableOrdered 支持分片
            // 异步传输，不阻塞场景同步和投票流程
            Op.PLAYER_APPEARANCE => PacketPriority.Critical,

            // ====== Important (重要状态 - ReliableSequenced) ======
            // 只保留最新状态即可

            // 玩家状态更新
            Op.PLAYER_STATUS_UPDATE => PacketPriority.Important,
            Op.CLIENT_STATUS_UPDATE => PacketPriority.Important,
            Op.PLAYER_HEALTH_REPORT => PacketPriority.Important,
            Op.AUTH_HEALTH_SELF => PacketPriority.Important,
            Op.AUTH_HEALTH_REMOTE => PacketPriority.Important,

            // ✅ SCENE_READY - 降回 Important，已移除 faceJson，现在是小包
            // 场景同步与外观数据解耦，确保投票不会因大包阻塞
            Op.SCENE_READY => PacketPriority.Important,

            // 🛡️ 装备更新 - 升级为 Critical，防止场景切换时丢失导致白模
            // 装备变更是低频事件，且对外观至关重要，必须保证送达
            Op.EQUIPMENT_UPDATE => PacketPriority.Critical,
            Op.PLAYERWEAPON_UPDATE => PacketPriority.Critical,

            // Buff 系统
            Op.HOST_BUFF_PROXY_APPLY => PacketPriority.Important,
            Op.PLAYER_BUFF_SELF_APPLY => PacketPriority.Important,

            // 环境同步
            Op.ENV_SYNC_REQUEST => PacketPriority.Important,
            // ✅ ENV_SYNC_STATE - 升级为 Critical，包含大量数据（LootBox、Door、Destructible）
            // 可能超过 MTU，必须用 ReliableOrdered 支持分片
            Op.ENV_SYNC_STATE => PacketPriority.Critical,

            // ====== Normal (普通事件 - ReliableUnordered) ======
            // 必须送达但顺序无关

            // 物品生成
            Op.ITEM_SPAWN => PacketPriority.Normal,
            Op.DEAD_LOOT_SPAWN => PacketPriority.Normal,
            Op.DEAD_LOOT_DESPAWN => PacketPriority.Normal,
            Op.LOOT_STATE => PacketPriority.Normal,
            Op.AUDIO_EVENT => PacketPriority.Normal,

            // AI 相关
            Op.AI_SEED_SNAPSHOT => PacketPriority.Normal,
            Op.AI_SEED_PATCH => PacketPriority.Normal,
            Op.AI_LOADOUT_SNAPSHOT => PacketPriority.Normal,
            Op.AI_FREEZE_TOGGLE => PacketPriority.Normal,
            Op.AI_NAME_ICON => PacketPriority.Normal,
            Op.AI_ATTACK_TELL => PacketPriority.Normal,

            // ====== Frequent (高频更新 - Unreliable) ======
            // 可以丢弃，下一帧覆盖

            // 位置同步 - 最关键的优化点！
            Op.POSITION_UPDATE => PacketPriority.Frequent,

            // 动画同步
            Op.ANIM_SYNC => PacketPriority.Frequent,
            Op.AI_ANIM_SNAPSHOT => PacketPriority.Frequent,

            // AI 位置同步
            Op.AI_TRANSFORM_SNAPSHOT => PacketPriority.Frequent,
            Op.AI_ATTACK_SWING => PacketPriority.Frequent,

            // 默认：使用 Critical 确保安全
            _ => PacketPriority.Critical
        };
    }

    /// <summary>
    /// 获取 Op 对应的 DeliveryMethod
    /// </summary>
    public static DeliveryMethod GetDeliveryMethod(this Op op)
    {
        return op.GetPriority().GetDeliveryMethod();
    }

    /// <summary>
    /// 获取 Op 对应的通道编号
    /// </summary>
    public static byte GetChannelNumber(this Op op)
    {
        return op.GetPriority().GetChannelNumber();
    }

    /// <summary>
    /// 判断 Op 是否需要可靠传输
    /// </summary>
    public static bool IsReliable(this Op op)
    {
        return op.GetPriority().IsReliable();
    }

    /// <summary>
    /// 获取优先级统计信息（调试用）
    /// </summary>
    public static string GetPriorityStats()
    {
        var stats = new System.Text.StringBuilder();
        stats.AppendLine("=== Op 优先级分布 ===\n");

        var allOps = System.Enum.GetValues(typeof(Op));
        var criticalOps = new System.Collections.Generic.List<Op>();
        var importantOps = new System.Collections.Generic.List<Op>();
        var normalOps = new System.Collections.Generic.List<Op>();
        var frequentOps = new System.Collections.Generic.List<Op>();

        foreach (Op op in allOps)
        {
            var priority = op.GetPriority();
            switch (priority)
            {
                case PacketPriority.Critical:
                    criticalOps.Add(op);
                    break;
                case PacketPriority.Important:
                    importantOps.Add(op);
                    break;
                case PacketPriority.Normal:
                    normalOps.Add(op);
                    break;
                case PacketPriority.Frequent:
                    frequentOps.Add(op);
                    break;
            }
        }

        stats.AppendLine($"Critical (关键 - ReliableOrdered): {criticalOps.Count} 个");
        foreach (var op in criticalOps)
        {
            stats.AppendLine($"  - {op}");
        }

        stats.AppendLine($"\nImportant (重要 - ReliableSequenced): {importantOps.Count} 个");
        foreach (var op in importantOps)
        {
            stats.AppendLine($"  - {op}");
        }

        stats.AppendLine($"\nNormal (普通 - ReliableUnordered): {normalOps.Count} 个");
        foreach (var op in normalOps)
        {
            stats.AppendLine($"  - {op}");
        }

        stats.AppendLine($"\nFrequent (高频 - Unreliable): {frequentOps.Count} 个");
        foreach (var op in frequentOps)
        {
            stats.AppendLine($"  - {op}");
        }

        stats.AppendLine($"\n总计: {allOps.Length} 个 Op");

        return stats.ToString();
    }
}

