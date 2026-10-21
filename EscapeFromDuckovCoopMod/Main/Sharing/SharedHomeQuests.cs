using Duckov.Quests;
using QuestTask = Duckov.Quests.Task;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeQuests
{
    private static readonly Dictionary<string, object> Baselines = new();
    internal static void Init()
    {
        Quest.onQuestStatusChanged += _ => SharedHome.MarkDirty();
        Quest.onQuestActivated += _ => SharedHome.MarkDirty();
        QuestTask.OnTaskStatusChanged += Changed;
    }
    private static string Key(QuestTask task) => task.Master.ID + ":" + task.ID;
    internal static void Changed(QuestTask task)
    {
        if (!task || !task.Master || !SharedHome.Active) return;
        if (!SharedHome.Guest) { SharedHome.MarkDirty(); return; }
        var key = Key(task);
        var value = task.GenerateSaveData();
        if (!SharedHome.Applying && Baselines.TryGetValue(key, out var before) && !Equals(before, value))
        {
            var delta = value is int number && before is int previous ? Math.Max(0, number - previous) : 0;
            SharedHome.Request("task", new { quest = task.Master.ID, task = task.ID, delta, value });
        }
        Baselines[key] = value;
    }
    internal static void Reload()
    {
        var manager = QuestManager.Instance;
        if (!manager) return;
        foreach (var quest in manager.ActiveQuests.Concat(manager.HistoryQuests).Distinct().ToArray())
            if (quest) { quest.gameObject.SetActive(false); Object.Destroy(quest.gameObject); }
        manager.ActiveQuests.Clear();
        manager.HistoryQuests.Clear();
        Baselines.Clear();
        SharedHomeSave.Load(manager);
        foreach (var quest in manager.ActiveQuests)
            foreach (var task in quest.Tasks)
                if (task) Baselines[Key(task)] = task.GenerateSaveData();
        if (AccessTools.Field(typeof(QuestManager), "onQuestListsChanged")?.GetValue(null) is Action<QuestManager> changed)
            changed(manager);
    }
}
