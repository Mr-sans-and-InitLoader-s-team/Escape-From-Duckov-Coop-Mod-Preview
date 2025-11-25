using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public sealed class PerformanceDiagnostics
{
    private const float ReportInterval = 2.0f;
    private const float SevereFrameThreshold = 0.25f; // 4 FPS
    private const float WarningFrameThreshold = 0.0666f; // ~15 FPS

    private readonly List<float> _frameSamples = new(256);
    private readonly int[] _gcCounts;

    private float _timer;
    private long _lastGcBytes;
    private bool _initialized;

    private readonly StringBuilder _sb = new(256);

    public static PerformanceDiagnostics Instance { get; } = new();

    private PerformanceDiagnostics()
    {
        _gcCounts = new int[GC.MaxGeneration + 1];
        _lastGcBytes = GC.GetTotalMemory(false);
        for (var i = 0; i <= GC.MaxGeneration; i++)
            _gcCounts[i] = GC.CollectionCount(i);
        _initialized = true;
    }

    public void Reset()
    {
        _frameSamples.Clear();
        _timer = 0f;
        _lastGcBytes = GC.GetTotalMemory(false);
        for (var i = 0; i <= GC.MaxGeneration; i++)
            _gcCounts[i] = GC.CollectionCount(i);
    }

    public void Update(float deltaTime)
    {
        if (!_initialized)
            return;

        _timer += deltaTime;
        _frameSamples.Add(Time.unscaledDeltaTime);

        var service = NetService.Instance;
        NetDiagnostics.Instance.Update(service?.netManager, deltaTime);

        if (_timer < ReportInterval)
            return;

        Report(service);
        _timer = 0f;
        _frameSamples.Clear();
    }

    private void Report(NetService? service)
    {
        if (_frameSamples.Count == 0)
            return;

        float sum = 0f;
        float min = float.MaxValue;
        float max = 0f;

        foreach (var sample in _frameSamples)
        {
            sum += sample;
            if (sample < min) min = sample;
            if (sample > max) max = sample;
        }

        var avgFrame = sum / _frameSamples.Count;
        var avgFps = avgFrame > 1e-5f ? 1f / avgFrame : 0f;
        var worstFps = max > 1e-5f ? 1f / max : 0f;
        var bestFps = min > 1e-5f ? 1f / min : 0f;

        var totalGcBytes = GC.GetTotalMemory(false);
        var gcDelta = totalGcBytes - _lastGcBytes;
        _lastGcBytes = totalGcBytes;

        var maxGeneration = _gcCounts.Length - 1;
        var totalGenCollections = 0;
        Span<int> genDeltas = stackalloc int[_gcCounts.Length];
        for (var generation = 0; generation <= maxGeneration; generation++)
        {
            var currentCount = GC.CollectionCount(generation);
            var delta = currentCount - _gcCounts[generation];
            if (delta < 0)
                delta = currentCount; // handle counters reset across domain reloads

            genDeltas[generation] = delta;
            totalGenCollections += delta;
            _gcCounts[generation] = currentCount;
        }

        var netReport = NetDiagnostics.Instance.LastReport;

        _sb.Clear();
        _sb.Append("[PerfDiag] FPS avg=").Append(avgFps.ToString("F1"));
        _sb.Append(" best=").Append(bestFps.ToString("F1"));
        _sb.Append(" worst=").Append(worstFps.ToString("F1"));

        if (max >= WarningFrameThreshold)
        {
            _sb.Append(" | peakFrame=").Append((max * 1000f).ToString("F0")).Append("ms");
        }

        if (gcDelta != 0 || totalGenCollections > 0)
        {
            _sb.Append(" | GCΔ=").Append(FormatBytes(gcDelta));
            if (totalGenCollections > 0)
            {
                _sb.Append(" GCs[");
                for (var generation = 0; generation <= maxGeneration; generation++)
                {
                    if (generation > 0)
                        _sb.Append(' ');

                    _sb.Append(generation).Append(':').Append(genDeltas[generation]);
                }
                _sb.Append(']');
            }
        }

        if (netReport.HasTraffic)
        {
            _sb.Append(" | Net send=").Append(FormatBytes((long)netReport.BytesSentPerSec)).Append("/s");
            _sb.Append(" recv=").Append(FormatBytes((long)netReport.BytesReceivedPerSec)).Append("/s");
            if (netReport.PacketLossPercent > 0.001f)
            {
                _sb.Append(" loss=").Append(netReport.PacketLossPercent.ToString("F2")).Append('%');
            }

            if (netReport.TopInbound.IsValid)
            {
                _sb.Append(" | TopRecv=")
                    .Append(netReport.TopInbound.Op)
                    .Append('(')
                    .Append(FormatBytes(netReport.TopInbound.Bytes))
                    .Append(" in ")
                    .Append(netReport.TopInbound.Packets)
                    .Append(" pkts, max ")
                    .Append(FormatBytes(netReport.TopInbound.LargestPacketBytes))
                    .Append(')');
            }

            if (netReport.TopOutbound.IsValid)
            {
                _sb.Append(" | TopSend=")
                    .Append(netReport.TopOutbound.Op)
                    .Append('(')
                    .Append(FormatBytes(netReport.TopOutbound.Bytes))
                    .Append(" in ")
                    .Append(netReport.TopOutbound.Packets)
                    .Append(" pkts, max ")
                    .Append(FormatBytes(netReport.TopOutbound.LargestPacketBytes))
                    .Append(')');
            }
        }

        if (service != null && service.netManager != null)
        {
            var peers = service.netManager.ConnectedPeerList;
            if (peers.Count > 0)
            {
                int maxPing = 0;
                int sumPing = 0;
                foreach (var peer in peers)
                {
                    if (peer == null) continue;
                    var ping = peer.Ping;
                    sumPing += ping;
                    if (ping > maxPing) maxPing = ping;
                }

                _sb.Append(" | Ping avg=").Append((sumPing / Math.Max(1, peers.Count)).ToString());
                _sb.Append(" max=").Append(maxPing.ToString());
            }
        }

        var hasFpsWarning = max >= WarningFrameThreshold || avgFps < 55f;
        var severeFps = max >= SevereFrameThreshold || avgFps < 30f;
        var hasGcActivity = gcDelta != 0 || totalGenCollections > 0;
        var hasNetTraffic = netReport.HasTraffic;

        if (!hasFpsWarning && !hasGcActivity && !hasNetTraffic)
            return;

        if (severeFps || gcDelta > 0 || hasNetTraffic)
            Debug.LogWarning(_sb.ToString());
        else
            Debug.Log(_sb.ToString());
    }

    private static string FormatBytes(long bytes)
    {
        var abs = Math.Abs(bytes);
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;

        return abs switch
        {
            >= GB => (bytes / (double)GB).ToString("F2") + " GB",
            >= MB => (bytes / (double)MB).ToString("F1") + " MB",
            >= KB => (bytes / (double)KB).ToString("F0") + " KB",
            _ => bytes + " B"
        };
    }
}
