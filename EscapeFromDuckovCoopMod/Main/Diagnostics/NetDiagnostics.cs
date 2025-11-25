using System.Collections.Generic;
using LiteNetLib;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public sealed class NetDiagnostics
{
    private const float ReportInterval = 1.0f;

    private readonly Dictionary<Op, MessageStats> _opStats = new();
    private readonly List<Op> _opScratch = new();

    private ulong _lastBytesSent;
    private ulong _lastBytesReceived;
    private ulong _lastPacketsSent;
    private ulong _lastPacketsReceived;
    private bool _hasPrevNetStats;

    private float _accumulator;
    private NetReport _lastReport;

    public static NetDiagnostics Instance { get; } = new();

    private NetDiagnostics()
    {
    }

    public NetReport LastReport => _lastReport;

    public void Reset()
    {
        _opStats.Clear();
        _opScratch.Clear();
        _accumulator = 0f;
        _lastReport = default;
        _hasPrevNetStats = false;
        _lastBytesSent = _lastBytesReceived = 0;
        _lastPacketsSent = _lastPacketsReceived = 0;
    }

    public void RecordInbound(Op op, int payloadBytes)
    {
        var stats = GetOrCreate(op);
        var totalBytes = payloadBytes + 1; // include opcode byte

        stats.InboundBytesTotal += totalBytes;
        stats.InboundPacketsTotal++;

        if (totalBytes > stats.MaxInboundPacketBytes)
        {
            stats.MaxInboundPacketBytes = totalBytes;
            stats.LastInboundPeakTime = Time.realtimeSinceStartup;
        }
    }

    public void RecordOutbound(Op op, int payloadBytes)
    {
        var stats = GetOrCreate(op);
        var totalBytes = payloadBytes + 1;

        stats.OutboundBytesTotal += totalBytes;
        stats.OutboundPacketsTotal++;

        if (totalBytes > stats.MaxOutboundPacketBytes)
        {
            stats.MaxOutboundPacketBytes = totalBytes;
            stats.LastOutboundPeakTime = Time.realtimeSinceStartup;
        }
    }

    public void Update(NetManager? manager, float deltaTime)
    {
        _accumulator += deltaTime;
        if (_accumulator < ReportInterval)
            return;

        _accumulator -= ReportInterval;

        ulong bytesSentDelta = 0;
        ulong bytesRecvDelta = 0;
        ulong packetsSentDelta = 0;
        ulong packetsRecvDelta = 0;
        float packetLoss = 0f;

        if (manager != null)
        {
            var stats = manager.Statistics;
            if (_hasPrevNetStats)
            {
                bytesSentDelta = (ulong)((long)stats.BytesSent - (long)_lastBytesSent);
                bytesRecvDelta = (ulong)((long)stats.BytesReceived - (long)_lastBytesReceived);
                packetsSentDelta = (ulong)(stats.PacketsSent - (long)_lastPacketsSent);
                packetsRecvDelta = (ulong)(stats.PacketsReceived - (long)_lastPacketsReceived);
            }

            _lastBytesSent = (ulong)stats.BytesSent;
            _lastBytesReceived = (ulong)stats.BytesReceived;
            _lastPacketsSent = (ulong)stats.PacketsSent;
            _lastPacketsReceived = (ulong)stats.PacketsReceived;
            _hasPrevNetStats = true;
            packetLoss = stats.PacketLoss;
        }
        else
        {
            _hasPrevNetStats = false;
            _lastBytesSent = _lastBytesReceived = 0;
            _lastPacketsSent = _lastPacketsReceived = 0;
        }

        _opScratch.Clear();
        foreach (var kvp in _opStats)
        {
            kvp.Value.ComputeDelta();
            _opScratch.Add(kvp.Key);
        }

        var topInbound = GetTopInbound();
        var topOutbound = GetTopOutbound();

        _lastReport = new NetReport
        {
            BytesSentPerSec = bytesSentDelta,
            BytesReceivedPerSec = bytesRecvDelta,
            PacketsSentPerSec = packetsSentDelta,
            PacketsReceivedPerSec = packetsRecvDelta,
            PacketLossPercent = packetLoss,
            TopInbound = topInbound,
            TopOutbound = topOutbound
        };
    }

    private OpMessageSnapshot GetTopInbound()
    {
        Op? bestOp = null;
        MessageStats bestStats = null;

        foreach (var op in _opScratch)
        {
            var stats = _opStats[op];
            if (stats.InboundBytesDelta <= 0)
                continue;

            if (bestStats == null || stats.InboundBytesDelta > bestStats.InboundBytesDelta)
            {
                bestStats = stats;
                bestOp = op;
            }
        }

        return bestOp.HasValue && bestStats != null
            ? new OpMessageSnapshot(bestOp.Value, bestStats.InboundBytesDelta, bestStats.InboundPacketsDelta, bestStats.MaxInboundPacketBytes, bestStats.LastInboundPeakTime)
            : default;
    }

    private OpMessageSnapshot GetTopOutbound()
    {
        Op? bestOp = null;
        MessageStats bestStats = null;

        foreach (var op in _opScratch)
        {
            var stats = _opStats[op];
            if (stats.OutboundBytesDelta <= 0)
                continue;

            if (bestStats == null || stats.OutboundBytesDelta > bestStats.OutboundBytesDelta)
            {
                bestStats = stats;
                bestOp = op;
            }
        }

        return bestOp.HasValue && bestStats != null
            ? new OpMessageSnapshot(bestOp.Value, bestStats.OutboundBytesDelta, bestStats.OutboundPacketsDelta, bestStats.MaxOutboundPacketBytes, bestStats.LastOutboundPeakTime)
            : default;
    }

    private MessageStats GetOrCreate(Op op)
    {
        if (!_opStats.TryGetValue(op, out var stats))
        {
            stats = new MessageStats();
            _opStats[op] = stats;
        }

        return stats;
    }

    private sealed class MessageStats
    {
        public long InboundBytesTotal;
        public int InboundPacketsTotal;
        public long OutboundBytesTotal;
        public int OutboundPacketsTotal;

        public long MaxInboundPacketBytes;
        public float LastInboundPeakTime;

        public long MaxOutboundPacketBytes;
        public float LastOutboundPeakTime;

        public long InboundBytesDelta;
        public int InboundPacketsDelta;
        public long OutboundBytesDelta;
        public int OutboundPacketsDelta;

        private long _lastInboundBytesSample;
        private int _lastInboundPacketsSample;
        private long _lastOutboundBytesSample;
        private int _lastOutboundPacketsSample;

        public void ComputeDelta()
        {
            InboundBytesDelta = InboundBytesTotal - _lastInboundBytesSample;
            InboundPacketsDelta = InboundPacketsTotal - _lastInboundPacketsSample;
            OutboundBytesDelta = OutboundBytesTotal - _lastOutboundBytesSample;
            OutboundPacketsDelta = OutboundPacketsTotal - _lastOutboundPacketsSample;

            _lastInboundBytesSample = InboundBytesTotal;
            _lastInboundPacketsSample = InboundPacketsTotal;
            _lastOutboundBytesSample = OutboundBytesTotal;
            _lastOutboundPacketsSample = OutboundPacketsTotal;
        }
    }
}

public struct NetReport
{
    public ulong BytesSentPerSec;
    public ulong BytesReceivedPerSec;
    public ulong PacketsSentPerSec;
    public ulong PacketsReceivedPerSec;
    public float PacketLossPercent;
    public OpMessageSnapshot TopInbound;
    public OpMessageSnapshot TopOutbound;

    public bool HasTraffic => BytesSentPerSec > 0 || BytesReceivedPerSec > 0 || TopInbound.IsValid || TopOutbound.IsValid;
}

public readonly struct OpMessageSnapshot
{
    public OpMessageSnapshot(Op op, long bytes, int packets, long maxPacket, float peakTime)
    {
        Op = op;
        Bytes = bytes;
        Packets = packets;
        LargestPacketBytes = maxPacket;
        LastPeakTime = peakTime;
        IsValid = true;
    }

    public Op Op { get; }
    public long Bytes { get; }
    public int Packets { get; }
    public long LargestPacketBytes { get; }
    public float LastPeakTime { get; }
    public bool IsValid { get; }
}
