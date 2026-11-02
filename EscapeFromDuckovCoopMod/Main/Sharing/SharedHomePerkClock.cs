using Duckov.PerkTrees;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomePerkClock
{
    internal const string Key = "SharedHomePerkClock";
    private static readonly AccessTools.FieldRef<Perk, long> BeginTime = AccessTools.FieldRefAccess<Perk, long>("unlockingBeginTimeRaw");
    private static DateTime _serverTime;
    private static double _receivedAt;
    private static bool _ready;

    internal static void Observe(ES3File state)
    {
        if (!state.KeyExists(Key)) return;
        _serverTime = DateTime.FromBinary(state.Load<long>(Key));
        _receivedAt = Time.realtimeSinceStartupAsDouble;
        _ready = true;
    }

    internal static bool TryRemaining(Perk perk, out TimeSpan remaining)
    {
        remaining = default;
        if (!_ready || !SharedHome.Guest || perk.Unlocked || !perk.Unlocking) return false;
        var elapsed = _serverTime.AddSeconds(Time.realtimeSinceStartupAsDouble - _receivedAt) - DateTime.FromBinary(BeginTime(perk));
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        remaining = perk.Requirement.RequireTime - elapsed;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return true;
    }

    internal static void Reset() => _ready = false;
}
