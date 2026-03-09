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
using Duckov;
using Duckov.Scenes;
using NodeCanvas.Tasks.Actions;
using System.Reflection;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public static class AIAwarenessSync
{
    private sealed class PopTextScope : IDisposable
    {
        public PopTextScope()
        {
            NetSilenceGuards.InAIPopTextBroadcast = true;
        }

        public void Dispose()
        {
            NetSilenceGuards.InAIPopTextBroadcast = false;
        }
    }

    private sealed class SoundScope : IDisposable
    {
        public SoundScope()
        {
            NetSilenceGuards.InAISoundBroadcast = true;
        }

        public void Dispose()
        {
            NetSilenceGuards.InAISoundBroadcast = false;
        }
    }

    private sealed class VoiceScope : IDisposable
    {
        public VoiceScope()
        {
            NetSilenceGuards.InAIVoiceBroadcast = true;
        }

        public void Dispose()
        {
            NetSilenceGuards.InAIVoiceBroadcast = false;
        }
    }

    private static NetService Service => NetService.Instance;

    private static readonly MethodInfo PostQuakMethod = typeof(AudioManager).GetMethod(
        "PostQuak",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

    private static bool ShouldBroadcast
    {
        get
        {
            var mod = ModBehaviourF.Instance;
            if (mod == null || !mod.networkStarted || !mod.IsServer)
                return false;

            var svc = Service;
            return svc != null && svc.IsServer && svc.netManager != null && svc.netManager.IsRunning;
        }
    }

    private static bool ShouldSyncSound(SoundTypes type)
    {
        return type == SoundTypes.unknowNoise || type == SoundTypes.combatSound || type == SoundTypes.grenadeDropSound;
    }

    private static string ResolveVoiceSoundKey(PostSound.VoiceSounds voiceSound)
    {
        return voiceSound switch
        {
            PostSound.VoiceSounds.normal => "normal",
            PostSound.VoiceSounds.surprise => "surprise",
            PostSound.VoiceSounds.death => "death",
            _ => null
        };
    }

    public static readonly Dictionary<int, AICharacterController> _cache = new();

    public static AICharacterController GetAIControllerFast(this CharacterMainControl cmc)
    {
        if (cmc == null) return null;

        var go = cmc.gameObject;
        var id = go.GetInstanceID();

        // 1. 先看缓存
        if (_cache.TryGetValue(id, out var cached) && cached != null)
            return cached;

        // 2. 第一次查：只查本物体上的组件（这是最省的）
        if (!go.TryGetComponent<AICharacterController>(out var ctrl))
        {
            // 本物体上没有，就在父物体链上找一次
            ctrl = go.GetComponentInParent<AICharacterController>();
        }

        // 3. 有就缓存起来，后面都是 O(1)
        if (ctrl != null)
            _cache[id] = ctrl;

        return ctrl;
    }

    public static void TrySendPopText(AICharacterController controller, string text, float speed)
    {
        if (NetSilenceGuards.InAIPopTextBroadcast)
            return;

        if (!ShouldBroadcast)
            return;

        if (controller == null || string.IsNullOrEmpty(text))
            return;


        if (!CoopSyncDatabase.AI.TryGet(controller, out var entry) || entry == null || entry.Id == 0)
            return;
        //Debug.Log($"[AIAwarenessSync] Broadcasting PopText for AI Id={entry.Id}: \"{text}\" (speed={speed})");
        var rpc = new AIPopTextRpc
        {
            Id = entry.Id,
            Text = text,
            Speed = speed
        };

        CoopTool.SendRpc(in rpc);
    }


    public static void Client_HandlePopText(AIPopTextRpc message)
    {
        using (new PopTextScope())
        {
            var cmc = COOPManager.AI?.TryGetCharacter(message.Id);
            if (cmc == null)
                return;
           // Debug.Log($"[AIAwarenessSync] Received PopText for AI Id={message.Id}: \"{message.Text}\" (speed={message.Speed})");
            cmc.PopText(message.Text, message.Speed);
        }
    }

    public static void TrySendSound(AISound sound)
    {
        if (NetSilenceGuards.InAISoundBroadcast)
            return;

        if (!ShouldBroadcast)
            return;

        if (sound.radius <= 0f)
            return;

        if (!ShouldSyncSound(sound.soundType))
            return;

        var rpc = new AISoundEventRpc
        {
            Position = sound.pos,
            Radius = sound.radius,
            Team = sound.fromTeam,
            SoundType = sound.soundType
        };

        CoopTool.SendRpc(in rpc);
    }

    public static void TrySendVoice(AICharacterController controller, CharacterMainControl cmc, PostSound.VoiceSounds voiceSound)
    {
        if (NetSilenceGuards.InAIVoiceBroadcast)
            return;

        if (!ShouldBroadcast)
            return;

        if (controller == null || cmc == null)
            return;

        if (cmc.IsMainCharacter)
            return;

        if (!CoopSyncDatabase.AI.TryGet(controller, out var entry) || entry == null || entry.Id == 0)
            return;

        var rpc = new AIPostSoundRpc
        {
            Id = entry.Id,
            VoiceSound = voiceSound,
            VoiceType = cmc.AudioVoiceType
        };

        CoopTool.SendRpc(in rpc);
    }

    public static void Client_HandleSound(AISoundEventRpc message)
    {
        using (new SoundScope())
        {
            var sound = new AISound
            {
                pos = message.Position,
                radius = message.Radius,
                fromTeam = message.Team,
                soundType = message.SoundType,
                fromObject = null,
                fromCharacter = null
            };

            AIMainBrain.MakeSound(sound);
        }
    }

    public static void Client_HandleVoice(AIPostSoundRpc message)
    {
        using (new VoiceScope())
        {
            var cmc = COOPManager.AI?.TryGetCharacter(message.Id);
            if (cmc == null)
                return;

            var soundKey = ResolveVoiceSoundKey(message.VoiceSound);
            if (string.IsNullOrEmpty(soundKey))
                return;

            cmc.AudioVoiceType = message.VoiceType;
            if (PostQuakMethod != null)
            {
                PostQuakMethod.Invoke(null, new object[] { soundKey, cmc.AudioVoiceType, cmc.gameObject });
                return;
            }

            var getOrCreate = typeof(AudioObject).GetMethod(
                "GetOrCreate",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (getOrCreate == null)
                return;

            if (getOrCreate.Invoke(null, new object[] { cmc.gameObject }) is not AudioObject audioObject)
                return;

            audioObject.VoiceType = cmc.AudioVoiceType;
            audioObject.PostQuak(soundKey);
        }
    }
}
