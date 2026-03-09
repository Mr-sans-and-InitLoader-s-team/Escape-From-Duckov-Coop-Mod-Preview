using System;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

internal sealed class ModNetworkPump : MonoBehaviour
{
    private Action<float> _tick;

    public static ModNetworkPump Create(Action<float> tick)
    {
        var go = new GameObject("[ModNetworkPump]")
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        DontDestroyOnLoad(go);
        var pump = go.AddComponent<ModNetworkPump>();
        pump._tick = tick;
        return pump;
    }

    private void Update()
    {
        _tick?.Invoke(Time.unscaledDeltaTime);
    }

    private void OnDestroy()
    {
        _tick = null;
    }
}
