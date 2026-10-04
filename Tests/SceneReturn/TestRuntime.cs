global using UniTaskVoid = System.Threading.Tasks.Task;
using System.Reflection;

namespace EscapeFromDuckovCoopMod
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name) { } }
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] args) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, null, args, null);
    }
    public static class TestTasks
    {
        public static readonly List<Task> Pending = new();
        public static void Forget(this Task task) => Pending.Add(task);
    }
    public static class Debug { public static void LogError(object value) { } }
    public static class CoopLogSystem { public static void WriteNetworkDiagnostic(string text) { } }
    public sealed class ModBehaviourF
    {
        public static ModBehaviourF Instance = new();
        public bool IsServer = false;
        public bool networkStarted = true;
    }
    public sealed class SceneNet
    {
        public static SceneNet Instance = new();
        public bool sceneVoteActive = false;
        public int WaitCalls;
        public TaskCompletionSource Wait = new();
        public Task Client_SceneGateAsync(string id) { WaitCalls++; return Wait.Task; }
    }
    public sealed class WaitingSynchronizationUI
    {
        public static WaitingSynchronizationUI Instance = new();
        public int Hidden;
        public void Hide() => Hidden++;
    }
    public static class TeleporterTravel { public static void Request(string scene, int beacon) { } }
}
public readonly struct SceneLoadingContext { }
public readonly struct TestScene
{
    public TestScene(int index, bool loaded) { buildIndex = index; isLoaded = loaded; }
    public int buildIndex { get; }
    public bool isLoaded { get; }
}
public sealed class TestGameObject { public TestScene scene = new(2, true); }
public sealed class LevelManager
{
    public TestGameObject gameObject = new();
    public int InitCalls;
    public static implicit operator bool(LevelManager value) => value is not null;
    private void InitLevel(SceneLoadingContext context) => InitCalls++;
}
public static class SceneInfoCollection
{
    public const string BaseSceneID = "Base";
    public static string GetSceneID(int index) => index == 1 ? "Base" : "Raid";
}
namespace Duckov.Scenes { public struct MultiSceneLocation { } }
namespace UnityEngine.EventSystems { public sealed class PointerEventData { } }
namespace Duckov.UI
{
    public sealed class MapSelectionEntry { public string SceneID = "Raid"; public int BeaconIndex = 0; }
    public sealed class MapSelectionView { }
}
