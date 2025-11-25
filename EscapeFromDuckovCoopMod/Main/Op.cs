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
    PLAYER_HEALTH_REPORT = 16, // 客户端 -> 主机：上传自己当前(max,curr)
    PLAYER_DAMAGE_FORWARD = 17, // 主机 -> 客户端：把权威命中数据转发给对应玩家
    PLAYER_HEALTH_BROADCAST = 18, // 主机 -> 所有客户端：广播某位玩家血量
    SCENE_VOTE_START = 19, // 主机 -> 全体：开始投票（下发目标 SceneID、Curtain GUID 等）
    SCENE_READY_SET = 20, // 客户端 -> 主机：我切换准备；主机 -> 全体：某人准备状态改变
    SCENE_BEGIN_LOAD = 21, // 主机 -> 全体：统一开始加载
    SCENE_CANCEL = 22, // 主机 -> 全体：取消投票
    SCENE_READY = 23,
    REMOTE_CREATE = 24,
    REMOTE_DESPAWN = 25,

    DOOR_REQ_SET = 206, // 客户端 -> 主机：请求把某个门设为开/关
    DOOR_STATE = 207, // 主机 -> 全体：下发某个门的状态（单条更新）

    LOOT_REQ_SLOT_UNPLUG = 208, // 从某个物品的某个slot里拔出附件
    LOOT_REQ_SLOT_PLUG = 209, // 往某个物品的某个slot里装入附件（随包带附件snapshot）

    SCENE_VOTE_REQ = 210, // 客户端 -> 主机：请求发起场景投票

    AI_SNAPSHOT_REQUEST = 211,
    AI_SNAPSHOT_CHUNK = 212,
    AI_ACTIVATION_REQUEST = 213,
    AI_SPAWN = 214,
    AI_DESPAWN = 215,
    AI_STATE_UPDATE = 216,
    AI_ACTIVATION_STATE = 217,

    DEAD_LOOT_DESPAWN = 247, // 主机 -> 客户端：AI 死亡掉落的箱子被移除（可选，先不强制使用）
    DEAD_LOOT_SPAWN = 248, // 主机 -> 客户端：AI 死亡掉落箱子生成（包含 scene 与变换）

    SCENE_GATE_READY = 228, // 客户端 -> 主机：我已加载完成，正在等待放行
    SCENE_GATE_RELEASE = 229, // 主机 -> 客户端：放行，退出加载界面进入游戏

    PLAYER_HURT_EVENT = 170,

    HOST_BUFF_PROXY_APPLY = 172,
    PLAYER_BUFF_SELF_APPLY = 171,

    AI_HEALTH_REPORT = 174,
    AI_HEALTH_BROADCAST = 175,
    AI_BUFF_REPORT = 176,
    AI_BUFF_BROADCAST = 177,
    ITEM_DROP_SNAPSHOT_REQUEST = 178,
    ITEM_DROP_SNAPSHOT_CHUNK = 179,
    ENV_HURT_REQUEST = 220, // 客户端 -> 主机：请求对某个 HealthSimpleBase 结算一次受击
    ENV_HURT_EVENT = 221, // 主机 -> 全体：某个对象受击（含当前血量与命中点/法线）
    ENV_DEAD_EVENT = 222, // 主机 -> 全体：某个对象死亡（切换视觉）
    SPECTATOR_FORCE_END = 223, // 主机 -> 客户端：强制结束观战并弹出结算
    AUDIO_EVENT = 226, // 双向：广播一次音频事件
    MELEE_ATTACK_REQUEST = 242, // 客户端 -> 主机：近战起手（含命中帧延时+位姿快照），用于播动作/挥空FX
    MELEE_ATTACK_SWING = 243, // 主机 -> 客户端：某玩家开始挥砍（远端播动作/挥空FX）
    DISCOVER_REQUEST = 240,
    DISCOVER_RESPONSE = 241,
    ENV_CLOCK_STATE = 230, // 主机 -> 客户端：广播时间推进
    ENV_WEATHER_STATE = 231, // 主机 -> 客户端：广播天气设定
    ENV_LOOT_CHUNK = 232, // 主机 -> 客户端：下发一段战利品显隐快照
    ENV_DOOR_CHUNK = 233, // 主机 -> 客户端：下发一段门状态快照
    ENV_DESTRUCTIBLE_STATE = 234, // 主机 -> 客户端：下发破坏物快照
    ENV_EXPBARREL_STATE = 235, // 主机 -> 客户端：下发爆炸桶显隐快照

    ENV_SYNC_REQUEST = 245, // 客户端 -> 主机：请求一份环境快照（新连入或场景就绪时）

    LOOT_REQ_SPLIT = 239,

    LOOT_REQ_OPEN = 250, // 客户端 -> 主机：请求容器快照
    LOOT_STATE = 251, // 主机 -> 客户端：下发容器快照（全量）
    LOOT_REQ_PUT = 252, // 客户端 -> 主机：请求“放入”
    LOOT_REQ_TAKE = 253, // 客户端 -> 主机：请求“取出”
    LOOT_PUT_OK = 254, // 主机 -> 发起客户端：确认“放入”成功，附回执 token
    LOOT_TAKE_OK = 255, // 主机 -> 发起客户端：确认“取出”成功 + 返回 Item 快照
    LOOT_DENY = 249 // 主机 -> 发起客户端：拒绝（例如并发冲突/格子无物品/容量不足）
}