using Duckov.Utilities;
using EscapeFromDuckovCoopMod;

static class Program
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static int Layers(CharacterMainControl character) => character.GetBuffManager().Buffs.FirstOrDefault(b => b.ID == 101)?.CurrentLayers ?? 0;
    static void Main()
    {
        var main = CharacterMainControl.Main = new CharacterMainControl();
        var service = NetService.Instance = new NetService { IsServer = true };
        var peer = new CoopPeer { Id = "client" }; var proxy = new GameObject();
        service.remoteCharacters[peer] = proxy;
        ColdBuffSync.ReceiveReport(peer, new PlayerBuffReportRpc { TargetPlayerId = "host", BuffId = 101, ColdLayers = 10 });
        Check(Layers(main) == 0 && CoopTool.Sent.Count == 0, "observer reapplied cold to host");
        Console.WriteLine("PASS proxy requests cannot reapply frostbite to host");

        ColdBuffSync.ReceiveReport(peer, new PlayerBuffReportRpc { BuffId = 101, ColdLayers = 10 });
        ColdBuffSync.ReceiveReport(peer, new PlayerBuffReportRpc { BuffId = 101, ColdLayers = 10 });
        Check(Layers(proxy.Character) == 10 && Layers(main) == 0, "repeat packet stacked or affected host");
        ColdBuffSync.ReceiveReport(peer, new PlayerBuffReportRpc { BuffId = 101, ColdLayers = 3 });
        Check(Layers(proxy.Character) == 3, "decreasing layers not mirrored");
        ColdBuffSync.ReceiveReport(peer, new PlayerBuffReportRpc { BuffId = 101, ColdLayers = 0 });
        Check(Layers(proxy.Character) == 0, "treatment did not remove proxy buff");
        Console.WriteLine("PASS exact layer counts, decrease and full removal without duplicate stacking");

        main.AddBuff(GameplayDataSettings.Buffs.Cold, null, 0);
        ColdBuffSync.PublishLocal(main, 101);
        var add = (PlayerBuffBroadcastRpc)CoopTool.Sent.Last();
        main.RemoveBuff(101, false); ColdBuffSync.PublishLocal(main, 101);
        var remove = (PlayerBuffBroadcastRpc)CoopTool.Sent.Last();
        Check(add.ColdLayers == 1 && remove.ColdLayers == 0, "owner add/remove state missing");
        Console.WriteLine("PASS host publishes actual native add and treatment-removal state");

        service.IsServer = false; service.SelfId = "client";
        ColdBuffSync.ReceiveBroadcast(new PlayerBuffBroadcastRpc { PlayerId = "client", BuffId = 101, ColdLayers = 20 });
        Check(Layers(main) == 0, "broadcast overwrote local player's native cold decision");
        Console.WriteLine("PASS network broadcasts cannot override owning player's recovery");

        ColdBuffSync.ReceiveBroadcast(new PlayerBuffBroadcastRpc { PlayerId = "host", BuffId = 101, ColdLayers = 8 });
        ColdBuffSync.ReceiveBroadcast(new PlayerBuffBroadcastRpc { PlayerId = "host", BuffId = 101, ColdLayers = 0 });
        var laterProxy = new CharacterMainControl(); ColdBuffSync.ApplyPending("host", laterProxy);
        Check(Layers(laterProxy) == 0, "late proxy creation replayed obsolete cold add");
        Console.WriteLine("PASS pending treatment replaces earlier add before proxy creation");

        Buff_.ApplyingNetworkBuff = true;
        ColdBuffSync.ReceiveBroadcast(new PlayerBuffBroadcastRpc { PlayerId = "other", BuffId = 101, ColdLayers = 2 });
        ColdBuffSync.ApplyPending("other", laterProxy);
        Check(Buff_.ApplyingNetworkBuff, "nested network suppression was lost");
        Buff_.ApplyingNetworkBuff = false;
        ColdBuffSync.Forget("other"); ColdBuffSync.Reset();
        var fresh = new CharacterMainControl(); ColdBuffSync.ApplyPending("other", fresh);
        Check(Layers(fresh) == 0, "stale state survived reset");
        Console.WriteLine("PASS nested suppression and network-state cleanup");

        CoopTool.Sent.Clear(); ColdBuffSync.PublishAllLocal();
        Check(CoopTool.Sent.Count == 2 && CoopTool.Sent.Cast<PlayerBuffReportRpc>().All(m => m.ColdLayers == 0), "new scene did not publish clean cold state");
        Console.WriteLine("PASS new local character publishes zero layers instead of retaining old-scene frostbite");
        Console.WriteLine("7 cold-buff regression scenarios passed (game runtime test doubles).");
    }
}
