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

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace EscapeFromDuckovCoopMod.Jobs
{
    /// <summary>
    /// 【优化】AI 种子计算 Job - 使用 Unity Job System 并行计算
    /// 参考 Fika 的 IJobParallelFor 实现
    /// </summary>
    [BurstCompile] // Burst 编译器加速（可选，但强烈推荐）
    public struct AISeedCalculationJob : IJobParallelFor
    {
        [ReadOnly]
        public int sceneSeed;

        [ReadOnly]
        public NativeArray<int> rootIds;

        [WriteOnly]
        public NativeArray<int> calculatedSeeds;

        /// <summary>
        /// 并行执行的核心逻辑
        /// </summary>
        public void Execute(int index)
        {
            // 从 AITool.DeriveSeed 提取的算法（纯数学计算，不依赖 Unity API）
            int rootId = rootIds[index];

            // 简单的种子生成算法（根据实际的 AITool.DeriveSeed 调整）
            // 这里使用 XOR 和乘法混合
            int seed = sceneSeed ^ rootId;
            seed = seed * 1103515245 + 12345; // 线性同余生成器
            seed = (seed / 65536) % 32768;

            calculatedSeeds[index] = seed;
        }
    }

    /// <summary>
    /// 【优化】AI 种子对计算 Job - 计算 (idA, idB) 双映射
    /// </summary>
    [BurstCompile]
    public struct AISeedPairCalculationJob : IJobParallelFor
    {
        [ReadOnly]
        public int sceneSeed;

        [ReadOnly]
        public NativeArray<int> rootIdsA; // 主ID

        [ReadOnly]
        public NativeArray<int> rootIdsB; // 兼容ID

        [WriteOnly]
        public NativeArray<int> calculatedSeedsA;

        [WriteOnly]
        public NativeArray<int> calculatedSeedsB;

        public void Execute(int index)
        {
            int idA = rootIdsA[index];
            int idB = rootIdsB[index];

            // 使用相同的种子生成算法
            int seedA = sceneSeed ^ idA;
            seedA = seedA * 1103515245 + 12345;
            seedA = (seedA / 65536) % 32768;

            int seedB = sceneSeed ^ idB;
            seedB = seedB * 1103515245 + 12345;
            seedB = (seedB / 65536) % 32768;

            calculatedSeedsA[index] = seedA;
            calculatedSeedsB[index] = seedB;
        }
    }
}

