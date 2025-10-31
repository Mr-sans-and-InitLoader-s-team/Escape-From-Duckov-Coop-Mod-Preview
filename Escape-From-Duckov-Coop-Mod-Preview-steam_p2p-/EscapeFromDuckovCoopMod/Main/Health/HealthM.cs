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

namespace EscapeFromDuckovCoopMod;

public class HealthM : MonoBehaviour
{
    private const float SRV_HP_SEND_COOLDOWN = 0.05f; // 20Hz
    public static HealthM Instance;
    private static (float max, float cur) _cliLastSentHp = HealthTool._cliLastSentHp;
    private static float _cliNextSendHp = HealthTool._cliNextSendHp;

    public bool _cliApplyingSelfSnap;
    public float _cliEchoMuteUntil;
    private readonly Dictionary<Health, NetPeer> _srvHealthOwner = HealthTool._srvHealthOwner;

    // 主机端：节流去抖
    private readonly Dictionary<Health, (float max, float cur)> _srvLastSent = new();
    private readonly Dictionary<Health, float> _srvNextSend = new();

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

    internal bool TryGetClientMaxOverride(Health h, out float v)
    {
        return COOPManager.AIHandle._cliAiMaxOverride.TryGetValue(h, out v);
    }


    // 发送自身血量（带 20Hz 节流 & 值未变不发）
    public void Client_SendSelfHealth(Health h, bool force)
    {
        if (_cliApplyingSelfSnap || Time.time < _cliEchoMuteUntil) return;

        if (!networkStarted || IsServer || connectedPeer == null || h == null) return;

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

        // 去抖：值相同直接跳过
        if (!force && Mathf.Approximately(max, _cliLastSentHp.max) && Mathf.Approximately(cur, _cliLastSentHp.cur))
            return;

        // 节流：20Hz
        if (!force && Time.time < _cliNextSendHp) return;

        var w = new NetDataWriter();
        w.Put((byte)Op.PLAYER_HEALTH_REPORT);
        w.Put(max);
        w.Put(cur);
        connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);

        _cliLastSentHp = (max, cur);
        _cliNextSendHp = Time.time + 0.05f;
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

    // 主机：把 DamageInfo（简化字段）发给拥有者客户端，让其本地执行 Hurt
    public void Server_ForwardHurtToOwner(NetPeer owner, DamageInfo di)
    {
        if (!IsServer || owner == null) return;

        var w = new NetDataWriter();
        w.Put((byte)Op.PLAYER_HURT_EVENT);

        // 参照你现有近战上报字段进行对称序列化
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
        // 去抖 + 限频（与你现有字段保持一致）
        if (_srvLastSent.TryGetValue(h, out var last))
            if (Mathf.Approximately(max, last.max) && Mathf.Approximately(cur, last.cur))
                return;

        var now = Time.time;
        if (_srvNextSend.TryGetValue(h, out var tNext) && now < tNext)
            return;

        _srvLastSent[h] = (max, cur);
        _srvNextSend[h] = now + SRV_HP_SEND_COOLDOWN;

        // 计算 playerId（你已有的辅助方法）
        var pid = NetService.Instance.GetPlayerId(ownerPeer);

        // ✅ 回传本人快照：AUTH_HEALTH_SELF（修复“自己本地看起来没伤害”的现象）
        if (ownerPeer != null && ownerPeer.ConnectionState == ConnectionState.Connected)
        {
            var w1 = new NetDataWriter();
            w1.Put((byte)Op.AUTH_HEALTH_SELF);
            w1.Put(max);
            w1.Put(cur);
            ownerPeer.Send(w1, DeliveryMethod.ReliableOrdered);
        }

        // ✅ 广播给其他玩家：AUTH_HEALTH_REMOTE（带 playerId）
        var w2 = new NetDataWriter();
        w2.Put((byte)Op.AUTH_HEALTH_REMOTE);
        w2.Put(pid);
        w2.Put(max);
        w2.Put(cur);

        foreach (var p in netManager.ConnectedPeerList)
        {
            if (p == ownerPeer) continue; // 跳过本人，避免重复
            p.Send(w2, DeliveryMethod.ReliableOrdered);
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
}