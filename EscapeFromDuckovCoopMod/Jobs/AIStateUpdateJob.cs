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

using Unity.Jobs;
using Unity.Collections;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Jobs
{
    /// <summary>
    /// 【优化】使用 Unity Job System 并行更新 AI 状态
    /// 参考 Fika 的实现，利用多核 CPU 提升性能
    /// </summary>
    public struct AIStateUpdateJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<int> aiIds;

        [ReadOnly]
        public NativeArray<Vector3> positions;

        [ReadOnly]
        public NativeArray<Quaternion> rotations;

        [ReadOnly]
        public float deltaTime;

        public void Execute(int index)
        {
            // 注意：这里只做数据处理，不能访问 Unity 主线程对象
            // 实际的 GameObject 更新需要在主线程完成

            // 这里可以做一些计算密集型操作，比如：
            // - 路径计算
            // - 插值计算
            // - 物理预测

            // 示例：位置插值计算
            // Vector3 interpolatedPos = Vector3.Lerp(currentPos, targetPos, deltaTime * speed);
        }
    }

    /// <summary>
    /// 【优化】并行处理 AI 种子计算
    /// </summary>
    public struct AIPathCalculationJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<Vector3> startPositions;

        [ReadOnly]
        public NativeArray<Vector3> targetPositions;

        [WriteOnly]
        public NativeArray<Vector3> calculatedDirections;

        public void Execute(int index)
        {
            // 计算 AI 移动方向
            Vector3 direction = (targetPositions[index] - startPositions[index]).normalized;
            calculatedDirections[index] = direction;
        }
    }

    /// <summary>
    /// 【优化】并行处理多个网络包的反序列化
    /// </summary>
    public struct NetworkPacketDeserializeJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<byte> packetData;

        [ReadOnly]
        public NativeArray<int> packetOffsets;

        [ReadOnly]
        public NativeArray<int> packetLengths;

        // 结果输出
        [WriteOnly]
        public NativeArray<int> decodedIds;

        public void Execute(int index)
        {
            // 从字节数组中解析数据
            int offset = packetOffsets[index];
            int length = packetLengths[index];

            // 示例：读取 int (4 bytes)
            if (length >= 4)
            {
                int id = (packetData[offset] << 0) |
                        (packetData[offset + 1] << 8) |
                        (packetData[offset + 2] << 16) |
                        (packetData[offset + 3] << 24);

                decodedIds[index] = id;
            }
        }
    }
}

