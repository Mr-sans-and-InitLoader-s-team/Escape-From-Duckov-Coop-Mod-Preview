namespace EscapeFromDuckovCoopMod.Utils.NetHelper
{
    /// <summary>
    /// 消息优先级枚举
    /// </summary>
    public enum MessagePriority : byte
    {
        /// <summary>关键消息（投票、伤害、交互）- 通道0</summary>
        Critical = 0,

        /// <summary>重要消息（血量、装备）- 通道1</summary>
        Important = 1,

        /// <summary>普通消息（NPC、物品生成）- 通道2</summary>
        Normal = 2,

        /// <summary>高频消息（位置、动画）- 通道3</summary>
        Frequent = 3
    }
}
