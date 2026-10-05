using Duckov;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using STOP_MODE = FMOD.Studio.STOP_MODE;

namespace EscapeFromDuckovCoopMod;

internal sealed class KazooRemoteEmitter : MonoBehaviour
{
    private EventInstance _event;
    private bool _ownsEvent;
    private float _deadline;
    public bool IsPlaying => _ownsEvent;

    public static KazooRemoteEmitter Create(Transform player, float pitch, float intensity)
    {
        var go = new GameObject("CoopKazoo");
        go.transform.SetParent(player, false);
        var emitter = go.AddComponent<KazooRemoteEmitter>();
        // Own the event directly: no generic AudioManager.Post hook, no released
        // looping handle, and no global RTPC affecting somebody else's instrument.
        if (!AudioManager.TryCreateEventInstance("SFX/Special/Kazoo", out emitter._event))
        {
            Destroy(go);
            return null;
        }
        emitter._ownsEvent = true;
        emitter.Refresh(pitch, intensity);
        emitter._event.set3DAttributes(go.transform.To3DAttributes());
        if (emitter._event.start() != RESULT.OK)
        {
            emitter.Shutdown();
            return null;
        }
        return emitter;
    }

    public void Refresh(float pitch, float intensity)
    {
        if (!_ownsEvent) return;
        _deadline = Time.realtimeSinceStartup + (float)KazooPlaybackState.TimeoutSeconds;
        _event.setParameterByName("parameter:/Kazoo/Pitch", pitch);
        _event.setParameterByName("parameter:/Kazoo/Intensity", intensity);
    }

    private void LateUpdate()
    {
        if (Time.realtimeSinceStartup >= _deadline || NetService.Instance == null || !NetService.Instance.networkStarted)
        {
            Shutdown();
            return;
        }
        if (_ownsEvent) _event.set3DAttributes(transform.To3DAttributes());
    }

    public void Shutdown()
    {
        Release();
        Destroy(gameObject);
    }
    private void OnDisable() => Release();
    private void OnDestroy() => Release();
    private void Release()
    {
        if (!_ownsEvent) return;
        _ownsEvent = false;
        _event.stop(STOP_MODE.IMMEDIATE);
        _event.release();
        _event.clearHandle();
    }
}
