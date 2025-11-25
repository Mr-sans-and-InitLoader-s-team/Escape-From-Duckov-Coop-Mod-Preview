using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public enum DifficultyLevel
{
    Easy = 0,
    Normal = 1,
    Hard = 2,
    VeryHard = 3,
    Impossible = 4
}

public readonly struct DifficultySettings
{
    public readonly float PatrolTurnSpeed;
    public readonly float CombatTurnSpeed;
    public readonly float BaseReactionTime;
    public readonly float ScatterMultiIfTargetRunning;
    public readonly float ScatterMultiIfOffScreen;

    public readonly float NightReactionTimeFactor;
    public readonly float HearingAbility;
    public readonly float TraceTargetChance;

    /// <summary>开火前延迟倍率（<1 更快开火，>1 更慢）</summary>
    public readonly float ShootDelayMultiplier;

    /// <summary>连续射击持续时间倍率（>1 打更久）</summary>
    public readonly float ShootTimeMultiplier;

    /// <summary>射击间隔倍率（<1 间隔更短，开火更频繁）</summary>
    public readonly float ShootIntervalMultiplier;

    /// <summary>战斗时移动时间倍率（>1 在战斗状态移动更久）</summary>
    public readonly float CombatMoveTimeMultiplier;

    /// <summary>视角倍率（>1 视野角度更大）</summary>
    public readonly float SightAngleMultiplier;

    /// <summary>视距倍率（>1 看得更远）</summary>
    public readonly float SightDistanceMultiplier;

    /// <summary>是否允许冲刺（按难度统一开/关）</summary>
    public readonly bool CanDash;

    /// <summary>冲刺冷却时间倍率（<1 冷却更短，更爱冲）</summary>
    public readonly float DashCoolTimeMultiplier;

    /// <summary>移动速度倍率（乘在 CharacterRandomPreset.moveSpeedFactor 上）</summary>
    public readonly float MoveSpeedFactor;

    /// <summary>子弹飞行速度倍率</summary>
    public readonly float BulletSpeedMultiplier;

    /// <summary>枪械射程倍率</summary>
    public readonly float GunDistanceMultiplier;

    /// <summary>AI 伤害倍率（乘在 damageMultiplier 上）</summary>
    public readonly float DamageMultiplier;

    public DifficultySettings(
        float patrolTurnSpeed,
        float combatTurnSpeed,
        float baseReactionTime,
        float scatterRunning,
        float scatterOffScreen,
        float nightReactionTimeFactor,
        float hearingAbility,
        float traceTargetChance,
        float shootDelayMultiplier,
        float shootTimeMultiplier,
        float shootIntervalMultiplier,
        float combatMoveTimeMultiplier,
        float sightAngleMultiplier,
        float sightDistanceMultiplier,
        bool canDash,
        float dashCoolTimeMultiplier,
        float moveSpeedFactor,
        float bulletSpeedMultiplier,
        float gunDistanceMultiplier,
        float damageMultiplier)
    {
        PatrolTurnSpeed = patrolTurnSpeed;
        CombatTurnSpeed = combatTurnSpeed;
        BaseReactionTime = baseReactionTime;
        ScatterMultiIfTargetRunning = scatterRunning;
        ScatterMultiIfOffScreen = scatterOffScreen;

        NightReactionTimeFactor = nightReactionTimeFactor;
        HearingAbility = hearingAbility;
        TraceTargetChance = traceTargetChance;

        ShootDelayMultiplier = shootDelayMultiplier;
        ShootTimeMultiplier = shootTimeMultiplier;
        ShootIntervalMultiplier = shootIntervalMultiplier;
        CombatMoveTimeMultiplier = combatMoveTimeMultiplier;

        SightAngleMultiplier = sightAngleMultiplier;
        SightDistanceMultiplier = sightDistanceMultiplier;

        CanDash = canDash;
        DashCoolTimeMultiplier = dashCoolTimeMultiplier;

        MoveSpeedFactor = moveSpeedFactor;
        BulletSpeedMultiplier = bulletSpeedMultiplier;
        GunDistanceMultiplier = gunDistanceMultiplier;
        DamageMultiplier = damageMultiplier;
    }
}



public static class DifficultyManager
{
    private static readonly Dictionary<DifficultyLevel, DifficultySettings> Settings = new()
    {
        // 参数顺序：
        // 巡逻转速, 战斗转速, 基础反应, 目标在跑散布, 屏幕外散布,
        // 夜晚反应倍率, 听觉倍率, 声音锁定概率,
        // 开火前延迟倍率, 连射持续倍率, 射击间隔倍率, 战斗移动时间倍率,
        // 视角倍率, 视距倍率, canDash, 冲刺冷却倍率,
        // 移动速度倍率, 子弹速度倍率, 射程倍率, 伤害倍率

        {
            DifficultyLevel.Easy,
            new DifficultySettings(
                200f, 1000f, 0.25f, 4f, 4f,
                1f,   1f,    1f,
                0f, 0f,  0f, 0f,
                0f, 0f,
                false, 0f,
                0f, 0f, 0f, 0f)
        },
        {
            DifficultyLevel.Normal,
            new DifficultySettings(
                270f, 1400f, 0.15f, 3f, 3f,
                1.5f, 1.5f,  1.5f,
                0.1f, 0.15f,  0.1f, 0.1f,
                0.1f, 0.1f,
                true,  0.2f,
                0.15f, 0.25f, 0.1f, 0.15f)
        },
        {
            DifficultyLevel.Hard,
            new DifficultySettings(
                350f, 1950f, 0.09f, 2f, 2f,
                2f,   2f,    2f,
                0.15f,0.25f,  0.15f, 0.15f,
                0.15f, 0.15f,
                true,  0.9f,
                0.25f, 0.3f, 0.15f, 0.25f)
        },
        {
            DifficultyLevel.VeryHard,
            new DifficultySettings(
                410f, 2300f, 0.05f, 1.5f, 1.5f,
                2.5f, 2.5f,  2.5f,
                0.25f, 0.33f,  0.25f, 0.25f,
                0.25f, 0.25f,
                true,  0.8f,
                0.4f, 0.35f, 0.25f, 0.35f)
        },
        {
            DifficultyLevel.Impossible,
            new DifficultySettings(
                500f, 3000f, 0.02f, 1f, 1f,
                3f,   3f,    3f,
                0.35f, 0.43f,  0.35f, 0.35f,
                0.35f, 0.35f,
                true,  0.7f,
                0.5f, 0.48f,  0.35f, 0.43f)
        }
    };

    public static DifficultySettings Get(DifficultyLevel level) => Settings[level];
    private static readonly Dictionary<DifficultyLevel, Sprite> Sprites = new();

    public static DifficultyLevel Selected { get; private set; } = DifficultyLevel.Normal;

    public static void SetDifficulty(DifficultyLevel level)
    {
        Selected = level;
    }

    public static DifficultySettings CurrentSettings => Settings.TryGetValue(Selected, out var value) ? value : Settings[DifficultyLevel.Normal];

    public static Sprite GetDifficultySprite(DifficultyLevel level)
    {
        if (Sprites.TryGetValue(level, out var cached))
            return cached;

        try
        {
            var asmLocation = typeof(DifficultyManager).Assembly.Location;
            var dir = Path.GetDirectoryName(asmLocation);
            if (dir == null)
                return null;

            var path = Path.Combine(dir, "Assets", $"Difficulty_{(int)level + 1}.png");
            if (!File.Exists(path))
                return null;

            var data = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, data))
                return null;

            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            Sprites[level] = sprite;
            return sprite;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DifficultyManager] Failed to load sprite for {level}: {ex.Message}");
            return null;
        }
    }

    public static string GetLocalizedName(DifficultyLevel level)
    {
        return level switch
        {
            DifficultyLevel.Easy => CoopLocalization.Get("ui.difficulty.easy"),
            DifficultyLevel.Normal => CoopLocalization.Get("ui.difficulty.normal"),
            DifficultyLevel.Hard => CoopLocalization.Get("ui.difficulty.hard"),
            DifficultyLevel.VeryHard => CoopLocalization.Get("ui.difficulty.veryHard"),
            DifficultyLevel.Impossible => CoopLocalization.Get("ui.difficulty.impossible"),
            _ => level.ToString()
        };
    }

    public static void ApplyToAI(AICharacterController ai)
    {
        if (ai == null)
            return;

        var s = CurrentSettings;



        ai.patrolTurnSpeed = s.PatrolTurnSpeed;
        ai.combatTurnSpeed = s.CombatTurnSpeed;

        ai.baseReactionTime = s.BaseReactionTime * LevelManager.Rule.EnemyReactionTimeFactor;
        ai.reactionTime = s.BaseReactionTime * LevelManager.Rule.EnemyReactionTimeFactor;

        ai.scatterMultiIfTargetRunning = s.ScatterMultiIfTargetRunning;
        ai.scatterMultiIfOffScreen = s.ScatterMultiIfOffScreen;

        ai.nightReactionTimeFactor = s.NightReactionTimeFactor;
        ai.hearingAbility = s.HearingAbility;
        ai.traceTargetChance = s.TraceTargetChance;

        // 刷一下旋转速度 Stat（保持你原来的反射逻辑）
        try
        {
            var rotateSpeedStat = Traverse.Create(ai).Field<Stat>("rotateSpeedStat").Value;
            if (rotateSpeedStat != null)
            {
                rotateSpeedStat.BaseValue = ai.patrolTurnSpeed;
            }
        }
        catch
        {
            // ignored
        }

        // 0f = 不变；0.1f = 在当前基础上 +10%（乘以 1.1）

        // --- 开火前延迟 ---
        if (Mathf.Abs(s.ShootDelayMultiplier) > 0.0001f)
        {
            ai.shootDelay *= (1f + s.ShootDelayMultiplier);
        }

        // --- 连续射击持续时间（Vector2） ---
        if (Mathf.Abs(s.ShootTimeMultiplier) > 0.0001f)
        {
            float factor = 1f + s.ShootTimeMultiplier;
            ai.shootTimeRange *= factor;               // Vector2 支持 * float
        }

        // --- 射击间隔（Vector2） ---
        if (Mathf.Abs(s.ShootIntervalMultiplier) > 0.0001f)
        {
            float factor = 1f - s.ShootIntervalMultiplier;
            // 防止变成 0 或负数
            factor = Mathf.Clamp(factor, 0.1f, 10f);

            ai.shootTimeSpaceRange *= factor;   // Vector2 支持 * float
        }

        // --- 战斗移动时间（Vector2） ---
        if (Mathf.Abs(s.CombatMoveTimeMultiplier) > 0.0001f)
        {
            float factor = 1f + s.CombatMoveTimeMultiplier;
            ai.combatMoveTimeRange *= factor;
        }

        // --- 视角 / 视距 ---
        if (Mathf.Abs(s.SightAngleMultiplier) > 0.0001f)
        {
            ai.sightAngle *= (1f + s.SightAngleMultiplier);
        }

        if (Mathf.Abs(s.SightDistanceMultiplier) > 0.0001f)
        {
            ai.sightDistance *= (1f + s.SightDistanceMultiplier);
        }

        // --- 冲刺开关 + 冷却 ---
        // 只有难度允许时才保留 canDash
        ai.canDash = ai.canDash && s.CanDash;

        // 冲刺冷却倍率特殊：0.1f = 冷却时间 -10%（乘以 0.9）
        if (Mathf.Abs(s.DashCoolTimeMultiplier) > 0.0001f)
        {
            float factor = 1f - s.DashCoolTimeMultiplier;
            // 防止冷却变成 0 或负数，给个下限
            factor = Mathf.Clamp(factor, 0.1f, 10f);
            ai.dashCoolTimeRange *= factor;
        }

        // ===== 3. 移动速度 / 子弹速度 / 射程 / 伤害：走 Stat 系统 =====
        var character = ai.CharacterMainControl;
        if (character != null)
        {
            var item = character.CharacterItem;
            if (item != null)
            {
                // 小工具：对某个 Stat 做 BaseValue *= (1 + 百分比)
                void MultiplyStatPercent(string statName, float percent)
                {
                    if (Mathf.Abs(percent) < 0.0001f)
                        return;

                    var stat = item.GetStat(statName.GetHashCode());
                    if (stat != null)
                    {
                        stat.BaseValue *= (1f + percent);
                    }
                }

                // --- 移动速度（Walk / Run）---
                MultiplyStatPercent("WalkSpeed", s.MoveSpeedFactor);
                MultiplyStatPercent("RunSpeed", s.MoveSpeedFactor);

                // --- 子弹飞行速度 ---
                MultiplyStatPercent("BulletSpeedMultiplier", s.BulletSpeedMultiplier);

                // --- 枪射程 ---
                MultiplyStatPercent("GunDistanceMultiplier", s.GunDistanceMultiplier);

                // --- 伤害倍率（枪 + 近战）---
                MultiplyStatPercent("GunDamageMultiplier", s.DamageMultiplier);
                MultiplyStatPercent("MeleeDamageMultiplier", s.DamageMultiplier);
            }
        }
    }







}
