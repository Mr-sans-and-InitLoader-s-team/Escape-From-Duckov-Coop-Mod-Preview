using Duckov.Scenes;
using EscapeFromDuckovCoopMod;

static class Program
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static GameObject Actor()
    {
        var go = new GameObject();
        go.Character = new CharacterMainControl { gameObject = go, characterModel = new CharacterModel() };
        return go;
    }
    static void Main()
    {
        var service = new NetService();
        var oldActor = Actor(); oldActor.Destroyed = true;
        var other = Actor(); service.clientRemoteCharacters["other"] = other;
        service.clientRemoteCharacters["departed"] = oldActor;
        RemoteMeleePlayback.Play(service, new MeleeSwingBroadcastRpc { PlayerId = "departed" });
        Check(MeleeFx.Slashes == 0 && other.Character.characterModel.Basic.Attacks == 0, "stale actor was played or redirected");
        Console.WriteLine("PASS destroyed actor in dictionary is safely ignored without nearest-player fallback");

        var actor = Actor(); service.clientRemoteCharacters["player"] = actor;
        actor.Character.characterModel.Destroyed = true;
        RemoteMeleePlayback.Play(service, new MeleeSwingBroadcastRpc { PlayerId = "player" });
        Check(MeleeFx.Slashes == 0, "destroyed model playback");
        actor.Character.characterModel = new CharacterModel();
        Console.WriteLine("PASS destroyed character model does not enter Unity GetComponent");

        SceneLoader.IsSceneLoading = true;
        RemoteMeleePlayback.Play(service, new MeleeSwingBroadcastRpc { PlayerId = "player" });
        SceneLoader.IsSceneLoading = false; MultiSceneCore.Instance.IsLoading = true;
        RemoteMeleePlayback.Play(service, new MeleeSwingBroadcastRpc { PlayerId = "player" });
        MultiSceneCore.Instance.IsLoading = false;
        Check(MeleeFx.Slashes == 0, "loading scene played stale swing");
        Console.WriteLine("PASS main and additive scene loading suppress stale melee playback");

        RemoteMeleePlayback.Play(service, new MeleeSwingBroadcastRpc { PlayerId = "player" });
        Check(MeleeFx.Slashes == 1 && actor.Character.characterModel.Magic.Attacks == 1, "valid client actor not played");
        Console.WriteLine("PASS current client actor still plays melee normally");

        service.IsServer = true;
        var peer = new CoopPeer(); service.Peers["player"] = peer; service.remoteCharacters[peer] = actor;
        RemoteMeleePlayback.Play(service, new MeleeSwingBroadcastRpc { PlayerId = "player" });
        Check(MeleeFx.Slashes == 2, "host did not use peer actor dictionary");
        Console.WriteLine("PASS host resolves player from host-side peer dictionary");

        var ai = Actor(); COOPManager.AI.Characters[7] = ai.Character;
        RemoteMeleePlayback.Play(service, new MeleeSwingBroadcastRpc { AiId = 7 });
        RemoteMeleePlayback.Play(service, new MeleeSwingBroadcastRpc { AiId = 8 });
        RemoteMeleePlayback.Play(service, new MeleeSwingBroadcastRpc { PlayerId = "self" });
        Check(MeleeFx.Slashes == 3, "AI identity or missing/self filtering");
        Console.WriteLine("PASS AI resolves by ID; unknown actors and self are not replayed");
        Console.WriteLine("6 melee playback regression scenarios passed (Unity test doubles).");
    }
}
