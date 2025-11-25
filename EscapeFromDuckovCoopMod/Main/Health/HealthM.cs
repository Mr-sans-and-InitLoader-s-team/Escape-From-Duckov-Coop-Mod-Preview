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

using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using LiteNetLib;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using UnityEngine;
using static Unity.Burst.Intrinsics.X86.Avx;

namespace EscapeFromDuckovCoopMod;

public class HealthM : MonoBehaviour
{
    private const float CLIENT_SEND_INTERVAL = 0.05f; // 20Hz
    private const float SERVER_SEND_INTERVAL = 0.05f;

    public static HealthM Instance;

    private readonly Dictionary<string, (float max, float cur)> _srvPlayerSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _srvNextBroadcast = new(StringComparer.OrdinalIgnoreCase);

    private (float max, float cur) _cliLastSentHp;
    private float _cliNextSendHp;
    private float _cliNextHeartbeat;

    private NetService Service => NetService.Instance;
    private bool IsServer => Service != null && Service.IsServer;
    private bool networkStarted => Service != null && Service.networkStarted;

    private Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;

    private MethodInfo _miCmcOnDead;

    public void Init()
    {
        Instance = this;
    }

    public void NotifyLocalHealthChanged(Health health, DamageInfo? damage)
    {
        if (!networkStarted || health == null) return;
        Debug.Log($"NotifyLocalHealthChanged {health.CurrentHealth} max:{health.MaxHealth}");
        if (IsServer)
            Server_BroadcastHostSnapshot(health, damage);
        else
            Client_SendSnapshot(health, damage);
    }

    private void Update()
    {
        if (IsServer || !networkStarted) return;

        if (Time.time < _cliNextHeartbeat) return;
        _cliNextHeartbeat = Time.time + 3f;

        var main = CharacterMainControl.Main;
        var health = main ? main.Health : null;
        if (!health) return;

        Client_SendSnapshot(health, null, true);
    }

    private void Client_SendSnapshot(Health health, DamageInfo? damage, bool force = false)
    {
        var peer = Service?.connectedPeer;
        if (peer == null || peer.ConnectionState != ConnectionState.Connected) return;

        var (max, cur) = ReadHealth(health);
        if (max <= 0f) return;
        Debug.Log($"Client_SendSnapshot {health.CurrentHealth} max:{health.MaxHealth}");
        var now = Time.time;
        force |= damage.HasValue;
        if (!force)
        {
            if (Mathf.Approximately(max, _cliLastSentHp.max) && Mathf.Approximately(cur, _cliLastSentHp.cur))
                if (now < _cliNextSendHp) return;
        }

        var rpc = new PlayerHealthReportRpc
        {
            MaxHealth = max,
            CurrentHealth = cur,
            HasDamage = damage.HasValue,
            Damage = DamageForwardPayload.FromDamageInfo(damage)
        };

        CoopTool.SendRpc(in rpc);

        _cliLastSentHp = (max, cur);
        _cliNextSendHp = now + CLIENT_SEND_INTERVAL;
    }

    private void Server_BroadcastHostSnapshot(Health health, DamageInfo? damage)
    {
        var service = Service;
        if (service == null) return;
        var playerId = service.GetPlayerId(null);
        if (string.IsNullOrEmpty(playerId)) return;

        var (max, cur) = ReadHealth(health);
        if (max <= 0f) return;


        BroadcastPlayerSnapshot(playerId, max, cur, damage.HasValue ? DamageForwardPayload.FromDamageInfo(damage) : (DamageForwardPayload?)null, null);
    }

    private static (float max, float cur) ReadHealth(Health health)
    {
        float max = 0f, cur = 0f;
        try { max = health.MaxHealth; }
        catch { }

        try { cur = health.CurrentHealth; }
        catch { }

        return (max, cur);
    }

    private void BroadcastPlayerSnapshot(string playerId, float max, float cur, DamageForwardPayload? damage, NetPeer excludePeer)
    {
        if (!IsServer || string.IsNullOrEmpty(playerId) || max <= 0f) return;

        _srvPlayerSnapshots[playerId] = (max, cur);
        _srvNextBroadcast[playerId] = Time.time + SERVER_SEND_INTERVAL;

        var rpc = new PlayerHealthBroadcastRpc
        {
            PlayerId = playerId,
            MaxHealth = max,
            CurrentHealth = cur,
            HasDamage = damage.HasValue,
            Damage = damage ?? default
        };

        CoopTool.SendRpc(in rpc, excludePeer);
    }

    public void Server_HandlePlayerHealthReport(NetPeer sender, PlayerHealthReportRpc message)
    {
        if (!IsServer || sender == null) return;

        var service = Service;
        var playerId = service?.GetPlayerId(sender);
        if (string.IsNullOrEmpty(playerId)) return;

        var max = Mathf.Max(1f, message.MaxHealth);
        var cur = Mathf.Clamp(message.CurrentHealth, 0f, max);


        if (remoteCharacters != null && remoteCharacters.TryGetValue(sender, out var go) && go)
            ApplyHealthAndEnsureBar(go, max, cur);
        else
        {
            _srvPlayerSnapshots[playerId] = (max, cur);

            Server_TrySpawnMissingRemote(sender, playerId, max, cur);
        }

        BroadcastPlayerSnapshot(playerId, max, cur, message.HasDamage ? message.Damage : (DamageForwardPayload?)null, sender);
    }

    public void Client_HandlePlayerHealthBroadcast(PlayerHealthBroadcastRpc message)
    {
        if (IsServer || string.IsNullOrEmpty(message.PlayerId)) return;
        if (Service != null && Service.IsSelfId(message.PlayerId)) return;

        var max = Mathf.Max(1f, message.MaxHealth);
        var cur = Mathf.Clamp(message.CurrentHealth, 0f, max);

        if (clientRemoteCharacters != null && clientRemoteCharacters.TryGetValue(message.PlayerId, out var go) && go)
            ApplyHealthAndEnsureBar(go, max, cur);
        else
        {
            CoopTool._cliPendingRemoteHp[message.PlayerId] = (max, cur);

            Client_TrySpawnMissingRemote(message.PlayerId, max, cur);
        }
    }

    public void Client_HandlePlayerDamageForward(PlayerDamageForwardRpc message)
    {
        if (IsServer) return;
        var service = Service;
        if (service == null) return;
        if (LocalPlayerManager.Instance != null && LocalPlayerManager.Instance.IsLocalInvincible())
            return;
        // 允许空 PlayerId 或不匹配的转发继续执行，恢复客户端对自身伤害的本地处理

        var main = CharacterMainControl.Main;
        var health = main ? main.GetComponentInChildren<Health>(true) : null;
        if (!health) return;

        var receiver = main ? main.mainDamageReceiver : null;
        var damage = message.Damage.ToDamageInfo(null, receiver);

        try
        {
            health.Hurt(damage);
        }
        catch
        {
        }
    }

    public void Server_ApplyCachedHealth(NetPeer peer, GameObject instance)
    {
        if (!IsServer || instance == null) return;
        var service = Service;
        var playerId = service?.GetPlayerId(peer);
        if (string.IsNullOrEmpty(playerId)) return;
        if (!_srvPlayerSnapshots.TryGetValue(playerId, out var snap)) return;
       // Debug.Log("Server_ApplyCachedHealth "+ snap.max+" "+snap.cur);
        ApplyHealthAndEnsureBar(instance, snap.max, snap.cur);
    }

    public void Server_EnsureAllHealthHooks()
    {
        if (!IsServer || !networkStarted) return;

        if (remoteCharacters != null)
            foreach (var kv in remoteCharacters)
                if (kv.Value)
                    Server_ApplyCachedHealth(kv.Key, kv.Value);
    }

    public void Server_SendAllSnapshotsTo(NetPeer peer)
    {
        if (!IsServer || peer == null) return;

        foreach (var kv in _srvPlayerSnapshots)
        {
            var playerId = kv.Key;
            if (string.IsNullOrEmpty(playerId))
                continue;

            var (max, cur) = kv.Value;
            var rpc = new PlayerHealthBroadcastRpc
            {
                PlayerId = playerId,
                MaxHealth = max,
                CurrentHealth = cur,
                HasDamage = false,
                Damage = default
            };

            CoopTool.SendRpcTo(peer, in rpc);
        }
    }

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

    public void ForceSetHealth(Health h, float max, float cur, bool ensureBar = true, float? bodyArmor = null, float? headArmor = null)
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

        if (max > 0f && (nowMax <= 0f || max > nowMax + 0.0001f || defMax <= 0))
            try
            {
                HealthTool.FI_defaultMax?.SetValue(h, Mathf.RoundToInt(max));
                HealthTool.FI_lastMax?.SetValue(h, -12345f);
                h.OnMaxHealthChange?.Invoke(h);
                var characterItemInstance = h.TryGetCharacter().CharacterItem;
                if (characterItemInstance != null)
                {
                    var stat = characterItemInstance.GetStat("MaxHealth".GetHashCode());
                    if (stat != null)
                    {
                        var rule = LevelManager.Rule;
                        var factor = rule != null ? rule.EnemyHealthFactor : 1f;
                        stat.BaseValue = max;
                    }
                    ApplyArmorStats(characterItemInstance, bodyArmor, headArmor);
                }
            }
            catch
            {
            }
        else
        {
            try
            {
                var characterItemInstance = h.TryGetCharacter().CharacterItem;
                if (characterItemInstance != null)
                    ApplyArmorStats(characterItemInstance, bodyArmor, headArmor);
            }
            catch
            {
            }
        }

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

            StartCoroutine(EnsureBarRoutine(h, 2, 0.1f));
        }
    }

    private static void ApplyArmorStats(Item characterItemInstance, float? bodyArmor, float? headArmor)
    {
        if (characterItemInstance == null) return;

        try
        {
            if (bodyArmor.HasValue)
            {
                Item item = characterItemInstance;
                var stat = item.GetStat("BodyArmor".GetHashCode());
                if (stat != null)
                    stat.BaseValue = bodyArmor.Value;
            }
        }
        catch
        {
        }

        try
        {
            if (headArmor.HasValue)
            {
                Item item = characterItemInstance;
                var stat = item.GetStat("HeadArmor".GetHashCode());
                if (stat != null)
                    stat.BaseValue = headArmor.Value;
            }
        }
        catch
        {
        }
    }

    public void ApplyHealthAndEnsureBar(GameObject go, float max, float cur)
    {
        if (!go) return;
        
        var cmc = go.GetComponent<CharacterMainControl>();
        var h = cmc.Health;
        if (!cmc || !h) return;

        try
        {
            h.autoInit = false;
        }
        catch
        {
        }
      //  Debug.Log("ApplyHealthAndEnsureBar "+cmc.Health.MaxHealth);
        HealthTool.BindHealthToCharacter(h, cmc);

        var clampedCur = Mathf.Max(0f, cur);
        ForceSetHealth(h, max, clampedCur, false);

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

        StartCoroutine(EnsureBarRoutine(h, 2, 0.25f));

        EnsureRemoteDeathState(cmc, h, clampedCur);
    }

    private void Server_TrySpawnMissingRemote(NetPeer peer, string playerId, float max, float cur)
    {
        if (!IsServer || !networkStarted || Service == null || peer == null) return;
        if (cur <= 0f || max <= 0f) return; // 不生成死亡/无效角色
        if (remoteCharacters != null && remoteCharacters.TryGetValue(peer, out var existing) && existing) return;

        if (!Service.playerStatuses.TryGetValue(peer, out var st) || st == null || !st.IsInGame) return;

        var mySceneId = Service.localPlayerStatus != null ? Service.localPlayerStatus.SceneId : null;
        if (string.IsNullOrEmpty(mySceneId))
            LocalPlayerManager.Instance.ComputeIsInGame(out mySceneId);
        if (!Spectator.AreSameMap(mySceneId, st.SceneId)) return;

        var pos = st.Position;
        var rot = st.Rotation;
        if (!IsFinite(pos) || !IsFinite(rot)) return;

        CreateRemoteCharacter.CreateRemoteCharacterAsync(peer, pos, rot, st.CustomFaceJson).Forget();
    }

    private void Client_TrySpawnMissingRemote(string playerId, float max, float cur)
    {
        if (IsServer || !networkStarted || Service == null || string.IsNullOrEmpty(playerId)) return;
        if (cur <= 0f || max <= 0f) return; // 不生成死亡/无效角色
        if (clientRemoteCharacters != null && clientRemoteCharacters.TryGetValue(playerId, out var existing) && existing) return;

        if (!Service.clientPlayerStatuses.TryGetValue(playerId, out var st) || st == null || !st.IsInGame) return;

        var mySceneId = Service.localPlayerStatus != null ? Service.localPlayerStatus.SceneId : null;
        if (string.IsNullOrEmpty(mySceneId))
            LocalPlayerManager.Instance.ComputeIsInGame(out mySceneId);
        if (!Spectator.AreSameMap(mySceneId, st.SceneId)) return;

        var pos = st.Position;
        var rot = st.Rotation;
        if (!IsFinite(pos) || !IsFinite(rot)) return;

        CreateRemoteCharacter.CreateRemoteCharacterForClient(playerId, pos, rot, st.CustomFaceJson).Forget();
    }

    private static bool IsFinite(Vector3 value)
    {
        return !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) ||
                 float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z));
    }

    private static bool IsFinite(Quaternion value)
    {
        return !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) || float.IsNaN(value.w) ||
                 float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z) || float.IsInfinity(value.w));
    }

    public void ForceRemoteOnDead(CharacterMainControl cmc)
    {
        if (cmc == null || cmc == CharacterMainControl.Main) return;

        var h = cmc.Health;
        if (h == null) return;

        if (cmc.Health.CurrentHealth <= 0)
        {
            GameObject.Destroy(cmc.gameObject);
        }

    }

    private void EnsureRemoteDeathState(CharacterMainControl cmc, Health h, float cur)
    {
        if (cmc == null || h == null) return;
        if (cmc == CharacterMainControl.Main) return; // 自己的死亡流程由本地逻辑处理

        var id = cmc.GetInstanceID();

        if(cur <= 0)
        {
            GameObject.Destroy(cmc.gameObject);
        }

    }

  
}
