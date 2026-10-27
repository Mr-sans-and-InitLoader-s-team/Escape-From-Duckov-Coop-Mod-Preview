using System.Reflection;

namespace EscapeFromDuckovCoopMod;

internal static class CountDownVoteRecovery
{
    private static readonly HashSet<CountDownArea> Pending = new();
    private static readonly MethodInfo Abort = AccessTools.Method(typeof(CountDownArea), "AbortCountDown");
    private static readonly FieldInfo Hovering = AccessTools.Field(typeof(CountDownArea), "hoveringMainCharacters");

    internal static void Record(CountDownArea area)
    {
        if (NetService.Instance?.networkStarted != true || !area) return;
        for (var i = 0; i < area.onCountDownSucceed.GetPersistentEventCount(); i++)
            if (area.onCountDownSucceed.GetPersistentTarget(i) is SceneLoaderProxy &&
                area.onCountDownSucceed.GetPersistentMethodName(i) == nameof(SceneLoaderProxy.LoadScene))
            {
                Pending.RemoveWhere(a => !a);
                Pending.Add(area);
                return;
            }
    }

    internal static void Reset()
    {
        foreach (var area in Pending)
        {
            if (!area) continue;
            Abort.Invoke(area, null);
            (Hovering.GetValue(area) as HashSet<CharacterMainControl>)?.Clear();
            area.enabled = true;
        }
        Pending.Clear();
    }
}
