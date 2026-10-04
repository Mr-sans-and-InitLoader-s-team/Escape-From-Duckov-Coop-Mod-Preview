static class Program
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static PlayerStatus Status(string equipment, string weapon)
    {
        var status = new PlayerStatus();
        status.EquipmentList.Add(new EquipmentSyncData { SlotHash = 100, ItemId = equipment });
        status.WeaponList.Add(new WeaponSyncData { SlotHash = 1, ItemId = weapon, Snapshot = 1 });
        return status;
    }
    static async Task Main()
    {
        var context = new LoadoutContextTracker();
        var character = new object();
        Check(context.Update(true, "Base", character), "initial context");
        Check(!context.Update(true, "Base", character), "duplicate context");
        Check(context.Update(true, "Basement", character), "additive scene change with IsInGame still true");
        Check(context.Update(true, "Basement", new object()), "character replacement in same scene");
        context.Update(false, null, null);
        Check(context.Update(true, "Basement", character), "re-entry");
        Console.WriteLine("PASS additive scene, character replacement and re-entry detection");

        var peer = new CoopPeer(); var remote = new GameObject();
        NetService.Instance = new NetService { IsServer = true };
        var service = NetService.Instance;
        service.remoteCharacters[peer] = remote;
        service.playerStatuses[peer] = Status("old-armor", "old-gun");
        RemoteLoadoutReplay.ForHost(peer, remote);
        Check(COOPManager.HostPlayer_Apply.Items.Count == 0, "must wait until next frame");
        service.playerStatuses[peer] = Status("latest-armor", "latest-gun");
        await UniTask.AdvanceFrame();
        Check(COOPManager.HostPlayer_Apply.Items.SequenceEqual(new[] { "latest-armor", "latest-gun" }), "host did not replay latest cached loadout");
        Console.WriteLine("PASS host scene-ready creation replays latest cache after factory completion");

        COOPManager.HostPlayer_Apply.Items.Clear();
        RemoteLoadoutReplay.ForHost(peer, remote);
        service.remoteCharacters[peer] = new GameObject();
        await UniTask.AdvanceFrame();
        Check(COOPManager.HostPlayer_Apply.Items.Count == 0, "stale creation applied to replacement");
        Check(!RemoteLoadoutReplay.IsCurrent(peer, remote, remote.Character.characterModel), "stale async item target accepted");
        Console.WriteLine("PASS replaced host proxy rejects stale replay and item completion");

        NetService.Instance = service = new NetService { IsServer = false };
        service.clientRemoteCharacters["player"] = remote;
        RemoteLoadoutReplay.ForClient("player", remote);
        service.clientPlayerStatuses["player"] = Status("helmet", "gun");
        await UniTask.AdvanceFrame();
        Check(COOPManager.ClientPlayer_Apply.Items.SequenceEqual(new[] { "helmet", "gun" }), "position-created proxy did not recover early loadout");
        Console.WriteLine("PASS client position-created proxy restores cached equipment and weapon");

        var oldModel = remote.Character.characterModel;
        remote.Character.characterModel = new CharacterModel();
        Check(!RemoteLoadoutReplay.IsCurrent("player", remote, oldModel), "old character model accepted");
        Check(RemoteLoadoutReplay.IsCurrent("player", remote, remote.Character.characterModel), "new character model rejected");
        Console.WriteLine("PASS model replacement invalidates old asynchronous equipment target");

        COOPManager.ClientPlayer_Apply.Items.Clear();
        RemoteLoadoutReplay.ForClient("player", remote);
        service.networkStarted = false;
        await UniTask.AdvanceFrame();
        Check(COOPManager.ClientPlayer_Apply.Items.Count == 0, "disconnected replay");
        Console.WriteLine("PASS network shutdown cancels pending loadout replay");
        Console.WriteLine("6 loadout regression scenarios passed (game runtime test doubles).");
    }
}
