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
using Duckov.Utilities;
using FX;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

public static class LocalHitKillFx
{
    private static FieldInfo _fiHurtVisual; // CharacterModel.hurtVisual (private global::HurtVisual)
    private static MethodInfo _miHvOnHurt, _miHvOnDead; // HurtVisual.OnHurt / OnDead (private)
    private static MethodInfo _miHmOnHit, _miHmOnKill; // HitMarker.OnHit / OnKill (private)

    private static float _lastBaseDamageForPop;

    private static void EnsureHurtVisualBindings(object characterModel, object hv)
    {
        if (_fiHurtVisual == null && characterModel != null)
            _fiHurtVisual = characterModel.GetType()
                .GetField("hurtVisual", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        if (hv != null)
        {
            var t = hv.GetType();
            if (_miHvOnHurt == null)
                _miHvOnHurt = t.GetMethod("OnHurt", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (_miHvOnDead == null)
                _miHvOnDead = t.GetMethod("OnDead", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }
    }

    public static void RememberLastBaseDamage(float v)
    {
        if (v > 0.01f) _lastBaseDamageForPop = v;
    }

    private static object FindHurtVisualOn(CharacterMainControl cmc)
    {
        if (!cmc) return null;
        var model = cmc.characterModel; // 公开字段可直接取到 CharacterModel
        if (model == null) return null;

        object hv = null;
        try
        {
            EnsureHurtVisualBindings(model, null);
            if (_fiHurtVisual != null)
                hv = _fiHurtVisual.GetValue(model);
        }
        catch
        {
        }

        // 兜底场景里找（有些模型可能没填字段）
        if (hv == null)
            try
            {
                hv = model.GetComponentInChildren(typeof(HurtVisual), true);
            }
            catch
            {
            }

        return hv;
    }

    private static object FindHitMarkerSingleton()
    {
        try
        {
            return Object.FindObjectOfType(typeof(HitMarker), true);
        }
        catch
        {
            return null;
        }
    }

    private static void PlayHurtVisual(object hv, DamageInfo di, bool predictedDead)
    {
        if (hv == null) return;
        EnsureHurtVisualBindings(null, hv);

        try
        {
            _miHvOnHurt?.Invoke(hv, new object[] { di });
        }
        catch
        {
        }

        if (predictedDead)
            try
            {
                _miHvOnDead?.Invoke(hv, new object[] { di });
            }
            catch
            {
            }
    }

    public static void PopDamageText(Vector3 hintPos, DamageInfo di)
    {
        try
        {
            if (PopText.instance)
            {
                var look = GameplayDataSettings.UIStyle.GetElementDamagePopTextLook(ElementTypes.physics);
                var size = di.crit > 0 ? look.critSize : look.normalSize;
                var sprite = di.crit > 0 ? GameplayDataSettings.UIStyle.CritPopSprite : null;
                Debug.Log(di.damageValue + " " + di.finalDamage);
                var _display = di.damageValue;
                // 某些路径里 DamageInfo.damageValue 会被归一化为 1；为避免弹字恒为 1.0，做一个兜底：
                if (_display <= 1.001f && _lastBaseDamageForPop > 0f)
                {
                    var critMul = di.crit > 0 && di.critDamageFactor > 0f ? di.critDamageFactor : 1f;
                    _display = Mathf.Max(_display, _lastBaseDamageForPop * critMul);
                }

                var text = _display > 0f ? _display.ToString("F1") : "HIT";
                PopText.Pop(text, hintPos, look.color, size, sprite);
            }
        }
        catch
        {
        }
    }

    // 只在“本地命中路径”里，必要时把 fromCharacter 强制设为 Main，以满足 HitMarker 的判断
    private static void PlayUiHitKill(DamageInfo di, bool predictedDead, bool forceLocalMain)
    {
        var hm = FindHitMarkerSingleton();
        if (hm == null) return;

        if (_miHmOnHit == null)
            _miHmOnHit = hm.GetType().GetMethod("OnHit", BindingFlags.Instance | BindingFlags.NonPublic);
        if (_miHmOnKill == null)
            _miHmOnKill = hm.GetType().GetMethod("OnKill", BindingFlags.Instance | BindingFlags.NonPublic);

        if (forceLocalMain)
            try
            {
                if (di.fromCharacter == null || di.fromCharacter != CharacterMainControl.Main)
                    di.fromCharacter = CharacterMainControl.Main;
            }
            catch
            {
            }

        try
        {
            _miHmOnHit?.Invoke(hm, new object[] { di });
        }
        catch
        {
        }

        if (predictedDead)
            try
            {
                _miHmOnKill?.Invoke(hm, new object[] { di });
            }
            catch
            {
            }
    }

    /// <summary>
    ///     客户端本地：玩家 → AI 命中（子弹/爆炸都可）。在“已拦截伤害”的前缀里调用。
    /// </summary>
    public static void ClientPlayForAI(CharacterMainControl victim, DamageInfo di, bool predictedDead)
    {
        // 1) AI 模型上的受击/死亡可视化（私有 OnHurt/OnDead）
        var hv = FindHurtVisualOn(victim);
        PlayHurtVisual(hv, di, predictedDead);

        // 2) UI 命中/击杀标记（私有 OnHit/OnKill）——只在本地命中路径强制 fromCharacter=Main
        PlayUiHitKill(di, predictedDead, true);

        // 3) 伤害数字
        var pos = (di.damagePoint.sqrMagnitude > 1e-6f ? di.damagePoint : victim.transform.position) + Vector3.up * 2f;
        PopDamageText(pos, di);
    }

    /// <summary>
    ///     客户端本地：玩家 → 场景可破坏物（HSB）
    /// </summary>
    public static void ClientPlayForDestructible(HealthSimpleBase hs, DamageInfo di, bool predictedDead)
    {
        // 复用 UI 命中/击杀标记（为保持手感，障碍物也给个命中标，但仍仅在本地命中时触发）
        PlayUiHitKill(di, predictedDead, true);

        // 伤害数字（位置优先用命中点）
        var basePos = hs ? hs.transform.position : Vector3.zero;
        var pos = (di.damagePoint.sqrMagnitude > 1e-6f ? di.damagePoint : basePos) + Vector3.up * 2f;
        PopDamageText(pos, di);
    }
}