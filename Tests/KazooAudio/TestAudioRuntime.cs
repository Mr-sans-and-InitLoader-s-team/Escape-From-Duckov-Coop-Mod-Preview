global using UnityEngine;

namespace UnityEngine
{
    public class MonoBehaviour
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        protected static void Destroy(GameObject gameObject) => gameObject.Destroyed = true;
    }
    public class GameObject
    {
        public bool Destroyed;
        public readonly Transform transform = new();
        public GameObject(string name) { }
        public T AddComponent<T>() where T : MonoBehaviour, new() => new T { gameObject = this };
    }
    public class Transform
    {
        public Transform Parent;
        public float X;
        public float WorldX => X + (Parent?.WorldX ?? 0);
        public void SetParent(Transform parent, bool worldPositionStays) => Parent = parent;
    }
    public static class Time { public static float realtimeSinceStartup; }
}
namespace FMOD
{
    public enum RESULT { OK, ERROR }
}
namespace FMOD.Studio
{
    public enum STOP_MODE { IMMEDIATE, ALLOWFADEOUT }
    public sealed class TestEvent
    {
        public int Starts, Stops, Releases;
        public float X;
        public RESULT StartResult = RESULT.OK;
        public STOP_MODE StopMode;
        public readonly Dictionary<string, float> Parameters = new();
    }
    public struct EventInstance
    {
        public TestEvent Event;
        public RESULT start() { Event.Starts++; return Event.StartResult; }
        public void set3DAttributes(float x) => Event.X = x;
        public void setParameterByName(string name, float value) => Event.Parameters[name] = value;
        public void stop(STOP_MODE mode) { Event.Stops++; Event.StopMode = mode; }
        public void release() => Event.Releases++;
        public void clearHandle() => Event = null;
    }
}
namespace FMODUnity
{
    public static class Attributes
    {
        public static float To3DAttributes(this Transform transform) => transform.WorldX;
    }
}
namespace Duckov
{
    public static class AudioManager
    {
        public static readonly List<FMOD.Studio.TestEvent> Events = new();
        public static bool FailCreate;
        public static bool FailStart;
        public static bool TryCreateEventInstance(string path, out FMOD.Studio.EventInstance instance)
        {
            if (path != "SFX/Special/Kazoo") throw new Exception("Unexpected audio event");
            instance = default;
            if (FailCreate) return false;
            var item = new FMOD.Studio.TestEvent { StartResult = FailStart ? FMOD.RESULT.ERROR : FMOD.RESULT.OK };
            Events.Add(item);
            instance.Event = item;
            return true;
        }
    }
}
namespace EscapeFromDuckovCoopMod
{
    public sealed class NetService
    {
        public static NetService Instance = new();
        public bool networkStarted = true;
    }
}
