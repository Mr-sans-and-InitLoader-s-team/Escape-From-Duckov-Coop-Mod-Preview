// Minimal engine surfaces for exercising the production policies without launching Unity.
global using HarmonyLib;
global using LiteNetLib;
global using LiteNetLib.Utils;
global using UnityEngine;
global using NetPacketReader = LiteNetLib.Utils.NetDataReader;

using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        private static int _nextId;
        private readonly int _id = ++_nextId;
        public int GetInstanceID() => _id;
        public bool Destroyed;
        public static void Destroy(Object value) => value.Destroyed = true;
        public static implicit operator bool(Object value) => value != null && !value.Destroyed;
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T GetComponentInParent<T>(bool includeInactive) where T : Component => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        public bool activeSelf = true;
        public void SetActive(bool value) => activeSelf = value;
        private readonly Dictionary<Type, Component> _components = new();
        public T GetComponent<T>() where T : Component => _components.GetValueOrDefault(typeof(T)) as T;
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            _components[typeof(T)] = component;
            return component;
        }
    }
    public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; } }
    public struct Quaternion { }
    public sealed class DisallowMultipleComponent : Attribute { }
    public static class Mathf
    {
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
    }
}

public class CharacterRandomPreset : UnityEngine.Object { }
public class CharacterMainControl : MonoBehaviour
{
    public ItemStatsSystem.Item CharacterItem = new();
    public static CharacterMainControl Main;
    public Health Health => GetComponent<Health>();
    public bool IsMainCharacter;
    public CharacterRandomPreset characterPreset;
}
public class Health : MonoBehaviour
{
    public bool IsDead;
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
    public enum Op { AI_HEALTH_BROADCAST, HOST_BUFF_PROXY_APPLY, PLAYER_BUFF_SELF_APPLY, CORPSE_INVENTORY, AI_BUFF_BROADCAST, AI_SNAPSHOT_CHUNK, AI_SPAWN, PLAYER_HEALTH_REPORT, PLAYER_HEALTH_BROADCAST, ITEM_DROP_SNAPSHOT_CHUNK, DEAD_LOOT_SPAWN, LOOT_PUT_OK, AI_SOUND_EVENT }
    public enum RpcDirection { ServerToClient, ClientToServer }
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
