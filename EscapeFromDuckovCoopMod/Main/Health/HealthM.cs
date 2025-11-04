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

using System.Collections;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

public class HealthM : MonoBehaviour
{
    private const float SRV_HP_SEND_COOLDOWN = 0.5f; // 降低频率到每0.5秒一次
    private const float HP_CHANGE_THRESHOLD = 0.5f; // 血量变化阈值：至少变化0.5才同步
    public static HealthM Instance;
    private static (float max, float cur) _cliLastSentHp = HealthTool._cliLastSentHp;
    private static float _cliNextSendHp = HealthTool._cliNextSendHp;

    public bool _cliApplyingSelfSnap;
    public float _cliEchoMuteUntil;
    private readonly Dictionary<Health, NetPeer> _srvHealthOwner = HealthTool._srvHealthOwner;

    private readonly Dictionary<Health, (float max, float cur)> _srvLastSent = new();
    private readonly Dictionary<Health, float> _srvNextSend = new();
    
    public readonly HashSet<Health> _srvApplyingHealth = new HashSet<Health>();
    
    private bool _rpcRegistered = false;

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
        RegisterRPCs();
    }
    
    private void RegisterRPCs()
    {
        var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null)
        {
            Debug.LogWarning("[HealthM] HybridRPCManager not found, RPC will not be used");
            return;
        }

        rpcManager.RegisterRPC("PlayerHealthReport", OnRPC_PlayerHealthReport);
        rpcManager.RegisterRPC("AuthHealthSelf", OnRPC_AuthHealthSelf);
        rpcManager.RegisterRPC("AuthHealthRemote", OnRPC_AuthHealthRemote);
        
        _rpcRegistered = true;
        Debug.Log("[HealthM] Health RPCs registered");
    }

    internal bool TryGetClientMaxOverride(Health h, out float v)
    {
        return COOPManager.AIHandle._cliAiMaxOverride.TryGetValue(h, out v);
    }


    public void Client_SendSelfHealth(Health h, bool force)
    {
        if (_cliApplyingSelfSnap || Time.time < _cliEchoMuteUntil) return;

        if (!networkStarted || IsServer || h == null) return;

        float max = 0f, cur = 0f;
        try
        {
            max = h.MaxHealth;
        }
        catch
        {
        }

        try
        {
            cur = h.CurrentHealth;
        }
        catch
        {
        }

        if (!force && Mathf.Approximately(max, _cliLastSentHp.max) && Mathf.Approximately(cur, _cliLastSentHp.cur))
            return;

        if (!force && Time.time < _cliNextSendHp) return;

        if (connectedPeer != null)
        {
            var w = new NetDataWriter();
            w.Put((byte)Op.PLAYER_HEALTH_REPORT);
            w.Put(max);
            w.Put(cur);
            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);

            _cliLastSentHp = (max, cur);
            _cliNextSendHp = Time.time + 0.05f;
        }
        
        if (_rpcRegistered && connectedPeer != null)
        {
            var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
            if (rpcManager != null)
            {
                rpcManager.CallRPC("PlayerHealthReport", Net.HybridP2P.RPCTarget.Server, 0, (writer) =>
                {
                    writer.Put(max);
                    writer.Put(cur);
                }, DeliveryMethod.ReliableOrdered);
            }
        }
    }


    public void Server_ForceAuthSelf(Health h)
    {
        if (!networkStarted || !IsServer || h == null) return;
        if (!_srvHealthOwner.TryGetValue(h, out var ownerPeer) || ownerPeer == null) return;

        var w = writer;
        if (w == null) return;
        w.Reset();
        w.Put((byte)Op.AUTH_HEALTH_SELF);
        float max = 0f, cur = 0f;
        try
        {
            max = h.MaxHealth;
            cur = h.CurrentHealth;
        }
        catch
        {
        }

        w.Put(max);
        w.Put(cur);
        ownerPeer.Send(w, DeliveryMethod.ReliableOrdered);
    }

    public void Server_ForwardHurtToOwner(NetPeer owner, DamageInfo di)
    {
        if (!IsServer || owner == null) return;

        var w = new NetDataWriter();
        w.Put((byte)Op.PLAYER_HURT_EVENT);
        w.Put(di.damageValue);
        w.Put(di.armorPiercing);
        w.Put(di.critDamageFactor);
        w.Put(di.critRate);
        w.Put(di.crit);
        w.PutV3cm(di.damagePoint);
        w.PutDir(di.damageNormal.sqrMagnitude < 1e-6f ? Vector3.up : di.damageNormal.normalized);
        w.Put(di.fromWeaponItemID);
        w.Put(di.bleedChance);
        w.Put(di.isExplosion);

        owner.Send(w, DeliveryMethod.ReliableOrdered);
    }


    public void Client_ApplySelfHurtFromServer(NetPacketReader r)
    {
        try
        {
            // 反序列化与上面写入顺序保持一致
            var dmg = r.GetFloat();
            var ap = r.GetFloat();
            var cdf = r.GetFloat();
            var cr = r.GetFloat();
            var crit = r.GetInt();
            var hit = r.GetV3cm();
            var nrm = r.GetDir();
            var wid = r.GetInt();
            var bleed = r.GetFloat();
            var boom = r.GetBool();

            var main = LevelManager.Instance ? LevelManager.Instance.MainCharacter : null;
            if (!main || main.Health == null) return;

            // 构造 DamageInfo（攻击者此处可不给/或给 main，自身并不影响结算核心）
            var di = new DamageInfo(main)
            {
                damageValue = dmg,
                armorPiercing = ap,
                critDamageFactor = cdf,
                critRate = cr,
                crit = crit,
                damagePoint = hit,
                damageNormal = nrm,
                fromWeaponItemID = wid,
                bleedChance = bleed,
                isExplosion = boom
            };

            // 记录“最近一次本地受击时间”，便于已有的 echo 抑制逻辑
            HealthTool._cliLastSelfHurtAt = Time.time;

            main.Health.Hurt(di);

            Client_ReportSelfHealth_IfReadyOnce();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CLIENT] apply self hurt from server failed: " + e);
        }
    }

    public void Client_ReportSelfHealth_IfReadyOnce()
    {
        if (_cliApplyingSelfSnap || Time.time < _cliEchoMuteUntil) return;
        if (IsServer || HealthTool._cliInitHpReported) return;
        if (connectedPeer == null || connectedPeer.ConnectionState != ConnectionState.Connected) return;

        var main = CharacterMainControl.Main;
        var h = main ? main.GetComponentInChildren<Health>(true) : null;
        if (!h) return;

        float max = 0f, cur = 0f;
        try
        {
            max = h.MaxHealth;
        }
        catch
        {
        }

        try
        {
            cur = h.CurrentHealth;
        }
        catch
        {
        }

        var w = new NetDataWriter();
        w.Put((byte)Op.PLAYER_HEALTH_REPORT);
        w.Put(max);
        w.Put(cur);
        connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);

        HealthTool._cliInitHpReported = true;
    }

    public void Server_OnHealthChanged(NetPeer ownerPeer, Health h)
    {
        if (!IsServer || !h) return;
        
        // 如果正在应用血量，阻止回环
        if (_srvApplyingHealth.Contains(h)) return;

        float max = 0f, cur = 0f;
        try
        {
            max = h.MaxHealth;
        }
        catch
        {
        }

        try
        {
            cur = h.CurrentHealth;
        }
        catch
        {
        }

        if (max <= 0f) return;
        
        var now = Time.time;
        
        // 检查是否有显著变化或冷却时间已到
        bool hasSignificantChange = false;
        if (_srvLastSent.TryGetValue(h, out var last))
        {
            float maxDiff = Mathf.Abs(max - last.max);
            float curDiff = Mathf.Abs(cur - last.cur);
            hasSignificantChange = (maxDiff >= HP_CHANGE_THRESHOLD) || (curDiff >= HP_CHANGE_THRESHOLD);
            
            // 如果变化不显著且值相同，直接返回
            if (!hasSignificantChange && Mathf.Approximately(max, last.max) && Mathf.Approximately(cur, last.cur))
                return;
        }
        else
        {
            hasSignificantChange = true; // 首次发送
        }

        // 如果有显著变化，立即发送；否则检查冷却时间
        if (!hasSignificantChange)
        {
            if (_srvNextSend.TryGetValue(h, out var tNext) && now < tNext)
                return;
        }

        _srvLastSent[h] = (max, cur);
        _srvNextSend[h] = now + SRV_HP_SEND_COOLDOWN;

        var pid = NetService.Instance.GetPlayerId(ownerPeer);

        if (ownerPeer == null)
        {
            try
            {
                h.OnMaxHealthChange?.Invoke(h);
                h.OnHealthChange?.Invoke(h);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HealthM] Failed to invoke host health change events: {e.Message}");
            }
            
            var wHost = new NetDataWriter();
            wHost.Put((byte)Op.AUTH_HEALTH_REMOTE);
            wHost.Put(pid);
            wHost.Put(max);
            wHost.Put(cur);

            foreach (var p in netManager.ConnectedPeerList)
            {
                p.Send(wHost, DeliveryMethod.ReliableOrdered);
            }
            
            if (_rpcRegistered)
            {
                var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
                if (rpcManager != null)
                {
                    rpcManager.CallRPC("AuthHealthRemote", Net.HybridP2P.RPCTarget.AllClients, 0, (writer) =>
                    {
                        writer.Put(pid);
                        writer.Put(max);
                        writer.Put(cur);
                    }, DeliveryMethod.ReliableOrdered);
                }
            }
            return;
        }

        if (ownerPeer.ConnectionState == ConnectionState.Connected)
        {
            var w1 = new NetDataWriter();
            w1.Put((byte)Op.AUTH_HEALTH_SELF);
            w1.Put(max);
            w1.Put(cur);
            ownerPeer.Send(w1, DeliveryMethod.ReliableOrdered);
        }

        var w2 = new NetDataWriter();
        w2.Put((byte)Op.AUTH_HEALTH_REMOTE);
        w2.Put(pid);
        w2.Put(max);
        w2.Put(cur);

        foreach (var p in netManager.ConnectedPeerList)
        {
            if (p == ownerPeer) continue;
            p.Send(w2, DeliveryMethod.ReliableOrdered);
        }
        
        if (_rpcRegistered && ownerPeer.ConnectionState == ConnectionState.Connected)
        {
            var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
            if (rpcManager != null)
            {
                rpcManager.CallRPC("AuthHealthSelf", Net.HybridP2P.RPCTarget.TargetClient, ownerPeer.Id, (writer) =>
                {
                    writer.Put(max);
                    writer.Put(cur);
                }, DeliveryMethod.ReliableOrdered);
                
                rpcManager.CallRPC("AuthHealthRemote", Net.HybridP2P.RPCTarget.AllClients, 0, (writer) =>
                {
                    writer.Put(pid);
                    writer.Put(max);
                    writer.Put(cur);
                }, DeliveryMethod.ReliableOrdered);
            }
        }
    }

    // 服务器兜底：每帧确保所有权威对象都已挂监听（含主机自己）
    public void Server_EnsureAllHealthHooks()
    {
        if (!IsServer || !networkStarted) return;

        var hostMain = CharacterMainControl.Main;
        if (hostMain) HealthTool.Server_HookOneHealth(null, hostMain.gameObject);

        if (remoteCharacters != null)
            foreach (var kv in remoteCharacters)
            {
                var peer = kv.Key;
                var go = kv.Value;
                if (peer == null || !go) continue;
                HealthTool.Server_HookOneHealth(peer, go);
            }
    }


    // 起条兜底：多帧重复请求血条，避免 UI 初始化竞态
    private static IEnumerator EnsureBarRoutine(Health h, int attempts, float interval)
    {
        for (var i = 0; i < attempts; i++)
        {
            if (h == null) yield break;
            try
            {
                h.showHealthBar = true;
            }
            catch
            {
            }

            try
            {
                h.RequestHealthBar();
            }
            catch
            {
            }

            try
            {
                h.OnMaxHealthChange?.Invoke(h);
            }
            catch
            {
            }

            try
            {
                h.OnHealthChange?.Invoke(h);
            }
            catch
            {
            }

            yield return new WaitForSeconds(interval);
        }
    }

    // 把 (max,cur) 灌到 Health，并确保血条显示（修正 defaultMax=0）
    public void ForceSetHealth(Health h, float max, float cur, bool ensureBar = true)
    {
        if (!h) return;

        var nowMax = 0f;
        try
        {
            nowMax = h.MaxHealth;
        }
        catch
        {
        }

        var defMax = 0;
        try
        {
            defMax = (int)(HealthTool.FI_defaultMax?.GetValue(h) ?? 0);
        }
        catch
        {
        }

        // ★ 只要传入的 max 更大，就把 defaultMaxHealth 调到更大，并触发一次 Max 变更事件
        if (max > 0f && (nowMax <= 0f || max > nowMax + 0.0001f || defMax <= 0))
            try
            {
                HealthTool.FI_defaultMax?.SetValue(h, Mathf.RoundToInt(max));
                HealthTool.FI_lastMax?.SetValue(h, -12345f);
                h.OnMaxHealthChange?.Invoke(h);
            }
            catch
            {
            }

        // ★ 避免被 SetHealth() 按旧 Max 夹住
        var effMax = 0f;
        try
        {
            effMax = h.MaxHealth;
        }
        catch
        {
        }

        if (effMax > 0f && cur > effMax + 0.0001f)
        {
            try
            {
                HealthTool.FI__current?.SetValue(h, cur);
            }
            catch
            {
            }

            try
            {
                h.OnHealthChange?.Invoke(h);
            }
            catch
            {
            }
        }
        else
        {
            try
            {
                h.SetHealth(cur);
            }
            catch
            {
                try
                {
                    HealthTool.FI__current?.SetValue(h, cur);
                }
                catch
                {
                }
            }

            try
            {
                h.OnHealthChange?.Invoke(h);
            }
            catch
            {
            }
        }

        if (ensureBar)
        {
            try
            {
                h.showHealthBar = true;
            }
            catch
            {
            }

            try
            {
                h.RequestHealthBar();
            }
            catch
            {
            }

            StartCoroutine(EnsureBarRoutine(h, 30, 0.1f));
        }
    }

    // 统一应用到某个 GameObject 的 Health（含绑定）

    public void ApplyHealthAndEnsureBar(GameObject go, float max, float cur)
    {
        if (!go) return;

        var cmc = go.GetComponent<CharacterMainControl>();
        var h = go.GetComponentInChildren<Health>(true);
        if (!cmc || !h) return;

        try
        {
            h.autoInit = false;
        }
        catch
        {
        }

        // 绑定 Health ⇄ Character（否则 UI/Hidden 判断拿不到角色）
        HealthTool.BindHealthToCharacter(h, cmc);

        // 先把数值灌进去（内部会触发 OnMax/OnHealth）
        ForceSetHealth(h, max > 0 ? max : 40f, cur > 0 ? cur : max > 0 ? max : 40f, false);

        // 立刻起条 + 多帧兜底（UI 还没起来时反复 Request）
        try
        {
            h.showHealthBar = true;
        }
        catch
        {
        }

        try
        {
            h.RequestHealthBar();
        }
        catch
        {
        }

        // 触发一轮事件，部分 UI 需要
        try
        {
            h.OnMaxHealthChange?.Invoke(h);
        }
        catch
        {
        }

        try
        {
            h.OnHealthChange?.Invoke(h);
        }
        catch
        {
        }

        // 多帧重试：8 次、每 0.25s 一次（你已有 EnsureBarRoutine(h, attempts, interval)）
        StartCoroutine(EnsureBarRoutine(h, 8, 0.25f));
    }
    
    private void OnRPC_PlayerHealthReport(long senderConnectionId, NetDataReader reader)
    {
        if (!IsServer) return;

        float max = reader.GetFloat();
        float cur = reader.GetFloat();

        NetPeer senderPeer = null;
        if (netManager != null)
        {
            foreach (var p in netManager.ConnectedPeerList)
            {
                if (p.Id == senderConnectionId)
                {
                    senderPeer = p;
                    break;
                }
            }
        }

        if (senderPeer == null)
        {
            Debug.LogWarning($"[HealthM] OnRPC_PlayerHealthReport: Cannot find peer for connection {senderConnectionId}");
            return;
        }

        var endPoint = senderPeer.EndPoint?.ToString() ?? "unknown";
        var validator = Net.HybridP2P.HybridP2PValidator.Instance;
        if (validator != null && !validator.ValidateHealthUpdate(endPoint, max, cur))
        {
            Debug.LogWarning($"[HealthM] Health validation failed for {endPoint}, rejecting update");
            return;
        }

        if (!remoteCharacters.TryGetValue(senderPeer, out var go) || !go)
        {
            HealthTool._srvPendingHp[senderPeer] = (max, cur);
            return;
        }

        var h = go.GetComponentInChildren<Health>(true);
        if (!h) return;

        if (max <= 0f) return;
        
        _srvApplyingHealth.Add(h);
        try
        {
            ApplyHealthAndEnsureBar(go, max, cur);
        }
        finally
        {
            _srvApplyingHealth.Remove(h);
        }
    }

    private void OnRPC_AuthHealthSelf(long senderConnectionId, NetDataReader reader)
    {
        if (IsServer) return;

        float max = reader.GetFloat();
        float cur = reader.GetFloat();

        var main = CharacterMainControl.Main;
        if (!main) return;

        var go = main.gameObject;
        if (!go) return;

        _cliApplyingSelfSnap = true;
        ApplyHealthAndEnsureBar(go, max, cur);
        _cliApplyingSelfSnap = false;
        _cliEchoMuteUntil = Time.time + 0.15f;
    }

    private void OnRPC_AuthHealthRemote(long senderConnectionId, NetDataReader reader)
    {
        if (IsServer) return;

        string playerId = reader.GetString();
        float max = reader.GetFloat();
        float cur = reader.GetFloat();

        if (string.IsNullOrEmpty(playerId)) return;

        if (Service.IsSelfId(playerId)) return;

        if (clientRemoteCharacters.TryGetValue(playerId, out var go) && go)
        {
            ApplyHealthAndEnsureBar(go, max, cur);
        }
        else
        {
            CoopTool._cliPendingRemoteHp[playerId] = (max, cur);
        }
    }
}