using Duckov.Endowment;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeEndowments
{
    internal const string Key = "endowment";
    internal static void Capture(ES3File data) => data.Save(Key, EndowmentManager.SelectedIndex);
    internal static void Restore(ES3File data)
    {
        if (data.KeyExists(Key) && EndowmentManager.Instance)
            AccessTools.Method(typeof(EndowmentManager), "SelectIndex").Invoke(EndowmentManager.Instance,
                new object[] { data.Load<EndowmentIndex>(Key) });
    }
}
