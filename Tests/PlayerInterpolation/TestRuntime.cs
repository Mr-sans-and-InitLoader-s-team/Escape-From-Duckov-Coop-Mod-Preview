global using UnityEngine;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DefaultExecutionOrder : Attribute { public int Order; public DefaultExecutionOrder(int order) => Order = order; }
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string text) { } }
    public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string text) { } }
    public class Object { public static implicit operator bool(Object value) => value is not null; }
    public class MonoBehaviour : Object
    {
        public Transform transform = new();
        public bool enabled = true;
        public T GetComponent<T>() where T : class => null;
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : class => null;
    }
    public sealed class GameObject : Object
    {
        public Transform transform = new();
        public T GetComponent<T>() where T : class => null;
        public T AddComponent<T>() => Activator.CreateInstance<T>();
    }
    public sealed class Transform : Object
    {
        public Vector3 position;
        public Quaternion rotation;
        public void SetPositionAndRotation(Vector3 p, Quaternion r) { position = p; rotation = r; }
        public T GetComponent<T>() where T : class => null;
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
        public static Vector3 operator /(Vector3 a, float b) => new(a.x / b, a.y / b, a.z / b);
        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
    }
    public struct Quaternion
    {
        public static Quaternion identity => default;
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => a; // These tests hold rotation constant.
    }
    public static class Mathf
    {
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Abs(float value) => Math.Abs(value);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp(t, 0, 1);
    }
    public static class Time
    {
        public static double unscaledTimeAsDouble;
        public static float unscaledDeltaTime = 1f / 60;
    }
}
public sealed class CharacterMainControl : MonoBehaviour
{
    public GameObject modelRoot = new();
    public bool isVehicle = false;
}
public sealed class LevelManager
{
    public static LevelManager Instance = null;
    public CharacterMainControl ControllingCharacter = null;
}
namespace EscapeFromDuckovCoopMod
{
    public sealed class CoopAISettings
    {
        public static CoopAISettings Active = null;
        public float StateBroadcastInterval = .05f;
    }
    public sealed class SendLocalVehicleStatus
    {
        public static SendLocalVehicleStatus Instance = null;
        public bool IsLocalAuthorityForVehicle(CharacterMainControl character) => false;
    }
}
