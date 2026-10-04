using EscapeFromDuckovCoopMod;

static class Program
{
    static void Check(bool condition, string description) { if (!condition) throw new Exception(description); }
    static void Main()
    {
        var health = new object(); var peer = new object();
        var client = new LocalPlayerDeathReport();
        var host = new PlayerDeathReportGate<object>();
        client.Observe(health, false, 100);
        var originalLife = client.LifeId;
        int reports = 0;
        for (int tick = 0; tick < 200; tick++)
        {
            // Actual game order: CurrentHealth setter fires at -1, then
            // non-raid protection restores 1 without ever setting IsDead.
            client.Observe(health, false, -1);
            if (client.TryReport(false, -1)) reports++;
            host.Observe(peer, client.LifeId, false);
            Check(!host.TryConsume(peer, client.LifeId), "living player received grave permit");
            client.Observe(health, false, 1);
        }
        Check(reports == 0 && client.LifeId == originalLife, "burning protection generated death or reset life");
        Console.WriteLine("PASS 200 bunker burn ticks: zero death reports and zero grave permits");

        client.Observe(health, false, 0);
        Check(!client.TryReport(false, 0), "transient zero health reported death");
        client.Observe(health, true, 0);
        Check(client.TryReport(true, 0), "confirmed zero-health death was lost");
        Check(!client.TryReport(true, 0), "duplicate local death report");
        Console.WriteLine("PASS zero HP counts only after confirmed death; report sent once");

        host.Observe(peer, originalLife, true);
        Check(host.TryConsume(peer, originalLife), "first real grave rejected");
        for (int duplicate = 0; duplicate < 100; duplicate++)
        {
            host.Observe(peer, originalLife, true);
            Check(!host.TryConsume(peer, originalLife), "replayed inventory duplicated grave");
        }
        Console.WriteLine("PASS repeated death health and inventory messages cannot spawn a second grave");

        host.Observe(peer, originalLife, false);
        host.Observe(peer, originalLife, true);
        Check(!host.TryConsume(peer, originalLife), "HP oscillation unlocked consumed life");
        Console.WriteLine("PASS positive HP alone cannot unlock consumed death inventory");

        client.Observe(health, false, 100); // actual revive of same Health object
        var revivedLife = client.LifeId;
        Check(revivedLife != originalLife && !client.TryReport(false, 100), "revival did not create new life");
        host.Observe(peer, revivedLife, false);
        Check(!host.TryConsume(peer, originalLife), "old delayed inventory accepted after revive");
        client.Observe(health, true, 0); host.Observe(peer, revivedLife, true);
        Check(client.TryReport(true, 0) && host.TryConsume(peer, revivedLife), "next legitimate death failed");
        Console.WriteLine("PASS true revival allows one new death while rejecting old-life inventory");

        client.Observe(new object(), false, 100);
        Check(client.LifeId != revivedLife, "replacement character reused life identity");
        Console.WriteLine("PASS replacement character gets a separate life identity");

        var otherPeer = new object();
        Check(!host.TryConsume(otherPeer, revivedLife), "different sender stole grave permit");
        host.Observe(otherPeer, "malformed", true);
        Check(!host.TryConsume(otherPeer, "malformed"), "malformed identity accepted");
        host.Remove(peer); Check(!host.CanConsume(peer, revivedLife), "disconnect retained permit");
        host.Observe(peer, client.LifeId, true); host.Clear();
        Check(!host.CanConsume(peer, client.LifeId), "network restart retained permit");
        Console.WriteLine("PASS sender binding, malformed report rejection and connection cleanup");
        Console.WriteLine("7 player death regression scenarios passed.");
    }
}
