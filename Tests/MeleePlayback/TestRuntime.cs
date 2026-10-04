global using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static implicit operator bool(Object value) => value is not null && !value.Destroyed;
    }
    public sealed class GameObject : Object
    {
        public bool activeInHierarchy = true;
        public CharacterMainControl Character;
        public T GetComponent<T>() where T : class
        {
            if (Destroyed) throw new NullReferenceException("Unity native object has been destroyed");
            return Character as T;
        }
    }
}
public sealed class CharacterMainControl : UnityEngine.Object
{
    public UnityEngine.GameObject gameObject;
    public CharacterModel characterModel;
}
public sealed class CharacterModel : UnityEngine.Object
{
    public CharacterAnimationControl_MagicBlend Magic = new();
    public CharacterAnimationControl Basic = new();
    public T GetComponent<T>() where T : class
    {
        if (Destroyed) throw new NullReferenceException("Destroyed model");
        return typeof(T) == typeof(CharacterAnimationControl) ? Basic as T : Magic as T;
    }
}
public sealed class CharacterAnimationControl_MagicBlend : UnityEngine.Object { public int Attacks; public void OnAttack() => Attacks++; }
public sealed class CharacterAnimationControl : UnityEngine.Object { public int Attacks; public void OnAttack() => Attacks++; }
public static class SceneLoader { public static bool IsSceneLoading; }
namespace Duckov.Scenes
{
    public sealed class MultiSceneCore
    {
        public static MultiSceneCore Instance = new();
        public bool IsLoading;
    }
}
namespace EscapeFromDuckovCoopMod
{
    public sealed class CoopPeer { }
    public sealed class NetService
    {
        public bool networkStarted = true;
        public bool IsServer;
        public Dictionary<string, CoopPeer> Peers = new();
        public Dictionary<CoopPeer, UnityEngine.GameObject> remoteCharacters = new();
        public Dictionary<string, UnityEngine.GameObject> clientRemoteCharacters = new();
        public bool IsSelfId(string id) => id == "self";
        public bool TryGetPeerByPlayerId(string id, out CoopPeer peer) => Peers.TryGetValue(id, out peer);
    }
    public struct MeleeSwingBroadcastRpc { public string PlayerId; public int AiId; }
    public sealed class AiService
    {
        public Dictionary<int, CharacterMainControl> Characters = new();
        public CharacterMainControl TryGetCharacter(int id) => Characters.TryGetValue(id, out var character) ? character : null;
    }
    public static class COOPManager { public static AiService AI = new(); }
    public static class MeleeFx { public static int Slashes; public static void SpawnSlashFx(CharacterModel model) => Slashes++; }
}
