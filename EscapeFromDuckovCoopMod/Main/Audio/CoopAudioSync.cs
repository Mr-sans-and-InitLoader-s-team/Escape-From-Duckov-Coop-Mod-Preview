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
    private static readonly string[] LocalHurtAudioTokens =
    {
        "hurt",
        "pain",
        "damage",
        "hit_organic",
        "breath"
    };

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
        "modding",
        "put_default",
        "pickup_default",
        "hitmarker_head",
        "killmarker_head",
        "death_organic",
        "hit_solid",
        "hit_organic_normal",
        "killmarker",
        "hitmarker",
        "Kazoo" //虽然有针对卡祖笛的同步处理但效果不尽人意所以封存起来，待有人能解开这个卡祖笛同步诅咒
    };

    private static bool ShouldBlockUi(string eventName, bool hasEmitter)
    {
        if (string.IsNullOrEmpty(eventName))
            return false;

        if (!hasEmitter)
            return true;

        for (int i = 0; i < UiTokens.Length; i++)
        {
            if (eventName.IndexOf(UiTokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static bool IsLocalPlayerDamageAudio(string eventName, GameObject emitter)
    {
        if (string.IsNullOrEmpty(eventName) || emitter == null)
            return false;

        var main = CharacterMainControl.Main;
        if (main == null)
            return false;

        var emitterTransform = emitter.transform;
        var mainTransform = main.transform;
        if (emitterTransform != mainTransform && !emitterTransform.IsChildOf(mainTransform))
            return false;

        for (var i = 0; i < LocalHurtAudioTokens.Length; i++)
        {
            if (eventName.IndexOf(LocalHurtAudioTokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static void Dispatch(CoopAudioEventPayload payload)
    {
        var service = Service;
        if (service == null || !service.networkStarted)
            return;

        var rpc = new AudioEventRpc { Payload = payload };
        CoopTool.SendRpc(in rpc);
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
        if (IsLocalPlayerDamageAudio(eventName, emitter)) return;
        var isKazoo = eventName.IndexOf("kazoo", StringComparison.OrdinalIgnoreCase) >= 0;
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
            SoundKey = soundKey ?? string.Empty,
            HasKazooPitch = isKazoo,
            KazooPitch = isKazoo ? UnityEngine.Random.Range(-10f, 10f) : 0f
        };

        Dispatch(payload);
    }

    internal static void NotifyLocalPost(string eventName, Vector3 position)
    {
        if (IsSuppressed) return;
        if (string.IsNullOrEmpty(eventName)) return;
        if (!ShouldSend) return;

        if (ShouldBlockUi(eventName, hasEmitter: true))
            return;

        var payload = new CoopAudioEventPayload
        {
            Kind = CoopAudioEventKind.ThreeD,
            EventName = eventName,
            Position = position,
            HasSwitch = false,
            HasSoundKey = false
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
