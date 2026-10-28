using System.IO;
using System.Security.Cryptography;
using System.Text;
using Duckov;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Saves;
using Steamworks;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeLoadouts
{
    private static readonly Dictionary<NetPeer, string> Players = new();
    private static readonly Dictionary<NetPeer, byte[]> LastReports = new();
    private static readonly HashSet<NetPeer> NewPlayers = new();
    private static readonly Dictionary<string, ItemTreeData> Personal = new();
    private static readonly SharedHomeCharacterContext SaveContext = new(typeof(LevelManager), typeof(LevelConfig));
    private static string _context;
    private static Item _watched;
    private static byte[] _pending;
    private static byte[] _lastSent;
    private static bool _requested, _ready, _changed;
    private static float _nextReport;
    private static int _generation;
    private static readonly string[] DeathKeys = { "DeathList", "CoopCorpseRemainders", "DeadCharacterToken", "Item/LastDeadCharacter" };
    internal static bool Ready => _ready;
    internal static bool Restoring { get; private set; }
    internal static string LocalIdentity()
    {
        if (SteamManager.Initialized) return SteamUser.GetSteamID().ToString();
        var path = Path.Combine(Application.persistentDataPath, "CoopSharedPlayerId.txt");
        if (File.Exists(path)) return File.ReadAllText(path).Trim();
        var id = Guid.NewGuid().ToString("N");
        File.WriteAllText(path, id);
        return id;
    }
    private static string Key(string identity)
    {
        using var hash = SHA256.Create();
        return "SharedHome/Players/" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "");
    }
    private static string ContextKey(string player, string context) => context == "MainCharacterItemData" ? player : player + "/" + context;
    internal static string TransactionKey(NetPeer peer, string context) =>
        ValidContext(context) && Players.TryGetValue(peer, out var player) ? ContextKey(player, context) : null;
    private static byte[] Serialize(Item item)
    {
        var data = new ES3File(false);
        data.Save("context", SaveContext.Read(LevelManager.Instance));
        data.Save("character", ItemTreeData.FromItem(item));
        data.Save("face", LevelManager.Instance.CustomFaceManager.LoadMainCharacterSetting());
        data.Save("created", _ready);
        SharedHomeEndowments.Capture(data);
        SharedHomeSafeInventory.Capture(data);
        if (ES3File.cachedFiles.TryGetValue(SavesSystem.CurrentFilePath, out var cache))
            foreach (var key in DeathKeys)
                if (cache.KeyExists(key)) data.Save(key, cache.Load(key));
        return data.LoadRawBytes();
    }
    internal static byte[] Report() => Serialize(CharacterMainControl.Main.CharacterItem);
    private static void Changed(Item _) { if (_ready && !SharedHome.Applying) _changed = true; }
    private static void Unwatch()
    {
        if (_watched) _watched.onItemTreeChanged -= Changed;
        _watched = null;
    }
    internal static void Tick()
    {
        if (!SharedHome.Guest || !SharedHomeSave.OverlayActive || Restoring || SharedHomeCharacterCreation.Creating) return;
        var character = CharacterMainControl.Main;
        if (!character || !character.CharacterItem) return;
        var context = SaveContext.Read(LevelManager.Instance);
        if (_context != context)
        {
            _generation++;
            _context = context;
            _requested = _ready = false;
            _pending = _lastSent = null;
        }
        if (!_requested)
        {
            if (!Personal.ContainsKey(context)) Personal[context] = ItemTreeData.FromItem(character.CharacterItem);
            var hello = new ES3File(false);
            hello.Save("identity", LocalIdentity());
            hello.Save("context", context);
            hello.Save("character", ItemTreeData.FromItem(character.CharacterItem));
            SharedHome.SendPrivate(4, hello.LoadRawBytes());
            _requested = true;
        }
        if (_pending != null)
        {
            var pending = _pending;
            _pending = null;
            var incoming = new ES3File(pending);
            if (incoming.KeyExists("face")) SharedHomeCharacterCreation.ApplyFace(incoming.Load<CustomFaceSettingData>("face"));
            var cache = ES3File.cachedFiles[SavesSystem.CurrentFilePath];
            foreach (var key in DeathKeys)
            {
                cache.DeleteKey(key);
                if (incoming.KeyExists(key)) cache.Save(key, incoming.Load(key));
            }
            ReloadDeaths();
            RestoreItem(incoming.Load<ItemTreeData>("character"), character.CharacterItem, _generation, false,
                incoming.Load<bool>("createCharacter"), incoming).Forget();
        }
        if (_watched != character.CharacterItem)
        {
            Unwatch();
            _watched = character.CharacterItem;
            _watched.onItemTreeChanged += Changed;
        }
        if (Time.unscaledTime >= _nextReport) Flush();
    }
    internal static void Flush()
    {
        if (!SharedHome.Guest || !_ready || Restoring || SharedHomeInventoryTransaction.Busy || !SharedHomeSafeInventory.Ready || !CharacterMainControl.Main?.CharacterItem) return;
        var bytes = Serialize(CharacterMainControl.Main.CharacterItem);
        _nextReport = Time.unscaledTime + (_changed ? .25f : 1f);
        _changed = false;
        if (_lastSent != null && _lastSent.SequenceEqual(bytes)) return;
        _lastSent = bytes;
        SharedHome.SendPrivate(6, bytes);
    }
    internal static void Receive(NetPeer sender, byte kind, byte[] bytes)
    {
        if (NetService.Instance.IsServer)
        {
            if (kind == 4)
            {
                var hello = new ES3File(bytes);
                var identity = hello.Load<string>("identity");
                var context = hello.Load<string>("context");
                if (string.IsNullOrEmpty(identity) || identity.Length > 128 ||
                    sender.SteamId != 0 && identity != sender.SteamId.ToString() || !ValidContext(context)) return;
                var key = Key(identity);
                if (Players.Any(p => p.Key != sender && p.Value == key)) { sender.Disconnect(); return; }
                Players[sender] = key;
                SendLoadout(sender, key, context).Forget();
            }
            else if (kind == 6 && Players.TryGetValue(sender, out var key))
            {
                // Validate the native tree before retaining the report in the host save.
                var report = new ES3File(bytes);
                var context = report.Load<string>("context");
                if (!ValidContext(context)) return;
                var tree = report.Load<ItemTreeData>("character");
                if (tree?.entries == null || tree.entries.Count == 0 || tree.entries.Count > 10000) return;
                var created = NewPlayers.Contains(sender);
                if (created && (!report.Load<bool>("created") || !report.KeyExists("face"))) return;
                if (LastReports.TryGetValue(sender, out var previous) && previous.SequenceEqual(bytes)) return;
                var firstReport = !LastReports.ContainsKey(sender);
                LastReports[sender] = bytes;
                var saveKey = ContextKey(key, context);
                if (!report.KeyExists(SharedHomeSafeInventory.ProfileKey))
                {
                    var saved = SavesSystem.Load<byte[]>(saveKey);
                    var previousProfile = saved == null ? null : new ES3File(saved);
                    if (previousProfile?.KeyExists(SharedHomeSafeInventory.ProfileKey) == true)
                        report.Save(SharedHomeSafeInventory.ProfileKey, previousProfile.Load<InventoryData>(SharedHomeSafeInventory.ProfileKey));
                }
                SavesSystem.Save(saveKey, report.LoadRawBytes());
                if (report.KeyExists("face")) SavesSystem.Save(key + "/Face", report.Load<CustomFaceSettingData>("face"));
                var deaths = new ES3File(false);
                foreach (var deathKey in DeathKeys)
                    if (report.KeyExists(deathKey)) deaths.Save(deathKey, report.Load(deathKey));
                if (deaths.GetKeys().Length > 0) SavesSystem.Save(key + "/Deaths", deaths.LoadRawBytes());
                SharedHome.MarkDirty();
                if (created)
                {
                    SavesSystem.Save(key + "/Created", true);
                    NewPlayers.Remove(sender);
                    SharedHomeCharacterCreation.SendDestination(sender).Forget();
                }
                else if (firstReport) LateJoinScene.SendDestination(sender).Forget();
            }
        }
        else if (kind == 5 && _requested && new ES3File(bytes).Load<string>("context") == _context) _pending = bytes;
        else if (kind == 11) SharedHomeCharacterCreation.JoinHost(bytes).Forget();
    }
    private static async UniTaskVoid SendLoadout(NetPeer sender, string key, string context)
    {
        await UniTask.WaitUntil(() => !SharedHome.Active || sender.ConnectionState != ConnectionState.Connected ||
            !SceneLoader.IsSceneLoading && LevelManager.LevelInited && CharacterMainControl.Main?.CharacterItem);
        if (!SharedHome.Active || sender.ConnectionState != ConnectionState.Connected) return;
        var create = !SavesSystem.KeyExisits(key) && !SavesSystem.Load<bool>(key + "/Created");
        var data = SavesSystem.Load<byte[]>(ContextKey(key, context));
        var reply = data == null ? new ES3File(false) : new ES3File(data);
        if (data == null)
        {
            reply.Save("character", await SharedHomeCharacterCreation.NewEquipment());
            reply.Save(SharedHomeEndowments.Key, (Duckov.Endowment.EndowmentIndex)0);
        }
        if (!SharedHome.Active || sender.ConnectionState != ConnectionState.Connected ||
            !Players.TryGetValue(sender, out var current) || current != key) return;
        if (create)
        {
            NewPlayers.Add(sender);
            reply.Save("face", Duckov.Utilities.GameplayDataSettings.CustomFaceData.DefaultPreset.settings);
        }
        else if (SavesSystem.KeyExisits(key + "/Face")) reply.Save("face", SavesSystem.Load<CustomFaceSettingData>(key + "/Face"));
        reply.Save("context", context);
        reply.Save("createCharacter", create);
        foreach (var deathKey in DeathKeys) reply.DeleteKey(deathKey);
        var deaths = SavesSystem.Load<byte[]>(key + "/Deaths");
        if (deaths != null)
        {
            var deathData = new ES3File(deaths);
            foreach (var deathKey in DeathKeys)
                if (deathData.KeyExists(deathKey)) reply.Save(deathKey, deathData.Load(deathKey));
        }
        SharedHome.SendPrivate(5, reply.LoadRawBytes(), sender);
    }
    private static bool ValidContext(string context) => context != null && context.Length <= 128 && context.StartsWith("MainCharacterItemData");
    private static async UniTaskVoid RestoreItem(ItemTreeData data, Item target, int generation, bool personal, bool create = false, ES3File profile = null)
    {
        try
        {
            await SharedHomeEquipment.LoadInto(data, target, () => generation == _generation);
            if (generation != _generation || !target) return;
            if (create) await SharedHomeCharacterCreation.Create(() => generation == _generation && SharedHome.Guest);
            if (generation != _generation || !create && !target) return;
            if (profile != null) SharedHomeEndowments.Restore(profile);
            if (profile != null) await SharedHomeSafeInventory.Restore(profile, () => generation == _generation && SharedHome.Guest);
            if (generation != _generation) return;
            if (!personal) { _ready = true; _changed = true; Flush(); }
        }
        catch (Exception e) when (create && generation == _generation)
        {
            Debug.LogError($"[SharedHome] Character creation failed: {e}");
            NetService.Instance.StopNetwork();
            MModUI.ShowTip(CoopLocalization.Get("net.sharedHome.entryFailed"));
        }
        finally
        {
            if (personal) Restoring = false;
        }
    }
    internal static void PeerDisconnected(NetPeer peer)
    {
        Players.Remove(peer);
        LastReports.Remove(peer);
        NewPlayers.Remove(peer);
    }
    internal static void ReloadDeaths()
    {
        if (!DeadBodyManager.Instance) return;
        var deaths = (List<DeadBodyManager.DeathInfo>)AccessTools.Field(typeof(DeadBodyManager), "deaths").GetValue(DeadBodyManager.Instance);
        deaths.Clear();
        var saved = SavesSystem.Load<List<DeadBodyManager.DeathInfo>>("DeathList");
        if (saved != null) deaths.AddRange(saved);
        ((List<DeadBodyManager.DeathInfo>)AccessTools.Field(typeof(DeadBodyManager), "spawnedBodies").GetValue(DeadBodyManager.Instance)).Clear();
    }
    internal static void Reset()
    {
        Flush();
        _generation++;
        Unwatch();
        Personal.TryGetValue(_context ?? string.Empty, out var personal);
        Personal.Clear();
        _context = null;
        _pending = _lastSent = null;
        _requested = _ready = _changed = false;
        Players.Clear();
        LastReports.Clear();
        NewPlayers.Clear();
        if (personal != null && CharacterMainControl.Main?.CharacterItem)
        {
            Restoring = true;
            RestoreItem(personal, CharacterMainControl.Main.CharacterItem, _generation, true).Forget();
        }
    }
}
