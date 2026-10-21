namespace EscapeFromDuckovCoopMod;

internal sealed class SharedHomeManifest
{
    public int Protocol = 1;
    public string Player;
    public string Game;
    public string Coop;
    public List<SharedHomeModEntry> Mods = new();

    internal List<SharedHomeCompatibilityIssue> Compare(SharedHomeManifest client)
    {
        var issues = new List<SharedHomeCompatibilityIssue>();
        if (!Valid(this) || !Valid(client))
        {
            issues.Add(new() { Kind = "invalid" });
            return issues;
        }
        if (Protocol != client.Protocol) issues.Add(new() { Kind = "protocol" });
        if (Game != client.Game) issues.Add(new() { Kind = "game", Host = Game, Client = client.Game });
        if (Coop != client.Coop) issues.Add(new() { Kind = "coop", Host = Coop, Client = client.Coop });
        var guests = client.Mods.ToDictionary(m => m.Name, StringComparer.Ordinal);
        foreach (var mod in Mods.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            if (!guests.TryGetValue(mod.Name, out var other))
                issues.Add(new() { Kind = "missing", Mod = mod.Name, Host = mod.Version });
            else
            {
                if (mod.Version != other.Version)
                    issues.Add(new() { Kind = "version", Mod = mod.Name, Host = mod.Version, Client = other.Version });
                if (mod.Unreadable || other.Unreadable)
                    issues.Add(new() { Kind = "unreadable", Mod = mod.Name });
                else if (!string.Equals(mod.Hash, other.Hash, StringComparison.OrdinalIgnoreCase))
                    issues.Add(new() { Kind = "hash", Mod = mod.Name, Host = mod.Hash, Client = other.Hash });
            }
        }
        foreach (var mod in client.Mods.Where(m => !Mods.Any(h => h.Name == m.Name)).OrderBy(m => m.Name, StringComparer.Ordinal))
            issues.Add(new() { Kind = "extra", Mod = mod.Name, Client = mod.Version });
        return issues;
    }

    private static bool Valid(SharedHomeManifest value) => value != null &&
        !string.IsNullOrWhiteSpace(value.Game) && value.Game.Length <= 128 &&
        !string.IsNullOrWhiteSpace(value.Coop) && value.Coop.Length <= 128 &&
        value.Mods != null && value.Mods.Count <= 512 && value.Mods.All(m => m != null &&
            !string.IsNullOrWhiteSpace(m.Name) && m.Name.Length <= 256 && m.Version != null && m.Version.Length <= 256 &&
            (m.Unreadable || m.Hash != null && (m.Hash.Length == 0 || m.Hash.Length == 64 && m.Hash.All(Uri.IsHexDigit)))) &&
        value.Mods.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count() == value.Mods.Count;
}

internal sealed class SharedHomeModEntry
{
    public string Name;
    public string Version;
    public string Hash;
    public bool Unreadable;
}

internal sealed class SharedHomeCompatibilityIssue
{
    public string Kind;
    public string Mod;
    public string Host;
    public string Client;
}
