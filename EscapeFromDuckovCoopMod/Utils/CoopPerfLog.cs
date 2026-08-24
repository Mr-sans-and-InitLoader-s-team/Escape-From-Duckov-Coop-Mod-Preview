using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

internal static class CoopPerfLog
{
    public const string AiPerfLogPath = @"C:\DuckovCoopMod_AI_Perf.log";

    private static readonly StringBuilder Builder = new(512);
    private static bool _disabled;
    private static bool _reportedFailure;

    public static void AppendAiSample(
        bool isServer,
        int totalEntries,
        int activeEntries,
        int replicas,
        int watcherLinks,
        int pendingSnapshots,
        int pendingStates,
        int statePackets,
        int snapshotPackets,
        int healthPackets,
        float deltaTime,
        float frameBudgetScale)
    {
        if (!BuildInfo.RuntimePerfLoggingEnabled || _disabled) return;

        try
        {
            Builder.Clear();
            Builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            Builder.Append(" role=").Append(isServer ? "server" : "client");
            Builder.Append(" dt=").Append(deltaTime.ToString("0.0000"));
            Builder.Append(" entries=").Append(totalEntries);
            Builder.Append(" active=").Append(activeEntries);
            Builder.Append(" replicas=").Append(replicas);
            Builder.Append(" watcherLinks=").Append(watcherLinks);
            Builder.Append(" pendingSnapshots=").Append(pendingSnapshots);
            Builder.Append(" pendingStates=").Append(pendingStates);
            Builder.Append(" statePackets=").Append(statePackets);
            Builder.Append(" snapshotPackets=").Append(snapshotPackets);
            Builder.Append(" healthPackets=").Append(healthPackets);
            Builder.Append(" budgetScale=").Append(frameBudgetScale.ToString("0.00"));
            Builder.AppendLine();

            File.AppendAllText(AiPerfLogPath, Builder.ToString());
        }
        catch (Exception ex)
        {
            _disabled = true;
            if (_reportedFailure) return;
            _reportedFailure = true;
            Debug.LogWarning($"[COOP][PerfLog] Failed to write {AiPerfLogPath}: {ex.Message}");
        }
    }

    public static void AppendEvent(string category, string message)
    {
        if (!BuildInfo.RuntimePerfLoggingEnabled || _disabled) return;

        try
        {
            Builder.Clear();
            Builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            Builder.Append(" event=").Append(category ?? "general");
            Builder.Append(' ').Append(message ?? string.Empty);
            Builder.AppendLine();

            File.AppendAllText(AiPerfLogPath, Builder.ToString());
        }
        catch (Exception ex)
        {
            _disabled = true;
            if (_reportedFailure) return;
            _reportedFailure = true;
            Debug.LogWarning($"[COOP][PerfLog] Failed to write {AiPerfLogPath}: {ex.Message}");
        }
    }
}
