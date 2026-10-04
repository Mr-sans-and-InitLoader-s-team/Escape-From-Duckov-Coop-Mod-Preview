using EscapeFromDuckovCoopMod;
using System.Reflection;

static class Program
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static bool Prefix(LevelManager level) => (bool)typeof(Patch_Level_StartInit_Gate)
        .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { level, new SceneLoadingContext() });
    static async Task Finish()
    {
        await Task.WhenAll(TestTasks.Pending);
        TestTasks.Pending.Clear();
    }
    static async Task Main()
    {
        var permit = new LocalSceneLoadPermit();
        permit.Grant("Base", 0, 10, true);
        Check(permit.Allows("Base", 120), "settlement authorization expired while reading results");
        Check(!permit.Allows("Raid", 120), "settlement authorization leaked to other maps");
        Console.WriteLine("PASS approved return remains valid after a two-minute settlement wait, only for its target");
        permit.Clear(); Check(!permit.Allows("Base", 121), "cleared authorization survived");
        permit.Grant("Raid", 0, 10, false);
        Check(permit.Allows("Raid", 9) && !permit.Allows("Raid", 11), "normal timed permit changed");
        Console.WriteLine("PASS completion/reset clears permits while ordinary timeout is preserved");

        var level = new LevelManager { gameObject = new() { scene = new TestScene(1, true) } };
        Check(Prefix(level) && SceneNet.Instance.WaitCalls == 0 && WaitingSynchronizationUI.Instance.Hidden == 1, "base initialization blocked by host gate");
        Console.WriteLine("PASS client return to base follows original initialization without waiting for host");

        level = new(); SceneNet.Instance = new();
        Check(!Prefix(level) && level.InitCalls == 0 && SceneNet.Instance.WaitCalls == 1, "raid gate did not wait");
        SceneNet.Instance.Wait.SetResult(); await Finish();
        Check(level.InitCalls == 1, "released raid gate did not initialize exactly once");
        Console.WriteLine("PASS raid gate release resumes original level initialization");

        level = new(); SceneNet.Instance = new(); Prefix(level);
        SceneNet.Instance.Wait.SetException(new Exception("simulated gate UI failure")); await Finish();
        Check(level.InitCalls == 1, "gate exception left scene permanently uninitialized");
        Console.WriteLine("PASS gate failure does not strand original initialization behind a black screen");

        level = new(); SceneNet.Instance = new(); Prefix(level);
        level.gameObject.scene = new TestScene(2, false);
        SceneNet.Instance.Wait.SetResult(); await Finish();
        Check(level.InitCalls == 0, "old unloaded level initialized after delayed release");
        Console.WriteLine("PASS old unloaded level ignores delayed initialization continuation");
        Console.WriteLine("6 scene return regression scenarios passed (game runtime test doubles).");
    }
}
