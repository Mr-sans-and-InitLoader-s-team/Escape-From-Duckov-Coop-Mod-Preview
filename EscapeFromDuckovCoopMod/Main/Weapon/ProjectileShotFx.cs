namespace EscapeFromDuckovCoopMod;

internal static class ProjectileShotFx
{
    [ThreadStatic] private static ItemAgent_Gun _gun;
    [ThreadStatic] private static bool _played;
    [ThreadStatic] private static bool _soundSent;

    internal static (ItemAgent_Gun Gun, bool Played, bool SoundSent) Begin(ItemAgent_Gun gun)
    {
        var previous = (_gun, _played, _soundSent);
        _gun = gun;
        _played = false;
        _soundSent = false;
        return previous;
    }

    internal static void End((ItemAgent_Gun Gun, bool Played, bool SoundSent) previous)
    {
        _gun = previous.Gun;
        _played = previous.Played;
        _soundSent = previous.SoundSent;
    }

    internal static bool Take(ItemAgent_Gun gun)
    {
        if (!gun || _gun != gun) return true;
        var play = !_played;
        _played = true;
        return play;
    }

    internal static bool TakeSound(AISound sound)
    {
        if (!_gun || sound.fromObject != _gun.gameObject) return true;
        var send = !_soundSent;
        _soundSent = true;
        return send;
    }
}

[HarmonyPatch(typeof(ItemAgent_Gun), "TransToFire")]
internal static class ProjectileShotFxPatch
{
    private static void Prefix(ItemAgent_Gun __instance, out (ItemAgent_Gun Gun, bool Played, bool SoundSent) __state)
    {
        __state = ProjectileShotFx.Begin(__instance);
    }

    private static void Finalizer((ItemAgent_Gun Gun, bool Played, bool SoundSent) __state)
    {
        ProjectileShotFx.End(__state);
    }
}
