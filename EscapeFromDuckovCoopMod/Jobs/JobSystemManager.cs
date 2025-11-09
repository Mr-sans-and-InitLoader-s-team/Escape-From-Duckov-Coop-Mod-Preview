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

using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Jobs
{
    /// <summary>
    /// 【优化】Unity Job System 管理器
    /// 参考 Fika 的实现，使用 Job System 进行并行计算
    /// </summary>
    public class JobSystemManager : MonoBehaviour
    {
        public static JobSystemManager Instance { get; private set; }

        // Job 句柄列表
        private readonly List<JobHandle> _activeJobHandles = new();

        // 是否启用 Job System（可配置）
        public static bool EnableJobSystem = true;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void LateUpdate()
        {
            // 确保所有 Job 在帧结束前完成
            CompleteAllJobs();
        }

        /// <summary>
        /// 调度 AI 路径计算任务（使用 IJobParallelFor.Schedule）
        /// </summary>
        public JobHandle ScheduleAIPathCalculation(
            NativeArray<Vector3> startPositions,
            NativeArray<Vector3> targetPositions,
            NativeArray<Vector3> resultDirections,
            int innerloopBatchCount = 64)
        {
            if (!EnableJobSystem || startPositions.Length == 0)
                return default;

            var job = new AIPathCalculationJob
            {
                startPositions = startPositions,
                targetPositions = targetPositions,
                calculatedDirections = resultDirections
            };

            // 【修复】IJobParallelFor 使用 Schedule 方法
            var handle = job.Schedule(startPositions.Length, innerloopBatchCount, default);
            _activeJobHandles.Add(handle);

            return handle;
        }

        /// <summary>
        /// 调度网络包反序列化任务
        /// </summary>
        public JobHandle ScheduleNetworkDeserialize(
            NativeArray<byte> packetData,
            NativeArray<int> offsets,
            NativeArray<int> lengths,
            NativeArray<int> resultIds,
            int innerloopBatchCount = 64)
        {
            if (!EnableJobSystem || offsets.Length == 0)
                return default;

            var job = new NetworkPacketDeserializeJob
            {
                packetData = packetData,
                packetOffsets = offsets,
                packetLengths = lengths,
                decodedIds = resultIds
            };

            // 【修复】IJobParallelFor 使用 Schedule 方法
            var handle = job.Schedule(offsets.Length, innerloopBatchCount, default);
            _activeJobHandles.Add(handle);

            return handle;
        }

        /// <summary>
        /// 完成所有活跃的 Job
        /// </summary>
        public void CompleteAllJobs()
        {
            foreach (var handle in _activeJobHandles)
            {
                if (!handle.IsCompleted)
                {
                    handle.Complete();
                }
            }
            _activeJobHandles.Clear();
        }

        /// <summary>
        /// 等待特定 Job 完成
        /// </summary>
        public void CompleteJob(JobHandle handle)
        {
            if (!handle.IsCompleted)
            {
                handle.Complete();
            }
        }

        private void OnDestroy()
        {
            // 清理：确保所有 Job 完成
            CompleteAllJobs();
        }
    }
}

