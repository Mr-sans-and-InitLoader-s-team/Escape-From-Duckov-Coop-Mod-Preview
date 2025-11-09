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
using System.Threading;
using Duckov;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public static class CoopAudioSync
{
    private static int _suppressCount;

    private sealed class SuppressScope : IDisposable
    {
        public void Dispose()
        {
            Interlocked.Decrement(ref _suppressCount);
        }
    }

    private static NetService Service => NetService.Instance;

    private static bool IsSuppressed => Interlocked.CompareExchange(ref _suppressCount, 0, 0) > 0;

    private static bool ShouldSend
    {
        get
        {
            var service = Service;
            if (service == null || !service.networkStarted)
                return false;

            if (service.IsServer)
                return service.netManager != null;

            return service.connectedPeer != null;
        }
    }

    private static readonly char[] UiSeparators = { '/', '\\', '_', '-', '.', ' ' };

    private static readonly string[] UiTokens =
    {
        "ui",
        "inventory",
        "stash",
        "ragfair",
        "trader",
        "handbook",
        "preset",
        "character",
        "profile",
        "quest",
        "dialog",
        "dialogue",
        "hideout",
        "menu",
        "loading",
        "matching",
        "map",
        "inspect",
        "examine",
        "modding"
    };

    private static bool ShouldBlockUi(string eventName, bool hasEmitter)
    {
        if (string.IsNullOrEmpty(eventName))
            return false;

        if (!hasEmitter)
            return true;

        var normalized = eventName.ToLowerInvariant();

        foreach (var token in UiTokens)
        {
            if (normalized.Contains(token))
                return true;
        }

        var split = normalized.Split(UiSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in split)
        {
            foreach (var token in UiTokens)
            {
                if (segment == token)
                    return true;
            }
        }

        return false;
    }

    private static void Dispatch(CoopAudioEventPayload payload)
    {
        var service = Service;
        if (service == null || !service.networkStarted)
            return;

        if (service.IsServer)
        {
            AudioEventMessage.ServerBroadcast(payload);
        }
        else if (service.connectedPeer != null)
        {
            AudioEventMessage.ClientSend(payload);
        }
    }

    private static IDisposable BeginSuppress()
    {
        Interlocked.Increment(ref _suppressCount);
        return new SuppressScope();
    }

    internal static void NotifyLocalPost(string eventName)
    {
        if (IsSuppressed) return;
        if (string.IsNullOrEmpty(eventName)) return;
        if (!ShouldSend) return;

        // Treat all two dimensional posts as UI and keep them local.
        if (ShouldBlockUi(eventName, hasEmitter: false))
            return;

        var payload = new CoopAudioEventPayload
        {
            Kind = CoopAudioEventKind.TwoD,
            EventName = eventName,
            Position = Vector3.zero,
            HasSwitch = false,
            HasSoundKey = false
        };

        Dispatch(payload);
    }

    internal static void NotifyLocalPost(string eventName, GameObject emitter, string switchName, string soundKey)
    {
        if (IsSuppressed) return;
        if (string.IsNullOrEmpty(eventName)) return;
        if (!ShouldSend) return;
        var hasEmitter = emitter != null;
        if (ShouldBlockUi(eventName, hasEmitter)) return;
        var position = Vector3.zero;
        if (hasEmitter)
        {
            try
            {
                position = emitter.transform.position;
            }
            catch
            {
                hasEmitter = false;
                position = Vector3.zero;
            }
        }

        var payload = new CoopAudioEventPayload
        {
            Kind = hasEmitter ? CoopAudioEventKind.ThreeD : CoopAudioEventKind.TwoD,
            EventName = eventName,
            Position = position,
            HasSwitch = !string.IsNullOrEmpty(switchName),
            SwitchName = switchName ?? string.Empty,
            HasSoundKey = !string.IsNullOrEmpty(soundKey),
            SoundKey = soundKey ?? string.Empty
        };

        Dispatch(payload);
    }

    internal static void HandleIncoming(CoopAudioEventPayload payload)
    {
        using (BeginSuppress())
        {
            var hasEmitter = payload.Kind == CoopAudioEventKind.ThreeD;

            if (ShouldBlockUi(payload.EventName, hasEmitter))
                return;

            if (!hasEmitter)
            {
                AudioManager.Post(payload.EventName);
                return;
            }

            var emitter = CoopAudioEmitter.Spawn();
            emitter.Play(payload);
        }
    }
}
