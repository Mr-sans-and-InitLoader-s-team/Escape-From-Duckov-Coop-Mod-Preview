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

using Duckov;
using FMOD.Studio;
using FMODUnity;
using System.Reflection;
using UnityEngine;
using STOP_MODE = FMOD.Studio.STOP_MODE;

namespace EscapeFromDuckovCoopMod;

public sealed class CoopAudioEmitter : MonoBehaviour
{
    private float _lifeTime;
    private EventInstance? _playing;
    private bool _stopAfterLife;
    private bool _isKazoo;

    public static CoopAudioEmitter Spawn()
    {
        var go = new GameObject("CoopAudioEmitter");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        return go.AddComponent<CoopAudioEmitter>();
    }

    private void Awake()
    {
        _lifeTime = 3f;
    }

    private void Update()
    {
        var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        if (serverLoading)
        {
            return;
        }
        _lifeTime -= Time.deltaTime;
        if (_lifeTime <= 0f)
        {
            StopPlaying();
            Destroy(gameObject);
        }
    }

    public void Play(CoopAudioEventPayload payload)
    {
        transform.position = payload.Position;

        var eventName = payload.EventName ?? string.Empty;
        _isKazoo = eventName.IndexOf("kazoo", StringComparison.OrdinalIgnoreCase) >= 0;
        _stopAfterLife = _isKazoo;
        if (_isKazoo && _lifeTime > 2.25f)
            _lifeTime = 2.25f;

        if (payload.HasSwitch || payload.HasSoundKey)
        {
            _playing = AudioManager.Post(payload.EventName, gameObject);
        }
        else
        {
            _playing = AudioManager.Post(payload.EventName, gameObject);
        }

        if (_isKazoo)
        {
            var pitch = payload.HasKazooPitch ? payload.KazooPitch : 0f;
            SetKazooParameter("Kazoo/Pitch", pitch, gameObject);
            SetKazooParameter("Kazoo/Intensity", 0.55f, gameObject);
        }
    }

    private static void SetKazooParameter(string parameter, float value, GameObject target)
    {
        try
        {
            var method = typeof(AudioManager).GetMethod("SetRTPC", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method != null)
            {
                method.Invoke(null, new object[] { parameter, value, target });
                return;
            }

            RuntimeManager.StudioSystem.setParameterByName($"parameter:/{parameter}", value, false);
        }
        catch
        {
            // Ignore parameter failures so coop audio continues even if RTPCs are unavailable.
        }
    }

    private void OnDestroy()
    {
        StopPlaying();
    }

    private void StopPlaying()
    {
        if (!_stopAfterLife)
            return;

        _stopAfterLife = false;
        AudioManager.StopAll(gameObject, STOP_MODE.ALLOWFADEOUT);
        if (_playing.HasValue)
        {
            try
            {
                _playing.Value.stop(STOP_MODE.ALLOWFADEOUT);
            }
            catch
            {
                // ignore FMOD stop errors on dispose
            }
        }
    }
}
