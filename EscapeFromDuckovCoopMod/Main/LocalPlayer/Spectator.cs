// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspicuous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

namespace EscapeFromDuckovCoopMod;

public class Spectator : MonoBehaviour
{
    public static Spectator Instance;
    public bool _spectatorEndOnVotePending;

    public bool _skipSpectatorForNextClosure;

    public bool _spectatorActive;
    public List<CharacterMainControl> _spectateList = new();
    public int _spectateIdx = -1;
    public float _spectateNextSwitchTime;
    public DamageInfo _lastDeathInfo;
    private bool _deathClosurePending;
    private Health _settlementRequestedHealth;
    private UniTask _deathClosureTask;
    public bool IsLocalDeathPending => _deathClosurePending || CharacterMainControl.Main?.Health?.IsDead == true;
    private NetService Service => NetService.Instance;

    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;
    private Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    private Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;
    private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;

    public void Init()
    {
        Instance = this;
    }

    public bool TryEnterSpectatorOnDeath(DamageInfo dmgInfo)
    {
        var main = CharacterMainControl.Main;
        var skipSpectator = main && _settlementRequestedHealth == main.Health;
        _settlementRequestedHealth = null;
        if (skipSpectator) return false;
       // if (!LevelManager.LevelInited || main == null) return false;

        BuildSpectateList(main);
        Debug.Log("观战: " + _spectateList.Count);

        if (_spectateList.Count <= 0 && !SpectatorTravel.TryGetSurvivor(out _)) return false;

        _lastDeathInfo = dmgInfo;
        _spectatorActive = true;
        _spectateIdx = 0;

        RefreshCameraTarget();
       

        if (SceneNet.Instance.sceneVoteActive)
            _spectatorEndOnVotePending = true;

        return true; // 告诉前缀：拦截结算，启用观战
    }

    public void BuildSpectateList(CharacterMainControl exclude)
    {
        _spectateList.Clear();

        var mySceneId = localPlayerStatus != null ? localPlayerStatus.SceneId : null;
        if (string.IsNullOrEmpty(mySceneId))
            LocalPlayerManager.Instance.ComputeIsInGame(out mySceneId);
        var myK = CanonicalizeSceneId(mySceneId);

        int cand = 0, kept = 0;

        if (IsServer)
            foreach (var kv in remoteCharacters)
            {
                var go = kv.Value;
                var cmc = go ? go.GetComponent<CharacterMainControl>() : null;
                if (!LocalPlayerManager.Instance.IsAlive(cmc) || cmc == exclude) continue;
                cand++;

                string peerScene = null;
                if (!SceneM._srvPeerScene.TryGetValue(kv.Key, out peerScene) && playerStatuses.TryGetValue(kv.Key, out var st))
                    peerScene = st?.SceneId;

                if (string.IsNullOrEmpty(peerScene) || AreSameMap(mySceneId, peerScene))
                {
                    _spectateList.Add(cmc);
                    kept++;
                }
            }
        else
            foreach (var kv in clientRemoteCharacters)
            {
                var go = kv.Value;
                var cmc = go ? go.GetComponent<CharacterMainControl>() : null;
                if (!LocalPlayerManager.Instance.IsAlive(cmc) || cmc == exclude) continue;
                cand++;

                //  先从 clientPlayerStatuses 拿 SceneId
                string peerScene = null;
                if (NetService.Instance.clientPlayerStatuses.TryGetValue(kv.Key, out var st))
                    peerScene = st?.SceneId;

                //  兜底：再从 _cliLastSceneIdByPlayer 回忆一次
                if (string.IsNullOrEmpty(peerScene))
                    SceneNet.Instance._cliLastSceneIdByPlayer.TryGetValue(kv.Key, out peerScene);

                if (string.IsNullOrEmpty(peerScene) || AreSameMap(mySceneId, peerScene))
                {
                    _spectateList.Add(cmc);
                    kept++;
                }
            }

        if (!_spectatorActive)
            Debug.Log($"[SPECTATE] 候选={cand}, 可观战保留={kept}, mySceneId={mySceneId} (canon={myK})");
    }

    //说实话这个方法没多大用   //说实话这个方法没多大用   //说实话这个方法没多大用   //说实话这个方法没多大用
    public static string CanonicalizeSceneId(string id)
    {
        if (string.IsNullOrEmpty(id)) return string.Empty;
        var s = id.Trim().ToLowerInvariant();

        // 反复剔除常见后缀
        string[] suffixes = { "_main", "_gameplay", "_core", "_scene", "_lod0", "_lod", "_client", "_server" };
        bool removed;
        do
        {
            removed = false;
            foreach (var suf in suffixes)
                if (s.EndsWith(suf))
                {
                    s = s.Substring(0, s.Length - suf.Length);
                    removed = true;
                }
        } while (removed);

        while (s.Contains("__")) s = s.Replace("__", "_");

        var parts = s.Split('_');
        if (parts.Length >= 2 && parts[0] == "level")
            s = parts[0] + "_" + parts[1];

        if (s == "base" || s.StartsWith("base_")) s = "base";
        return s;
    }

    public static bool AreSameMap(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return a == b;
        return CanonicalizeSceneId(a) == CanonicalizeSceneId(b);
    }

    // —— 用已有字典反查该 CMC 是否属于“本地图”的远端玩家 —— 
    public bool IsInSameScene(CharacterMainControl cmc)
    {
        if (!cmc) return false;
        var mySceneId = localPlayerStatus != null ? localPlayerStatus.SceneId : null;
        if (string.IsNullOrEmpty(mySceneId)) return true; // 降级：无ID时不做过滤

        if (IsServer)
            foreach (var kv in remoteCharacters)
            {
                if (kv.Value == null) continue;
                var v = kv.Value.GetComponent<CharacterMainControl>();
                if (v == cmc)
                {
                    if (playerStatuses.TryGetValue(kv.Key, out var st) && st != null)
                        return st.SceneId == mySceneId;
                    return false;
                }
            }
        else
            foreach (var kv in clientRemoteCharacters)
            {
                if (kv.Value == null) continue;
                var v = kv.Value.GetComponent<CharacterMainControl>();
                if (v == cmc)
                {
                    if (NetService.Instance.clientPlayerStatuses.TryGetValue(kv.Key, out var st) && st != null)
                        return st.SceneId == mySceneId;
                    return false;
                }
            }

        return false;
    }

    public void SpectateNext()
    {
        if (_spectateList.Count == 0) return;
        _spectateIdx = (_spectateIdx + 1) % _spectateList.Count;
        RefreshCameraTarget();
    }

    public void SpectatePrev()
    {
        if (_spectateList.Count == 0) return;
        _spectateIdx = (_spectateIdx - 1 + _spectateList.Count) % _spectateList.Count;
        RefreshCameraTarget();
    }

    public void RefreshCameraTarget(CharacterMainControl previous = null)
    {
        if (_spectateList.Count == 0) return;
        var previousIndex = previous ? _spectateList.IndexOf(previous) : -1;
        if (previousIndex >= 0) _spectateIdx = previousIndex;
        else if (_spectateIdx < 0 || _spectateIdx >= _spectateList.Count) _spectateIdx = 0;

        var camera = GameCamera.Instance;
        var target = _spectateList[_spectateIdx];
        // Changing the controlling character also hands local input to the remote replica.
        if (camera && camera.target != target) camera.SetTarget(target);
    }

    public void EndSpectatorAndShowClosure(bool force = false)
    {
        _spectatorEndOnVotePending = false;

        if (!_spectatorActive && !force) return;
        if (!_spectatorActive && !_deathClosurePending)
        {
            var health = CharacterMainControl.Main?.Health;
            if (!health || !health.IsDead) return;
            _settlementRequestedHealth = health;
        }
        _spectatorActive = false;
    }

    public UniTask BeginDeathClosure(DamageInfo damage, float duration)
    {
        if (_deathClosurePending) return _deathClosureTask;
        TryEnterSpectatorOnDeath(damage);
        _deathClosureTask = WaitForDeathClosure(damage, duration).Preserve();
        return _deathClosureTask;
    }

    private async UniTask WaitForDeathClosure(DamageInfo damage, float duration)
    {
        _deathClosurePending = true;
        try
        {
            await UniTask.WaitUntil(() => !_spectatorActive || !networkStarted);
            _spectatorActive = false;
            _spectatorEndOnVotePending = false;
            var camera = GameCamera.Instance;
            var main = CharacterMainControl.Main;
            if (camera && main && camera.target != main) camera.SetTarget(main);
            _skipSpectatorForNextClosure = true;
            // Keep the native CharacterDieTask awaiting this settlement before it returns to base.
            await Duckov.UI.ClosureView.ShowAndReturnTask(damage, duration);
        }
        finally
        {
            _deathClosurePending = false;
            _settlementRequestedHealth = null;
            _skipSpectatorForNextClosure = false;
        }
    }
}
