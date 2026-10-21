global using UniTask = EscapeFromDuckovCoopMod.TestUniTask;
global using NetPeer = EscapeFromDuckovCoopMod.CoopPeer;
global using NetManager = EscapeFromDuckovCoopMod.CoopNetworkManager;

using System.Runtime.CompilerServices;

namespace EscapeFromDuckovCoopMod
{
    // Deterministic substitutes for Unity's player loop and the user's Continue button.
    [AsyncMethodBuilder(typeof(TestUniTaskBuilder))]
    public readonly struct TestUniTask(Task task)
    {
        public Task Task { get; } = task;
        public TaskAwaiter GetAwaiter() => Task.GetAwaiter();
        public TestUniTask Preserve() => this;
        public void Forget() { if (Task.IsFaulted) throw Task.Exception; }
        public static TestUniTask Yield(PlayerLoopTiming timing)
        {
            var completion = new TaskCompletionSource();
            Waiters.Add((() => true, completion));
            return new(completion.Task);
        }
        public static TestUniTask CompletedTask => new(System.Threading.Tasks.Task.CompletedTask);
        private static readonly List<(Func<bool> Ready, TaskCompletionSource Completion)> Waiters = new();
        public static TestUniTask WaitUntil(Func<bool> ready)
        {
            if (ready()) return CompletedTask;
            var completion = new TaskCompletionSource();
            Waiters.Add((ready, completion));
            return new(completion.Task);
        }
        public static void Tick()
        {
            foreach (var waiter in Waiters.ToArray())
                if (waiter.Ready())
                {
                    Waiters.Remove(waiter);
                    waiter.Completion.SetResult();
                }
        }
    }
    public struct TestUniTaskBuilder
    {
        private AsyncTaskMethodBuilder _builder;
        public static TestUniTaskBuilder Create() => new() { _builder = AsyncTaskMethodBuilder.Create() };
        public TestUniTask Task => new(_builder.Task);
        public void SetResult() => _builder.SetResult();
        public void SetException(Exception error) => _builder.SetException(error);
        public void SetStateMachine(IAsyncStateMachine state) => _builder.SetStateMachine(state);
        public void Start<T>(ref T state) where T : IAsyncStateMachine => _builder.Start(ref state);
        public void AwaitOnCompleted<T, S>(ref T awaiter, ref S state) where T : INotifyCompletion where S : IAsyncStateMachine => _builder.AwaitOnCompleted(ref awaiter, ref state);
        public void AwaitUnsafeOnCompleted<T, S>(ref T awaiter, ref S state) where T : ICriticalNotifyCompletion where S : IAsyncStateMachine => _builder.AwaitUnsafeOnCompleted(ref awaiter, ref state);
    }
    public class CoopPeer { }
    public enum PlayerLoopTiming { LastPostLateUpdate }
    public class CoopNetworkManager { }
    public class PlayerStatus { public string EndPoint, SceneId; }
    public class NetService
    {
        public static NetService Instance = new();
        public bool IsServer;
        public bool networkStarted = true;
        public CoopNetworkManager netManager;
        public NetDataWriter writer;
        public CoopPeer connectedPeer;
        public PlayerStatus localPlayerStatus = new() { SceneId = "Raid" };
        public Dictionary<CoopPeer, GameObject> remoteCharacters = new();
        public Dictionary<CoopPeer, PlayerStatus> playerStatuses = new();
        public Dictionary<string, GameObject> clientRemoteCharacters = new();
        public Dictionary<string, PlayerStatus> clientPlayerStatuses = new();
    }
    public class ModBehaviourF { public static ModBehaviourF Instance = new(); public bool networkStarted = true; }
    public class SceneNet
    {
        public static SceneNet Instance = new();
        public bool sceneVoteActive;
        public Dictionary<string, string> _cliLastSceneIdByPlayer = new();
    }
    public static class SceneM { public static Dictionary<CoopPeer, string> _srvPeerScene = new(); }
    public class LocalPlayerManager
    {
        public static LocalPlayerManager Instance = new();
        public bool IsAlive(CharacterMainControl character) => character && character.Health && !character.Health.IsDead && character.Health.CurrentHealth > 0;
        public bool ComputeIsInGame(out string scene) { scene = "Raid"; return true; }
    }
}
public struct DamageInfo { }
public class GameCamera : UnityEngine.Object
{
    public static GameCamera Instance;
    public CharacterMainControl target;
    public int TargetChanges;
    public void SetTarget(CharacterMainControl character) { target = character; TargetChanges++; }
}
public class LevelManager
{
    public static LevelManager Instance = new();
    public CharacterMainControl ControllingCharacter;
    public void SetControllingCharacter(CharacterMainControl character) => ControllingCharacter = character;
}
namespace UnityEngine { public static class Debug { public static void Log(object message) { } } }
namespace Duckov.UI
{
    public static class ClosureView
    {
        public static int NativeCalls;
        public static TaskCompletionSource Continue;
        public static UniTask ShowAndReturnTask(DamageInfo damage, float duration = .5f)
        {
            object[] args = { default(UniTask), damage, duration };
            var runNative = (bool)AccessTools.Method(typeof(EscapeFromDuckovCoopMod.Patch_ClosureView_ShowAndReturnTask_SpectatorGate), "Prefix").Invoke(null, args);
            if (!runNative) return (UniTask)args[0];
            NativeCalls++;
            Continue = new();
            return new(Continue.Task);
        }
    }
}
