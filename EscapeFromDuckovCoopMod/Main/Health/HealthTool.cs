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

using System.Reflection;
using Duckov.UI;
using LiteNetLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

public static class HealthTool
{
    public static bool _cliHookedSelf;
    public static UnityAction<Health> _cbSelfHpChanged, _cbSelfMaxChanged;
    public static UnityAction<DamageInfo> _cbSelfHurt, _cbSelfDead;


    // 反射字段（Health 反编译字段）研究了20年研究出来的
    public static readonly FieldInfo FI_defaultMax =
        typeof(Health).GetField("defaultMaxHealth", BindingFlags.NonPublic | BindingFlags.Instance);

    public static readonly FieldInfo FI_lastMax =
        typeof(Health).GetField("lastMaxHealth", BindingFlags.NonPublic | BindingFlags.Instance);

    public static readonly FieldInfo FI__current =
        typeof(Health).GetField("_currentHealth", BindingFlags.NonPublic | BindingFlags.Instance);

    public static readonly FieldInfo FI_characterCached =
        typeof(Health).GetField("characterCached", BindingFlags.NonPublic | BindingFlags.Instance);

    public static readonly FieldInfo FI_hasCharacter =
        typeof(Health).GetField("hasCharacter", BindingFlags.NonPublic | BindingFlags.Instance);

    private static NetService Service => NetService.Instance;
    private static Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;


    // 小工具：仅做UI表现，不改数值与事件
    public static void TryShowDamageBarUI(Health h, float damage)
    {
        if (h == null || damage <= 0f) return;

        try
        {
            // 1) 找到当前 HealthBar
            var hbm = HealthBarManager.Instance;
            if (hbm == null) return;

            var miGet = AccessTools.DeclaredMethod(typeof(HealthBarManager), "GetActiveHealthBar", new[] { typeof(Health) });
            var hb = miGet?.Invoke(hbm, new object[] { h });
            if (hb == null) return;

            // 2) 取得 fill 的 rect 宽度（像素）
            var fiFill = AccessTools.Field(typeof(HealthBar), "fill");
            var fillImg = fiFill?.GetValue(hb) as Image;
            var width = 0f;
            if (fillImg != null)
                // 注意：rect 是本地空间宽度，足够用于“最小像素宽度”
                width = fillImg.rectTransform.rect.width;

            // 3) 计算“最小可见伤害”
            //    - minPixels: 小伤害条至少显示这么宽
            //    - minPercent: 即使宽度没取到，也保证一个极小百分比
            const float minPixels = 2f;
            const float minPercent = 0.0015f; // 0.15%

            var maxHp = Mathf.Max(1f, h.MaxHealth);
            var minByPixels = width > 0f ? minPixels / width * maxHp : 0f;
            var minByPercent = minPercent * maxHp;
            var minDamageToShow = Mathf.Max(minByPixels, minByPercent);

            // 4) 以“实际伤害 or 最小可见伤害”的较大者来显示受击条（仅视觉，不改真实血量）
            var visualDamage = Mathf.Max(damage, minDamageToShow);

            // 5) 反射调用 HealthBar.ShowDamageBar(float)
            var miShow = AccessTools.DeclaredMethod(typeof(HealthBar), "ShowDamageBar", new[] { typeof(float) });
            miShow?.Invoke(hb, new object[] { visualDamage });
        }
        catch
        {
            // 静默失败，避免 UI 缺失导致报错
        }
    }

    public static void Server_HookOneHealth(NetPeer peer, GameObject instance)
    {
        if (!instance) return;

        var h = instance.GetComponentInChildren<Health>(true);
        var cmc = instance.GetComponent<CharacterMainControl>();
        if (!h) return;

        try
        {
            h.autoInit = false;
        }
        catch
        {
        }

        BindHealthToCharacter(h, cmc); // 你已有：修正 hasCharacter 以便 UI/Hidden 逻辑正常
        if (peer == null)
        {
            HealthM.Instance?.NotifyLocalHealthChanged(h, null);
            return;
        }

        HealthM.Instance?.Server_ApplyCachedHealth(peer, instance);
    }


    public static void Client_HookSelfHealth()
    {
        if (_cliHookedSelf) return;
        var main = CharacterMainControl.Main;
        var h = main ? main.GetComponentInChildren<Health>(true) : null;
        if (!h) return;

        EnsureSelfHealthInitialized(main, h);

        _cbSelfHpChanged = _ => HealthM.Instance.NotifyLocalHealthChanged(h, null);
        _cbSelfMaxChanged = _ => HealthM.Instance.NotifyLocalHealthChanged(h, null);
        _cbSelfHurt = di =>
        {
            HealthM.Instance.NotifyLocalHealthChanged(h, di);
        };
        _cbSelfDead = _ => HealthM.Instance.NotifyLocalHealthChanged(h, null);

        h.OnHealthChange.AddListener(_cbSelfHpChanged);
        h.OnMaxHealthChange.AddListener(_cbSelfMaxChanged);
        h.OnHurtEvent.AddListener(_cbSelfHurt);
        h.OnDeadEvent.AddListener(_cbSelfDead);

        _cliHookedSelf = true;

        HealthM.Instance.NotifyLocalHealthChanged(h, null);
    }

    public static void Client_UnhookSelfHealth()
    {
        if (!_cliHookedSelf) return;
        var main = CharacterMainControl.Main;
        var h = main ? main.GetComponentInChildren<Health>(true) : null;
        if (h)
        {
            if (_cbSelfHpChanged != null) h.OnHealthChange.RemoveListener(_cbSelfHpChanged);
            if (_cbSelfMaxChanged != null) h.OnMaxHealthChange.RemoveListener(_cbSelfMaxChanged);
            if (_cbSelfHurt != null) h.OnHurtEvent.RemoveListener(_cbSelfHurt);
            if (_cbSelfDead != null) h.OnDeadEvent.RemoveListener(_cbSelfDead);
        }

        _cliHookedSelf = false;
        _cbSelfHpChanged = _cbSelfMaxChanged = null;
        _cbSelfHurt = _cbSelfDead = null;
    }

    // 绑定 Health⇄Character，修复“Health 没绑定角色”导致的 UI/Hidden 逻辑缺参
    public static void BindHealthToCharacter(Health h, CharacterMainControl cmc)
    {
        try
        {
            FI_characterCached?.SetValue(h, cmc);
            FI_hasCharacter?.SetValue(h, true);
        }
        catch
        {
        }
    }

    private static void EnsureSelfHealthInitialized(CharacterMainControl cmc, Health h)
    {
        if (!cmc || !h) return;

        var max = 0f;
        var cur = 0f;

        try { max = h.MaxHealth; }
        catch { }

        try { cur = h.CurrentHealth; }
        catch { }

        if (max > 0.01f && cur > 0.01f) return;

        var fallbackMax = max;
        if (fallbackMax <= 0f)
        {
            var preset = cmc.characterPreset;
            if (preset)
                fallbackMax = Mathf.Max(1f, preset.health);
        }

        if (fallbackMax <= 0f)
            fallbackMax = 40f;

        var fallbackCur = cur > 0.01f ? cur : fallbackMax;
        HealthM.Instance?.ForceSetHealth(h, fallbackMax, fallbackCur);
    }
}
