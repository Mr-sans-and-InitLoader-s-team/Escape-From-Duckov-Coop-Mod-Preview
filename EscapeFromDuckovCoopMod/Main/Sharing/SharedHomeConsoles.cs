using Duckov.MiniGames;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;
using Saves;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeConsoles
{
    internal static readonly HashSet<GamingConsole> Consoles = new();
    private static readonly Dictionary<GamingConsole, int> Versions = new();
    private static readonly HashSet<GamingConsole> Pending = new();
    internal static bool Loading(GamingConsole console) => Pending.Contains(console);

    internal static void Reload(HashSet<string> changed)
    {
        if (changed != null && !changed.Contains("GamingConsoleData")) return;
        Consoles.RemoveWhere(c => !c);
        Pending.RemoveWhere(c => !c);
        foreach (var console in Versions.Keys.Where(c => !c).ToArray()) Versions.Remove(console);
        foreach (var console in Consoles)
        {
            var version = Versions.TryGetValue(console, out var old) ? old + 1 : 1;
            Versions[console] = version;
            Pending.Add(console);
            Refresh(console, version).Forget();
        }
    }

    private static async UniTaskVoid Refresh(GamingConsole console, int version)
    {
        Item monitor = null, machine = null;
        bool Current() => console && Versions.TryGetValue(console, out var current) && current == version;
        try
        {
            await UniTask.WaitUntil(() => !console || (bool)AccessTools.Field(typeof(GamingConsole), "loaded").GetValue(console));
            if (!Current()) return;
            var cache = ES3File.cachedFiles[SavesSystem.CurrentFilePath];
            var data = cache.KeyExists("GamingConsoleData") ? cache.Load("GamingConsoleData") : null;
            if (data != null)
            {
                monitor = await ItemTreeData.InstantiateAsync((ItemTreeData)AccessTools.Field(data.GetType(), "monitorData").GetValue(data));
                machine = await ItemTreeData.InstantiateAsync((ItemTreeData)AccessTools.Field(data.GetType(), "consoleData").GetValue(data));
            }
            if (!Current()) return;
            SharedHome.Apply(() =>
            {
                void Replace(Slot slot, Item incoming)
                {
                    var old = slot.Unplug();
                    if (old) old.DestroyTree();
                    if (incoming && !slot.Plug(incoming, out _)) throw new InvalidOperationException("Shared console slot is incompatible");
                }
                Replace(console.MonitorSlot, monitor);
                Replace(console.ConsoleSlot, machine);
                monitor = machine = null;
                Pending.Remove(console);
                AccessTools.Method(typeof(GamingConsole), "OnContentChanged").Invoke(console, new object[] { null });
            });
        }
        catch (Exception error) { Debug.LogError($"[SharedHome] Console could not synchronize: {error}"); }
        finally
        {
            if (monitor) monitor.DestroyTree();
            if (machine) machine.DestroyTree();
            if (!console || Current()) Pending.Remove(console);
        }
    }
}
