using System.Reflection;
using Duckov;
using EscapeFromDuckovCoopMod;
using FMOD.Studio;

static class Program
{
    static int _count;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Invoke(KazooRemoteEmitter emitter, string method) => typeof(KazooRemoteEmitter).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(emitter, null);
    static void Test(string name, Action action)
    {
        Time.realtimeSinceStartup = 0;
        NetService.Instance.networkStarted = true;
        AudioManager.Events.Clear(); AudioManager.FailCreate = AudioManager.FailStart = false;
        action(); _count++; Console.WriteLine($"PASS {name}");
    }
    static void Main()
    {
        Test("late start and duplicate heartbeat cannot resurrect stopped sound", () =>
        {
            var state = new KazooPlaybackState();
            Check(state.Apply(10, true, "A", -2, 1, 0), "start");
            Check(state.Apply(12, false, "A", 0, 0, .2), "stop");
            Check(!state.Apply(11, true, "A", 4, 1, .3), "late start accepted");
            Check(!state.Apply(12, true, "A", 4, 1, .4), "duplicate accepted");
            Check(!state.IsAudible(.5, "A"), "sound resurrected");
        });
        Test("lost stop times out and stale packets cannot extend the lease", () =>
        {
            var state = new KazooPlaybackState(); state.Apply(1, true, "A", 2, 1, 0);
            Check(state.IsAudible(.9, "A") && !state.IsAudible(1, "A"), "lease timeout");
            Check(!state.Apply(1, true, "A", 2, 1, 1.1) && !state.IsAudible(1.2, "A"), "duplicate renewed lease");
            Check(state.Apply(2, true, "A", 3, .5f, 1.3) && state.IsAudible(1.4, "A"), "fresh heartbeat recovery");
        });
        Test("scene mismatch and unload silence without resetting sequence", () =>
        {
            var state = new KazooPlaybackState(); state.Apply(8, true, "A", 2, 1, 0);
            Check(!state.IsAudible(.2, "B"), "wrong scene audible");
            state.Expire(); Check(!state.IsAudible(.3, "A"), "unload still audible");
            Check(!state.Apply(7, true, "A", 2, 1, .4), "unload reset sequence");
        });
        Test("sequence rollover and malformed parameters", () =>
        {
            var state = new KazooPlaybackState(); state.Apply(uint.MaxValue, true, "A", 2, 1, 0);
            Check(state.Apply(0, false, "A", 0, 0, .1), "rollover");
            Check(!state.Apply(1, true, "A", float.NaN, 1, .2), "NaN accepted");
            Check(!state.Apply(1, true, "A", 0, float.PositiveInfinity, .2), "infinity accepted");
            Check(!state.IsAudible(.3, "A"), "bad data started sound");
        });
        Test("pitch updates reuse one event and its position follows player", () =>
        {
            var player = new Transform { X = 5 };
            var emitter = KazooRemoteEmitter.Create(player, -7, .7f);
            for (var i = 0; i < 20; i++) emitter.Refresh(i, .8f);
            player.X = 25; Invoke(emitter, "LateUpdate");
            var sound = AudioManager.Events.Single();
            Check(sound.Starts == 1 && sound.X == 25, "repeated event or frozen position");
            Check(sound.Parameters["parameter:/Kazoo/Pitch"] == 19 && sound.Parameters["parameter:/Kazoo/Intensity"] == .8f, "actual parameters");
            emitter.Shutdown();
        });
        Test("stop disable destroy release the event exactly once", () =>
        {
            var emitter = KazooRemoteEmitter.Create(new Transform(), 0, 1);
            emitter.Shutdown(); Invoke(emitter, "OnDisable"); Invoke(emitter, "OnDestroy");
            var sound = AudioManager.Events.Single();
            Check(sound.Stops == 1 && sound.Releases == 1 && sound.StopMode == STOP_MODE.IMMEDIATE && !emitter.IsPlaying, "double/missing release");
        });
        Test("emitter watchdog works independently of game sync tick", () =>
        {
            var emitter = KazooRemoteEmitter.Create(new Transform(), 0, 1);
            Time.realtimeSinceStartup = 1.1f; Invoke(emitter, "LateUpdate");
            Check(AudioManager.Events.Single().Stops == 1 && emitter.gameObject.Destroyed, "watchdog did not stop");
        });
        Test("network shutdown and disabled player silence owned event", () =>
        {
            var emitter = KazooRemoteEmitter.Create(new Transform(), 0, 1);
            NetService.Instance.networkStarted = false; Invoke(emitter, "LateUpdate");
            Check(AudioManager.Events.Single().Releases == 1, "network shutdown leak");
            var other = KazooRemoteEmitter.Create(new Transform(), 0, 1);
            Invoke(other, "OnDisable"); Check(!other.IsPlaying && AudioManager.Events.Last().Releases == 1, "disable leak");
        });
        Test("create and start failures do not retain an event", () =>
        {
            AudioManager.FailCreate = true;
            Check(KazooRemoteEmitter.Create(new Transform(), 0, 1) == null && AudioManager.Events.Count == 0, "failed create");
            AudioManager.FailCreate = false; AudioManager.FailStart = true;
            Check(KazooRemoteEmitter.Create(new Transform(), 0, 1) == null && AudioManager.Events.Single().Releases == 1, "failed start leaked event");
        });
        Console.WriteLine($"{_count} kazoo audio tests passed (Unity/FMOD test doubles).");
    }
}
