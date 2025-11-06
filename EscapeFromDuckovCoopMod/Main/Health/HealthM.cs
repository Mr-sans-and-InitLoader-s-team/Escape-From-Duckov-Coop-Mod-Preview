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

using System;
using System.Collections;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public class HealthM : MonoBehaviour
{
    private const float CLIENT_SEND_COOLDOWN = 0.05f;
    private const float HEALTH_CHANGE_THRESHOLD = 0.5f;
    public static HealthM Instance;
    private static HealthTool.HealthSnapshot _cliLastSentHp = HealthTool._cliLastSentHp;
    private static float _cliNextSendHp = HealthTool._cliNextSendHp;
    private static uint _cliLastSequence = 0;

    private readonly Dictionary<Health, NetPeer> _srvHealthOwner = HealthTool._srvHealthOwner;
    private readonly Dictionary<string, (HealthTool.HealthSnapshot snapshot, uint sequence)> _srvLastBroadcastByPlayerId = new();
    private readonly Dictionary<NetPeer, uint> _srvLastClientSequence = new();
    private readonly Dictionary<string, uint> _cliLastRemoteSequence = new();
    private readonly Dictionary<Health, Coroutine> _ensureBarCoroutines = new();
    private readonly HashSet<Health> _appliedOnce = new();

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
        _srvLastBroadcastByPlayerId.Clear();
        _srvLastClientSequence.Clear();
        _cliLastRemoteSequence.Clear();
        foreach (var kv in _ensureBarCoroutines)
        {
            if (kv.Value != null) StopCoroutine(kv.Value);
        }
        _ensureBarCoroutines.Clear();
        _appliedOnce.Clear();
        _cliLastSequence = 0;
        UpdateClientSnapshotState(HealthTool.HealthSnapshot.Empty);
        UpdateClientCooldown(0f);
        RegisterRPCs();
    }
    
    private void RegisterRPCs()
    {
        var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null)
        {
            Debug.LogWarning("[HealthM] HybridRPCManager not found, fallback to legacy Op mode");
            _rpcRegistered = false;
            return;
        }

        rpcManager.RegisterRPC("PlayerHealthReport", OnRPC_PlayerHealthReport);
        rpcManager.RegisterRPC("AuthHealthRemote", OnRPC_AuthHealthRemote);
        
        _rpcRegistered = true;
        Debug.Log("[HealthM] Health sync optimized: RPC mode enabled, legacy Op as fallback");
    }

    internal bool TryGetClientMaxOverride(Health h, out float v)
    {
        return COOPManager.AIHandle._cliAiMaxOverride.TryGetValue(h, out v);
    }

    private static HealthTool.HealthSnapshot CaptureSnapshot(Health h)
    {
        if (!h) return HealthTool.HealthSnapshot.Empty;

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

        return new HealthTool.HealthSnapshot(max, cur);
    }

    private static void UpdateClientSnapshotState(HealthTool.HealthSnapshot snapshot)
    {
        _cliLastSentHp = snapshot;
        HealthTool._cliLastSentHp = snapshot;
    }

    private static void UpdateClientCooldown(float nextSendTime)
    {
        _cliNextSendHp = nextSendTime;
        HealthTool._cliNextSendHp = nextSendTime;
    }

    private bool Client_SendSnapshotToServer(HealthTool.HealthSnapshot snapshot, uint sequence)
    {
        var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null)
        {
            Debug.LogError("[HealthM] HybridRPCManager not available, cannot send health data");
            return false;
        }

        rpcManager.CallRPC("PlayerHealthReport", Net.HybridP2P.RPCTarget.Server, 0, writer =>
        {
            writer.Put(snapshot.Max);
            writer.Put(snapshot.Current);
            writer.Put(sequence);
        }, DeliveryMethod.ReliableOrdered);
        
        return true;
    }


    public void Client_SendSelfHealth(Health h, bool force)
    {
        if (!networkStarted || IsServer || h == null) return;

        var snapshot = CaptureSnapshot(h);

        if (!force)
        {
            if (snapshot.ApproximatelyEquals(_cliLastSentHp))
                return;

            var maxDiff = Mathf.Abs(snapshot.Max - _cliLastSentHp.Max);
            var curDiff = Mathf.Abs(snapshot.Current - _cliLastSentHp.Current);
            if (maxDiff < HEALTH_CHANGE_THRESHOLD && curDiff < HEALTH_CHANGE_THRESHOLD)
                return;

            var now = Time.time;
            if (now < _cliNextSendHp) return;
        }

        var nextSequence = unchecked(_cliLastSequence + 1);
        if (Client_SendSnapshotToServer(snapshot, nextSequence))
        {
            _cliLastSequence = nextSequence;
            UpdateClientSnapshotState(snapshot);
            UpdateClientCooldown(Time.time + CLIENT_SEND_COOLDOWN);
        }
    }

    internal void Client_ApplyRemoteSnapshot(string playerId, GameObject go, HealthTool.HealthSnapshot snapshot, uint sequence, bool force = false)
    {
        if (string.IsNullOrEmpty(playerId) || !go) return;

        if (!force && sequence != 0 &&
            _cliLastRemoteSequence.TryGetValue(playerId, out var lastSeq) &&
            !HealthTool.IsSequenceNewer(sequence, lastSeq))
            return;

        var h = go.GetComponentInChildren<Health>(true);
        float fallbackMax = 40f;
        if (h)
        {
            try
            {
                var candidate = h.MaxHealth;
                if (candidate > 0f) fallbackMax = candidate;
            }
            catch
            {
            }
        }

        var toApply = snapshot.WithFallbacks(fallbackMax);

        ApplyHealthAndEnsureBar(go, toApply.Max, toApply.Current);

        if (sequence != 0)
            _cliLastRemoteSequence[playerId] = sequence;
    }

    internal void Client_StorePendingRemoteSnapshot(string playerId, HealthTool.HealthSnapshot snapshot, uint sequence)
    {
        if (string.IsNullOrEmpty(playerId)) return;

        if (CoopTool._cliPendingRemoteHp.TryGetValue(playerId, out var existing))
        {
            var existingSeq = existing.sequence;
            if (sequence != 0 && existingSeq != 0 && !HealthTool.IsSequenceNewer(sequence, existingSeq))
                return;
        }

        CoopTool._cliPendingRemoteHp[playerId] = (snapshot, sequence);
    }


    public void Server_ForceAuthSelf(Health h)
    {
        if (!networkStarted || !IsServer || h == null) return;
        if (!_srvHealthOwner.TryGetValue(h, out var ownerPeer) || ownerPeer == null) return;

        var snapshot = CaptureSnapshot(h);
        Server_BroadcastClientHealth(ownerPeer, snapshot, true);
    }

    public void Server_ForwardHurtToOwner(NetPeer owner, DamageInfo di)
    {
        if (!IsServer || owner == null) return;

        var rpcManager = EscapeFromDuckovCoopMod.Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null) return;

        var connectionId = rpcManager.GetConnectionIdByPeer(owner);
        if (connectionId == 0) return;

        var dmgNormal = di.damageNormal.sqrMagnitude < 1e-6f ? Vector3.up : di.damageNormal.normalized;

        rpcManager.CallRPC("PlayerHurtEvent", EscapeFromDuckovCoopMod.Net.HybridP2P.RPCTarget.TargetClient, connectionId, w =>
        {
            w.Put(di.damageValue);
            w.Put(di.armorPiercing);
            w.Put(di.critDamageFactor);
            w.Put(di.critRate);
            w.Put(di.crit);
            w.Put(di.damagePoint.x);
            w.Put(di.damagePoint.y);
            w.Put(di.damagePoint.z);
            w.Put(dmgNormal.x);
            w.Put(dmgNormal.y);
            w.Put(dmgNormal.z);
            w.Put(di.fromWeaponItemID);
            w.Put(di.bleedChance);
            w.Put(di.isExplosion);
        }, DeliveryMethod.ReliableOrdered);
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

            var health = main.Health;

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

            health.Hurt(di);

            Client_SendSelfHealth(health, true);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CLIENT] apply self hurt from server failed: " + e);
        }
    }

    public void Client_ReportSelfHealth_IfReadyOnce()
    {
        if (IsServer || HealthTool._cliInitHpReported) return;
        if (connectedPeer == null || connectedPeer.ConnectionState != ConnectionState.Connected) return;

        var main = CharacterMainControl.Main;
        var h = main ? main.GetComponentInChildren<Health>(true) : null;
        if (!h) return;

        var snapshot = CaptureSnapshot(h);
        var now = Time.time;
        var nextSequence = unchecked(_cliLastSequence + 1);
        if (Client_SendSnapshotToServer(snapshot, nextSequence))
        {
            _cliLastSequence = nextSequence;
            UpdateClientSnapshotState(snapshot);
            UpdateClientCooldown(now + CLIENT_SEND_COOLDOWN);
            HealthTool._cliInitHpReported = true;
        }
    }

    public void Server_OnHealthChanged(NetPeer ownerPeer, Health h, bool force = false)
    {
        if (!IsServer || !h) return;
        if (ownerPeer != null) return;

        var snapshot = CaptureSnapshot(h);
        if (!snapshot.IsValid) return;

        Server_BroadcastClientHealth(null, snapshot, force);
    }

    public void Server_ProcessClientHealthReport(NetPeer peer, float max, float cur, uint sequence)
    {
        if (!IsServer || peer == null) return;

        var snapshot = new HealthTool.HealthSnapshot(max, cur);

        if (sequence == 0)
        {
            sequence = _srvLastClientSequence.TryGetValue(peer, out var last)
                ? unchecked(last + 1)
                : 1u;
        }

        if (_srvLastClientSequence.TryGetValue(peer, out var prevSeq) &&
            !HealthTool.IsSequenceNewer(sequence, prevSeq))
        {
            if (!snapshot.IsValid)
                HealthTool._srvPendingHp[peer] = (snapshot, prevSeq);
            return;
        }

        _srvLastClientSequence[peer] = sequence;

        if (!snapshot.IsValid)
        {
            HealthTool._srvPendingHp[peer] = (snapshot, sequence);
            return;
        }

        if (remoteCharacters != null && remoteCharacters.TryGetValue(peer, out var go) && go)
        {
            var h = go.GetComponentInChildren<Health>(true);
            if (!h) return;

            ApplyHealthAndEnsureBar(go, snapshot.Max, snapshot.Current);
            HealthTool._srvPendingHp.Remove(peer);
            Server_BroadcastClientHealth(peer, snapshot, false, sequence);
        }
        else
        {
            HealthTool._srvPendingHp[peer] = (snapshot, sequence);
        }
    }

    internal void Server_BroadcastClientHealth(NetPeer ownerPeer, HealthTool.HealthSnapshot snapshot, bool force = false, uint sequence = 0)
    {
        if (!IsServer || !snapshot.IsValid) return;

        var pid = NetService.Instance.GetPlayerId(ownerPeer);
        if (string.IsNullOrEmpty(pid)) return;

        if (!force && _srvLastBroadcastByPlayerId.TryGetValue(pid, out var last))
        {
            if (!HealthTool.IsSequenceNewer(sequence, last.sequence) && sequence != 0)
                return;

            if (last.snapshot.ApproximatelyEquals(snapshot))
            {
                var maxDiff = Mathf.Abs(snapshot.Max - last.snapshot.Max);
                var curDiff = Mathf.Abs(snapshot.Current - last.snapshot.Current);
                if (maxDiff < HEALTH_CHANGE_THRESHOLD && curDiff < HEALTH_CHANGE_THRESHOLD)
                    return;
            }
        }

        if (sequence == 0)
        {
            sequence = _srvLastBroadcastByPlayerId.TryGetValue(pid, out var prev)
                ? unchecked(prev.sequence + 1)
                : 1u;
        }
        else if (_srvLastBroadcastByPlayerId.TryGetValue(pid, out var existing) && sequence <= existing.sequence)
        {
            sequence = unchecked(existing.sequence + 1);
        }

        _srvLastBroadcastByPlayerId[pid] = (snapshot, sequence);

        var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null)
        {
            return;
        }

        try
        {
            rpcManager.CallRPC("PlayerHealthBroadcast", Net.HybridP2P.RPCTarget.AllClients, 0, writer =>
            {
                int beforePos = writer.Length;
                writer.Put(pid);
                writer.Put(snapshot.Max);
                writer.Put(snapshot.Current);
                writer.Put(sequence);
                int afterPos = writer.Length;
                int dataSize = afterPos - beforePos;
            }, DeliveryMethod.ReliableOrdered);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[HealthM] Failed to broadcast health for {pid}: {ex.Message}");
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
    private void EnsureBarLater(Health h, int attempts, float interval)
    {
        if (!h) return;

        if (_ensureBarCoroutines.TryGetValue(h, out var running) && running != null)
            return;

        var routine = StartCoroutine(EnsureBarRoutine(h, attempts, interval));
        if (routine != null)
            _ensureBarCoroutines[h] = routine;
    }

    private IEnumerator EnsureBarRoutine(Health h, int attempts, float interval)
    {
        try
        {
            for (var i = 0; i < attempts; i++)
            {
                if (!h) break;

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

                yield return new WaitForSeconds(interval);
            }
        }
        finally
        {
            _ensureBarCoroutines.Remove(h);
        }
    }

    // 把 (max,cur) 灌到 Health，并确保血条显示（修正 defaultMax=0）
    public void ForceSetHealth(Health h, float max, float cur, bool ensureBar = true, bool forceEvent = false)
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

        var maxChanged = false;

        // ★ 只要传入的 max 更大，就把 defaultMaxHealth 调到更大
        if (max > 0f && (nowMax <= 0f || max > nowMax + 0.0001f || defMax <= 0))
            try
            {
                HealthTool.FI_defaultMax?.SetValue(h, Mathf.RoundToInt(max));
                HealthTool.FI_lastMax?.SetValue(h, -12345f);
                maxChanged = true;
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

        var prevCur = 0f;
        try
        {
            prevCur = h.CurrentHealth;
        }
        catch
        {
        }

        var usedSetter = false;
        var healthChanged = false;

        if (effMax > 0f && cur > effMax + 0.0001f)
        {
            try
            {
                HealthTool.FI__current?.SetValue(h, cur);
                healthChanged = !Mathf.Approximately(prevCur, cur);
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
                usedSetter = true;
                float actual;
                try
                {
                    actual = h.CurrentHealth;
                }
                catch
                {
                    actual = cur;
                }

                healthChanged = !Mathf.Approximately(prevCur, actual);
            }
            catch
            {
                try
                {
                    HealthTool.FI__current?.SetValue(h, cur);
                    healthChanged = !Mathf.Approximately(prevCur, cur);
                }
                catch
                {
                }
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

            EnsureBarLater(h, 10, 0.2f);
        }

        if (forceEvent || maxChanged)
        {
            try
            {
                h.OnMaxHealthChange?.Invoke(h);
            }
            catch
            {
            }
        }

        if (forceEvent || (!usedSetter && healthChanged))
        {
            try
            {
                h.OnHealthChange?.Invoke(h);
            }
            catch
            {
            }
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

        var resolvedMax = max > 0f ? max : 40f;
        if (resolvedMax <= 0f) resolvedMax = 40f;
        var resolvedCur = cur > 0f ? cur : resolvedMax;

        var firstApply = _appliedOnce.Add(h);

        ForceSetHealth(h, resolvedMax, resolvedCur, firstApply, firstApply);

        try
        {
            h.showHealthBar = true;
        }
        catch
        {
        }

        if (firstApply)
        {
            try
            {
                h.RequestHealthBar();
            }
            catch
            {
            }

            EnsureBarLater(h, 8, 0.25f);
        }
    }
    
    private void OnRPC_PlayerHealthReport(long senderConnectionId, NetDataReader reader)
    {
        if (!IsServer) return;

        float max = reader.GetFloat();
        float cur = reader.GetFloat();
        uint sequence = reader.GetUInt();

        var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
        NetPeer senderPeer = rpcManager?.GetPeerByConnectionId(senderConnectionId);

        if (senderPeer == null && netManager != null)
        {
            var mapper = EscapeFromDuckovCoopMod.VirtualEndpointManager.Instance;
            
            foreach (var p in netManager.ConnectedPeerList)
            {
                bool matched = false;
                
                // 方式1: 直接匹配peer.Id
                if (p.Id > 0 && p.Id == senderConnectionId)
                {
                    matched = true;
                }
                // 方式2: 匹配peer.EndPoint.GetHashCode()
                else if (p.EndPoint != null && p.EndPoint.GetHashCode() == senderConnectionId)
                {
                    matched = true;
                }
                // 方式3: 匹配SteamID（Steam P2P模式）
                else if (mapper != null && p.EndPoint != null && 
                         mapper.TryGetSteamID(p.EndPoint as System.Net.IPEndPoint, out var steamId))
                {
                    if ((long)steamId.m_SteamID == senderConnectionId)
                    {
                        matched = true;
                    }
                }
                
                if (matched)
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
            Debug.LogWarning($"[HealthM] Health validation failed for {endPoint}, rejecting RPC update");
            return;
        }

        Server_ProcessClientHealthReport(senderPeer, max, cur, sequence);
    }

    private void OnRPC_AuthHealthRemote(long senderConnectionId, NetDataReader reader)
    {
        if (IsServer) return;

        string playerId = reader.GetString();
        float max = reader.GetFloat();
        float cur = reader.GetFloat();
        uint sequence = reader.GetUInt();

        if (string.IsNullOrEmpty(playerId)) return;

        if (Service.IsSelfId(playerId)) return;

        var snapshot = new HealthTool.HealthSnapshot(max, cur);

        if (clientRemoteCharacters.TryGetValue(playerId, out var go) && go)
        {
            Client_ApplyRemoteSnapshot(playerId, go, snapshot, sequence);
        }
        else
        {
            Client_StorePendingRemoteSnapshot(playerId, snapshot, sequence);
        }
    }
}
