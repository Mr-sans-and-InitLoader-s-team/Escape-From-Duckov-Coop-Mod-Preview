using Duckov;
using FMOD;
using FMODUnity;

namespace EscapeFromDuckovCoopMod;

internal static class CoopCombatAudio
{
    internal const string FlybyEvent = "SFX/Combat/Bullet/flyby";
    private static readonly Dictionary<string, bool> GunshotOneShots = new(StringComparer.OrdinalIgnoreCase);

    private static bool IsGunshot(CoopAudioEventPayload payload)
    {
        return payload.Kind == CoopAudioEventKind.ThreeD && !payload.HasSwitch && !payload.HasSoundKey && !payload.HasKazooPitch &&
            payload.EventName != null && payload.EventName.StartsWith("SFX/Combat/Gun/Shoot/", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsTransientGunshot(CoopAudioEventPayload payload)
    {
        if (!IsGunshot(payload)) return false;
        if (GunshotOneShots.TryGetValue(payload.EventName, out var oneShot)) return oneShot;
        if (!AudioManager.TryCreateEventInstance(payload.EventName, out var sound)) return false;
        try
        {
            if (sound.getDescription(out var description) != RESULT.OK || description.isOneshot(out oneShot) != RESULT.OK)
                return false;
            GunshotOneShots[payload.EventName] = oneShot;
            return oneShot;
        }
        finally
        {
            sound.release();
        }
    }

    internal static bool TryPlayGunshot(CoopAudioEventPayload payload)
    {
        if (!IsGunshot(payload))
            return false;

        return TryPlayOneShot(payload.EventName, payload.Position);
    }

    internal static bool TryPlayOneShot(string eventName, Vector3 position)
    {
        if (!AudioManager.TryCreateEventInstance(eventName, out var sound))
            return false;

        try
        {
            if (sound.getDescription(out var description) != RESULT.OK ||
                description.isOneshot(out var oneShot) != RESULT.OK || !oneShot)
                return false;

            // Match the native position-based post without its unused SoundSource GameObject.
            sound.set3DAttributes(position.To3DAttributes());
            return sound.start() == RESULT.OK;
        }
        finally
        {
            sound.release();
        }
    }
}

[HarmonyPatch(typeof(AudioManager), "OnBulletFlyby")]
internal static class CoopBulletFlybyAudioPatch
{
    private static bool Prefix(Vector3 __0)
    {
        if (NetService.Instance == null || !NetService.Instance.networkStarted)
            return true;

        return !CoopCombatAudio.TryPlayOneShot(CoopCombatAudio.FlybyEvent, __0);
    }
}
