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

using System;

namespace EscapeFromDuckovCoopMod.Utils;

/// <summary>
/// 指数移动平均（Exponential Moving Average）
/// 用于平滑网络抖动和追踪延迟变化
/// 移植自 Fika-Plugin 项目（MIT License）
/// </summary>
public struct ExponentialMovingAverage
{
    private readonly double _alpha;
    private bool _initialized;

    /// <summary>
    /// 当前平均值
    /// </summary>
    public double Value;

    /// <summary>
    /// 方差
    /// </summary>
    public double Variance;

    /// <summary>
    /// 标准差（用于衡量抖动程度）
    /// </summary>
    public double StandardDeviation;

    /// <summary>
    /// 创建 EMA 实例
    /// </summary>
    /// <param name="n">平滑窗口大小（帧数）。n越大越平滑但响应越慢</param>
    public ExponentialMovingAverage(int n)
    {
        if (n <= 0)
            throw new ArgumentException("窗口大小必须大于0", nameof(n));

        _alpha = 2.0 / (n + 1);
        _initialized = false;
        Value = 0;
        Variance = 0;
        StandardDeviation = 0;
    }

    /// <summary>
    /// 添加新的数据点
    /// </summary>
    /// <param name="newValue">新的观测值</param>
    public void Add(double newValue)
    {
        if (!_initialized)
        {
            // 首次初始化：直接使用新值
            Value = newValue;
            _initialized = true;
            return;
        }

        // 计算增量
        double delta = newValue - Value;

        // 更新 EMA 值
        Value += _alpha * delta;

        // 更新方差（用于计算标准差）
        Variance = (1 - _alpha) * (Variance + _alpha * delta * delta);

        // 更新标准差（抖动程度）
        StandardDeviation = Math.Sqrt(Variance);
    }

    /// <summary>
    /// 重置 EMA 为初始状态
    /// </summary>
    public void Reset()
    {
        _initialized = false;
        Value = 0;
        Variance = 0;
        StandardDeviation = 0;
    }

    /// <summary>
    /// 检查是否已初始化
    /// </summary>
    public readonly bool IsInitialized => _initialized;

    /// <summary>
    /// 获取信噪比（值/标准差）
    /// </summary>
    public readonly double SignalToNoiseRatio
    {
        get
        {
            if (StandardDeviation < 1e-6)
                return double.MaxValue;
            return Math.Abs(Value / StandardDeviation);
        }
    }
}

