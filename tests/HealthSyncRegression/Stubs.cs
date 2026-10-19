// Minimal engine surfaces for exercising the production policies without launching Unity.
global using HarmonyLib;
global using LiteNetLib;
global using UnityEngine;
global using NetPacketReader = LiteNetLib.Utils.NetDataReader;

using System.Reflection;
using LiteNetLib.Utils;

namespace UnityEngine
{
    public class Object
    {
        public static implicit operator bool(Object value) => value != null;
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        private readonly Dictionary<Type, Component> _components = new();
        public T GetComponent<T>() where T : Component => _components.GetValueOrDefault(typeof(T)) as T;
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            _components[typeof(T)] = component;
            return component;
        }
    }
    public sealed class DisallowMultipleComponent : Attribute { }
    public static class Mathf
    {
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
    }
}

public class CharacterRandomPreset : UnityEngine.Object { }
public class CharacterMainControl : MonoBehaviour
{
    public bool IsMainCharacter;
    public CharacterRandomPreset characterPreset;
}
public class Health : MonoBehaviour
{
    private float _currentHealth;
    public float CurrentHealth { get => _currentHealth; set => _currentHealth = value; }
    public float MaxHealth => 120f;
    public bool Hurt() => true;
    public Action<Health> OnMaxHealthChange, OnHealthChange;
    public CharacterMainControl TryGetCharacter() => GetComponent<CharacterMainControl>();
}

namespace EscapeFromDuckovCoopMod
{
    public sealed class RemoteReplicaTag : MonoBehaviour { }
    public sealed class RemoteAIReplicaTag : MonoBehaviour { }
    public static class HealthTool
    {
        public static readonly FieldInfo FI__current = AccessTools.Field(typeof(Health), "_currentHealth");
    }
    public enum Op { AI_HEALTH_BROADCAST }
    public enum RpcDirection { ServerToClient }
    public sealed class RpcAttribute : Attribute
    {
        public RpcAttribute(Op op, DeliveryMethod delivery, RpcDirection direction) { }
    }
    public interface IRpcMessage
    {
        void Serialize(NetDataWriter writer);
        void Deserialize(NetPacketReader reader);
    }
    public struct DamageForwardPayload
    {
        public float DamageValue;
        public void Serialize(NetDataWriter writer) => writer.Put(DamageValue);
        public void Deserialize(NetPacketReader reader) => DamageValue = reader.GetFloat();
    }
}
