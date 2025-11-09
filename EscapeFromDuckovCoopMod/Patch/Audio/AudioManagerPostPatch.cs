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
using System.Reflection;
using Duckov;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(AudioManager))]
public static class AudioManagerPostPatch
{
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(AudioManager)))
        {
            if (method.Name != nameof(AudioManager.Post))
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length == 0)
                continue;

            if (parameters[0].ParameterType != typeof(string))
                continue;

            yield return method;
        }
    }

    [HarmonyPrefix]
    private static void CapturePost(object[] __args)
    {
        if (__args == null || __args.Length == 0)
            return;

        if (__args[0] is not string eventName || string.IsNullOrEmpty(eventName))
            return;

        GameObject emitter = null;
        string switchName = null;
        string soundKey = null;

        for (var i = 1; i < __args.Length; i++)
        {
            switch (__args[i])
            {
                case GameObject go:
                    emitter = go;
                    break;
                case Component component:
                    emitter = component.gameObject;
                    break;
                case string str when switchName == null:
                    switchName = str;
                    break;
                case string str:
                    if (soundKey == null)
                        soundKey = str;
                    break;
            }
        }

        if (emitter == null)
        {
            for (var i = 1; i < __args.Length; i++)
            {
                var arg = __args[i];
                if (arg == null)
                    continue;

                var type = arg.GetType();
                if (type.Name != "AudioObject")
                    continue;

                try
                {
                    var getter = AccessTools.Property(type, "Emitter")?.GetGetMethod(true);
                    if (getter != null && getter.Invoke(arg, null) is GameObject go)
                    {
                        emitter = go;
                        break;
                    }
                }
                catch
                {
                    // ignored - reflection helper best effort only
                }

                try
                {
                    var field = AccessTools.Field(type, "emitter");
                    if (field != null && field.GetValue(arg) is GameObject go)
                    {
                        emitter = go;
                        break;
                    }
                }
                catch
                {
                    // ignored - reflection helper best effort only
                }
            }
        }

        if (emitter == null && switchName == null && soundKey == null)
        {
            CoopAudioSync.NotifyLocalPost(eventName);
        }
        else
        {
            CoopAudioSync.NotifyLocalPost(eventName, emitter, switchName, soundKey);
        }
    }
}
