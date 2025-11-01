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

using Duckov.UI;
using ItemStatsSystem;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

public static class CoopTool
{
    // 客户端：本地 SELF 权威包尚未套用时缓存
    public static bool _cliSelfHpPending;

    public static float _cliSelfHpMax, _cliSelfHpCur;

    public static readonly Dictionary<string, List<(int weaponTypeId, int buffId)>> _cliPendingProxyBuffs = new();

    // 客户端：远端克隆未生成前收到的远端HP缓存
    public static readonly Dictionary<string, (float max, float cur)> _cliPendingRemoteHp = new();

    private static NetService Service
    {
        get
        {
            var svc = NetService.Instance;
            if (svc == null)
            {
                svc = Object.FindObjectOfType<NetService>();
                if (svc != null) NetService.Instance = svc;
            }

            return svc;
        }
    }

    private static bool IsServer => Service != null && Service.IsServer;
    private static NetManager NetManager => Service?.netManager;
    private static NetDataWriter Writer => Service?.writer;
    private static NetPeer ConnectedPeer => Service?.connectedPeer;

    private static Dictionary<NetPeer, GameObject> RemoteCharacters => Service?.remoteCharacters;
    private static Dictionary<NetPeer, PlayerStatus> PlayerStatuses => Service?.playerStatuses;
    private static Dictionary<string, GameObject> ClientRemoteCharacters => Service?.clientRemoteCharacters;
    private static int Port => Service != null ? Service.port : 0;

    public static void Init()
    {
        // 触发 Service 属性的初始化，确保 NetService 已经准备好。
        _ = Service;
    }


    public static void SafeKillItemAgent(Item item)
    {
        if (item == null) return;
        try
        {
            var ag = item.ActiveAgent;
            if (ag != null && ag.gameObject != null)
                Object.Destroy(ag.gameObject);
        }
        catch
        {
        }

        try
        {
            item.Detach();
        }
        catch
        {
        }
    }

    // 只清“目标插槽”，避免每次都清三处带来的频繁销毁/创建
    public static void ClearWeaponSlot(CharacterModel model, HandheldSocketTypes socket)
    {
        COOPManager.ChangeWeaponModel(model, null, socket);
    }

    // 小工具：把 slotHash 解析为合法的 HandheldSocketTypes；无法识别时回退到右手
    public static HandheldSocketTypes ResolveSocketOrDefault(int slotHash)
    {
        var socket = (HandheldSocketTypes)slotHash;
        if (socket != HandheldSocketTypes.normalHandheld &&
            socket != HandheldSocketTypes.meleeWeapon &&
            socket != HandheldSocketTypes.leftHandSocket)
            socket = HandheldSocketTypes.normalHandheld; // 回退
        return socket;
    }

    public static void TryPlayShootAnim(string shooterId)
    {
        // 自己开火的广播会带自己的 shooterId，这里直接跳过，避免把动作套在本地自己或主机身上
        if (NetService.Instance.IsSelfId(shooterId)) return;

        var remoteCharacters = ClientRemoteCharacters;
        if (remoteCharacters == null) return;

        if (!remoteCharacters.TryGetValue(shooterId, out var shooterGo) || !shooterGo) return;

        var animCtrl = shooterGo.GetComponent<CharacterAnimationControl_MagicBlend>();
        if (animCtrl && animCtrl.animator) animCtrl.OnAttack();
    }

    public static bool TryGetProjectilePrefab(int weaponTypeId, out Projectile pfb)
    {
        return LoaclPlayerManager.Instance._projCacheByWeaponType.TryGetValue(weaponTypeId, out pfb);
    }


    public static void BroadcastReliable(NetDataWriter w)
    {
        var manager = NetManager;
        if (!IsServer || manager == null) return;
        manager.SendToAll(w, DeliveryMethod.ReliableOrdered);
    }

    public static void SendReliable(NetDataWriter w)
    {
        var manager = NetManager;
        if (IsServer) manager?.SendToAll(w, DeliveryMethod.ReliableOrdered);
        else ConnectedPeer?.Send(w, DeliveryMethod.ReliableOrdered);
    }

    public static void SendBroadcastDiscovery()
    {
        if (IsServer) return;

        var service = Service;
        if (service == null) return;

        var manager = service.netManager;
        if (manager == null || !manager.IsRunning) return;

        var writer = service.writer;
        writer.Reset();
        writer.Put("DISCOVER_REQUEST");
        manager.SendUnconnectedMessage(writer, "255.255.255.255", service.port);
    }

    public static MapSelectionEntry GetMapSelectionEntrylist(string SceneID)
    {
        const string keyword = "MapSelectionEntry";

        var trs = Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        var gos = trs
            .Select(t => t.gameObject)
            .Where(go => go.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        foreach (var i in gos)
            try
            {
                var map = i.GetComponentInChildren<MapSelectionEntry>();
                if (map != null)
                    if (map.SceneID == SceneID)
                        return map;
            }
            catch
            {
            }

        return null;
    }

    private static string CleanName(string n)
    {
        if (string.IsNullOrEmpty(n)) return string.Empty;
        if (n.EndsWith("(Clone)", StringComparison.Ordinal)) n = n.Substring(0, n.Length - "(Clone)".Length);
        return n.Trim();
    }

    private static string TypeNameOf(Grenade g)
    {
        return g ? g.GetType().FullName : string.Empty;
    }

    public static void CacheGrenadePrefab(int typeId, Grenade prefab)
    {
        if (!prefab) return;
        COOPManager.GrenadeM.prefabByTypeId[typeId] = prefab;
    }


    public static bool TryResolvePrefab(int typeId, string _, string __, out Grenade prefab)
    {
        prefab = null;
        if (COOPManager.GrenadeM.prefabByTypeId.TryGetValue(typeId, out var p) && p)
        {
            prefab = p;
            return true;
        }

        return false;
    }


    public static CharacterMainControl TryGetRemoteCharacterForPeer(NetPeer peer)
    {
        var remotes = RemoteCharacters;
        if (remotes != null && remotes.TryGetValue(peer, out var remoteObj) && remoteObj)
        {
            var cm = remoteObj.GetComponent<CharacterMainControl>().characterModel;
            if (cm != null) return cm.characterMainControl;
        }

        return null;
    }

    // 工具：判断 DR 是否属于攻击者自己
    public static bool IsSelfDR(DamageReceiver dr, CharacterMainControl attacker)
    {
        if (!dr || !attacker) return false;
        var owner = dr.GetComponentInParent<CharacterMainControl>(true);
        return owner == attacker;
    }

    // 工具：该 DR 是否属于“角色”（而不是环境/建筑）
    public static bool IsCharacterDR(DamageReceiver dr)
    {
        return dr && dr.GetComponentInParent<CharacterMainControl>(true) != null;
    }


    public static void Client_ApplyPendingSelfIfReady()
    {
        if (!_cliSelfHpPending) return;
        var main = CharacterMainControl.Main;
        if (!main) return;

        var h = main.GetComponentInChildren<Health>(true);
        var cmc = main.GetComponent<CharacterMainControl>();
        if (!h) return;

        try
        {
            h.autoInit = false;
        }
        catch
        {
        } // 防止本地也被 Init() 回满

        HealthTool.BindHealthToCharacter(h, cmc);
        HealthM.Instance.ForceSetHealth(h, _cliSelfHpMax, _cliSelfHpCur);

        // 若现在血量已到 0，补一次死亡事件（只在客户端本地）
        LoaclPlayerManager.Instance.Client_EnsureSelfDeathEvent(h, cmc);

        _cliSelfHpPending = false;
    }

    public static void Client_ApplyPendingRemoteIfAny(string playerId, GameObject go)
    {
        if (string.IsNullOrEmpty(playerId) || !go) return;
        if (!_cliPendingRemoteHp.TryGetValue(playerId, out var snap)) return;

        var cmc = go.GetComponent<CharacterMainControl>();
        var h = cmc.Health;

        if (!h) return;

        try
        {
            h.autoInit = false;
        }
        catch
        {
        }

        HealthTool.BindHealthToCharacter(h, cmc);

        var applyMax = snap.max > 0f ? snap.max : h.MaxHealth > 0f ? h.MaxHealth : 40f;
        var applyCur = snap.cur > 0f ? snap.cur : applyMax;

        HealthM.Instance.ForceSetHealth(h, applyMax, applyCur);
        _cliPendingRemoteHp.Remove(playerId);


        if (_cliPendingProxyBuffs.TryGetValue(playerId, out var pendings) && pendings != null && pendings.Count > 0)
        {
            if (cmc)
                foreach (var (weaponTypeId, buffId) in pendings)
                    COOPManager.ResolveBuffAsync(weaponTypeId, buffId)
                        .ContinueWith(b =>
                        {
                            if (b != null && cmc) cmc.AddBuff(b, null, weaponTypeId);
                        })
                        .Forget();

            _cliPendingProxyBuffs.Remove(playerId);
        }
    }

    public static List<string> BuildParticipantIds_Server()
    {
        var list = new List<string>();

        // 计算主机当前 SceneId（仅当真正处于关卡中）
        string hostSceneId = null;
        LoaclPlayerManager.Instance.ComputeIsInGame(out hostSceneId); // 返回 false 也无所谓，hostSceneId 可能为 null/空

        // 主机自己
        var hostPid = NetService.Instance.GetPlayerId(null);
        if (!string.IsNullOrEmpty(hostPid)) list.Add(hostPid);

        // 仅把“SceneId == 主机SceneId”的客户端加入
        var statuses = PlayerStatuses;
        if (statuses == null) return list;

        foreach (var kv in statuses)
        {
            var peer = kv.Key;
            if (peer == null) continue;

            // 优先从服务端缓存的现场表取（最权威），兜底用 playerStatuses 的 SceneId
            string peerScene = null;
            if (!SceneM._srvPeerScene.TryGetValue(peer, out peerScene))
                peerScene = kv.Value?.SceneId;

            if (!string.IsNullOrEmpty(hostSceneId) && !string.IsNullOrEmpty(peerScene))
            {
                if (peerScene == hostSceneId)
                {
                    var pid = NetService.Instance.GetPlayerId(peer);
                    if (!string.IsNullOrEmpty(pid)) list.Add(pid);
                }
            }
            else
            {
                // 如果一开始拿不到 SceneId（极端竞态），先把玩家加进来，交给客户端白名单过滤
                var pid = NetService.Instance.GetPlayerId(peer);
                if (!string.IsNullOrEmpty(pid)) list.Add(pid);
            }
        }

        return list;
    }
}