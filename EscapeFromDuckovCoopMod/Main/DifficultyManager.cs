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
    Impossible = 4,
    Custom = 5
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

    /// <summary>AI 血量倍率（受联机人数附加倍率影响，总上限 2.5x）</summary>
    public readonly float HealthMultiplier;

    /// <summary>敌人生成数量附加倍率（加在当前倍率上，而非覆盖），例：+1 = 在现有基础上再加 1 倍</summary>
    public readonly float EnemySpawnBonusMultiplier;

    /// <summary>是否强制生成 Boss</summary>
    public readonly bool ForceBossSpawn;

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
        float damageMultiplier,
        float healthMultiplier,
        float enemySpawnBonusMultiplier = 0f,
        bool forceBossSpawn = false)
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
        HealthMultiplier = healthMultiplier;
        EnemySpawnBonusMultiplier = enemySpawnBonusMultiplier;
        ForceBossSpawn = forceBossSpawn;
    }

}

[Serializable]
public sealed class DifficultyCustomSettings
{
    public float PatrolTurnSpeed;
    public float CombatTurnSpeed;
    public float BaseReactionTime;
    public float ScatterMultiIfTargetRunning;
    public float ScatterMultiIfOffScreen;
    public float NightReactionTimeFactor;
    public float HearingAbility;
    public float TraceTargetChance;
    public float ShootDelayMultiplier;
    public float ShootTimeMultiplier;
    public float ShootIntervalMultiplier;
    public float CombatMoveTimeMultiplier;
    public float SightAngleMultiplier;
    public float SightDistanceMultiplier;
    public bool CanDash;
    public float DashCoolTimeMultiplier;
    public float MoveSpeedFactor;
    public float BulletSpeedMultiplier;
    public float GunDistanceMultiplier;
    public float DamageMultiplier;
    public float HealthMultiplier = 1f;
    public float EnemySpawnBonusMultiplier;
    public bool ForceBossSpawn;

    public DifficultyCustomSettings Clone()
    {
        return (DifficultyCustomSettings)MemberwiseClone();
    }

    public DifficultyCustomSettings CloneAndClamp()
    {
        var clone = Clone();
        clone.PatrolTurnSpeed = Mathf.Clamp(clone.PatrolTurnSpeed, 100f, 600f);
        clone.CombatTurnSpeed = Mathf.Clamp(clone.CombatTurnSpeed, 800f, 3500f);
        clone.BaseReactionTime = Mathf.Clamp(clone.BaseReactionTime, 0.01f, 0.35f);
        clone.ScatterMultiIfTargetRunning = Mathf.Clamp(clone.ScatterMultiIfTargetRunning, 0f, 5f);
        clone.ScatterMultiIfOffScreen = Mathf.Clamp(clone.ScatterMultiIfOffScreen, 0f, 5f);
        clone.NightReactionTimeFactor = Mathf.Clamp(clone.NightReactionTimeFactor, 0.5f, 4f);
        clone.HearingAbility = Mathf.Clamp(clone.HearingAbility, 0.5f, 4f);
        clone.TraceTargetChance = Mathf.Clamp(clone.TraceTargetChance, 0f, 4f);
        clone.ShootDelayMultiplier = Mathf.Clamp(clone.ShootDelayMultiplier, -0.3f, 0.6f);
        clone.ShootTimeMultiplier = Mathf.Clamp(clone.ShootTimeMultiplier, -0.3f, 0.8f);
        clone.ShootIntervalMultiplier = Mathf.Clamp(clone.ShootIntervalMultiplier, -0.5f, 0.6f);
        clone.CombatMoveTimeMultiplier = Mathf.Clamp(clone.CombatMoveTimeMultiplier, -0.3f, 0.8f);
        clone.SightAngleMultiplier = Mathf.Clamp(clone.SightAngleMultiplier, -0.25f, 0.7f);
        clone.SightDistanceMultiplier = Mathf.Clamp(clone.SightDistanceMultiplier, -0.25f, 0.8f);
        clone.DashCoolTimeMultiplier = Mathf.Clamp(clone.DashCoolTimeMultiplier, -0.6f, 0.9f);
        clone.MoveSpeedFactor = Mathf.Clamp(clone.MoveSpeedFactor, -0.5f, 0.8f);
        clone.BulletSpeedMultiplier = Mathf.Clamp(clone.BulletSpeedMultiplier, -0.5f, 1.2f);
        clone.GunDistanceMultiplier = Mathf.Clamp(clone.GunDistanceMultiplier, -0.5f, 1.2f);
        clone.DamageMultiplier = Mathf.Clamp(clone.DamageMultiplier, -0.5f, 1.2f);
        clone.HealthMultiplier = Mathf.Clamp(clone.HealthMultiplier <= 0f ? 1f : clone.HealthMultiplier, 0.1f, DifficultyManager.MaxHealthMultiplier);
        clone.EnemySpawnBonusMultiplier = Mathf.Clamp(clone.EnemySpawnBonusMultiplier, 0f, 6f);
        return clone;
    }

    public DifficultySettings ToSettings()
    {
        return new DifficultySettings(
            PatrolTurnSpeed,
            CombatTurnSpeed,
            BaseReactionTime,
            ScatterMultiIfTargetRunning,
            ScatterMultiIfOffScreen,
            NightReactionTimeFactor,
            HearingAbility,
            TraceTargetChance,
            ShootDelayMultiplier,
            ShootTimeMultiplier,
            ShootIntervalMultiplier,
            CombatMoveTimeMultiplier,
            SightAngleMultiplier,
            SightDistanceMultiplier,
            CanDash,
            DashCoolTimeMultiplier,
            MoveSpeedFactor,
            BulletSpeedMultiplier,
            GunDistanceMultiplier,
            DamageMultiplier,
            HealthMultiplier,
            EnemySpawnBonusMultiplier,
            ForceBossSpawn);
    }

    public static DifficultyCustomSettings FromSettings(DifficultySettings settings)
    {
        return new DifficultyCustomSettings
        {
            PatrolTurnSpeed = settings.PatrolTurnSpeed,
            CombatTurnSpeed = settings.CombatTurnSpeed,
            BaseReactionTime = settings.BaseReactionTime,
            ScatterMultiIfTargetRunning = settings.ScatterMultiIfTargetRunning,
            ScatterMultiIfOffScreen = settings.ScatterMultiIfOffScreen,
            NightReactionTimeFactor = settings.NightReactionTimeFactor,
            HearingAbility = settings.HearingAbility,
            TraceTargetChance = settings.TraceTargetChance,
            ShootDelayMultiplier = settings.ShootDelayMultiplier,
            ShootTimeMultiplier = settings.ShootTimeMultiplier,
            ShootIntervalMultiplier = settings.ShootIntervalMultiplier,
            CombatMoveTimeMultiplier = settings.CombatMoveTimeMultiplier,
            SightAngleMultiplier = settings.SightAngleMultiplier,
            SightDistanceMultiplier = settings.SightDistanceMultiplier,
            CanDash = settings.CanDash,
            DashCoolTimeMultiplier = settings.DashCoolTimeMultiplier,
            MoveSpeedFactor = settings.MoveSpeedFactor,
            BulletSpeedMultiplier = settings.BulletSpeedMultiplier,
            GunDistanceMultiplier = settings.GunDistanceMultiplier,
            DamageMultiplier = settings.DamageMultiplier,
            HealthMultiplier = settings.HealthMultiplier,
            EnemySpawnBonusMultiplier = settings.EnemySpawnBonusMultiplier,
            ForceBossSpawn = settings.ForceBossSpawn
        };
    }
}



public static class DifficultyManager
{
    public const float MaxHealthMultiplier = 2.5f;
    private static float _baseEnemySpawnFactor = float.NaN;

    private static readonly Dictionary<DifficultyLevel, DifficultySettings> PresetSettings = new()
    {
        // 参数顺序：
        // 巡逻转速, 战斗转速, 基础反应, 目标在跑散布, 屏幕外散布,
        // 夜晚反应倍率, 听觉倍率, 声音锁定概率,
        // 开火前延迟倍率, 连射持续倍率, 射击间隔倍率, 战斗移动时间倍率,
        // 视角倍率, 视距倍率, canDash, 冲刺冷却倍率,
        // 移动速度倍率, 子弹速度倍率, 射程倍率, 伤害倍率, 血量倍率,
        // 敌人生成数量附加倍率, 是否强制生成Boss

        {
            DifficultyLevel.Easy,
            new DifficultySettings(
                200f, 1000f, 0.25f, 4f, 4f,
                1f,   1f,    1f,
                0f, 0f,  0f, 0f,
                0f, 0f,
                false, 0f,
                0f, 0f, 0f, 0f,
                1f,
                0f,
                false)
        },
        {
            DifficultyLevel.Normal,
            new DifficultySettings(
                270f, 1400f, 0.15f, 3f, 3f,
                1.15f, 1.15f,  1.15f,
                0.1f, 0.15f,  0.1f, 0.1f,
                0.1f, 0.1f,
                true,  0.2f,
                0.15f, 0.25f, 0.1f, 0.15f,
                1.25f,
                1f,
                false)
        },
        {
            DifficultyLevel.Hard,
            new DifficultySettings(
                350f, 1950f, 0.09f, 2f, 2f,
                1.25f,   1.25f,    1.25f,
                0.15f,0.25f,  0.15f, 0.15f,
                0.15f, 0.15f,
                true,  0.9f,
                0.25f, 0.3f, 0.15f, 0.25f,
                1.35f,
                1.5f,
                false)
        },
        {
            DifficultyLevel.VeryHard,
            new DifficultySettings(
                410f, 2300f, 0.05f, 1.5f, 1.5f,
                1.35f, 1.35f,  1.35f,
                0.25f, 0.33f,  0.25f, 0.25f,
                0.25f, 0.25f,
                true,  0.8f,
                0.4f, 0.35f, 0.25f, 0.35f,
                1.5f,
                2f,
                false)
        },
        {
            DifficultyLevel.Impossible,
            new DifficultySettings(
                500f, 3000f, 0.02f, 1f, 1f,
                1.5f,   1.5f,   1.5f,
                0.35f, 0.43f,  0.35f, 0.35f,
                0.35f, 0.35f,
                true,  0.7f,
                0.5f, 0.48f,  0.35f, 0.43f,
                1.85f,
                3f,
                true)
        }
    };

    private static DifficultyCustomSettings _customSettings = DifficultyCustomSettings.FromSettings(
        new DifficultySettings(
            270f, 1400f, 0.15f, 3f, 3f,
            1.5f, 1.5f,  1.5f,
            0.1f, 0.15f,  0.1f, 0.1f,
            0.1f, 0.1f,
            true,  0.2f,
            0.15f, 0.25f, 0.1f, 0.15f,
            1f,
            0f,
            false));

    public static DifficultySettings Get(DifficultyLevel level) =>
        level == DifficultyLevel.Custom ? _customSettings.ToSettings() : PresetSettings[level];
    private static readonly Dictionary<DifficultyLevel, Sprite> Sprites = new();

    public static DifficultyLevel Selected { get; private set; } = DifficultyLevel.Normal;

    public static void SetDifficulty(DifficultyLevel level)
    {
        Selected = level;
        ApplyLevelWideSettings(CurrentSettings);
    }

    public static DifficultySettings CurrentSettings => Get(Selected);

    public static DifficultyCustomSettings GetCustomSettings() => _customSettings.Clone();

    public static void SetCustomSettings(DifficultyCustomSettings next)
    {
        _customSettings = next?.CloneAndClamp() ?? _customSettings;
        ApplyLevelWideSettings(CurrentSettings);
    }

    public static Sprite GetDifficultySprite(DifficultyLevel level)
    {
        if (level == DifficultyLevel.Custom)
            return null;

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
            DifficultyLevel.Custom => CoopLocalization.Get("ui.difficulty.custom"),
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

        ApplyHealthMultiplier(ai);
    }

    private static float GetExtraPlayerIncrement(DifficultyLevel level)
    {
        return level switch
        {
            DifficultyLevel.Easy => 0f,
            DifficultyLevel.Normal => 0.4f,
            DifficultyLevel.Hard => 0.5f,
            DifficultyLevel.VeryHard => 0.6f,
            DifficultyLevel.Impossible => 0.75f,
            _ => 0f
        };
    }

    private static int GetAdditionalPlayerCount()
    {
        var svc = NetService.Instance;
        if (svc == null || !svc.IsServer)
            return 0;

        try
        {
            return Mathf.Max(0, svc.playerStatuses?.Count ?? 0);
        }
        catch
        {
            return 0;
        }
    }

    public static float GetHealthMultiplier(int extraPlayers)
    {
        var baseMultiplier = Mathf.Clamp(CurrentSettings.HealthMultiplier <= 0f ? 1f : CurrentSettings.HealthMultiplier, 0.1f, MaxHealthMultiplier);
        var perPlayer = GetExtraPlayerIncrement(Selected);
        var withPlayers = baseMultiplier + Mathf.Max(0, extraPlayers) * perPlayer;
        return Mathf.Min(withPlayers, MaxHealthMultiplier);
    }

    private static void ApplyLevelWideSettings(DifficultySettings settings)
    {
        var svc = NetService.Instance;
        if (svc != null && !svc.IsServer)
            return;

        var lm = LevelManager.Instance;
        if (lm == null)
            return;

        try
        {
            var type = lm.GetType();
            var factorField = AccessTools.Field(type, "enemySpawnCountFactor");
            var factorProp = factorField == null ? AccessTools.Property(type, "enemySpawnCountFactor") : null;

            if (float.IsNaN(_baseEnemySpawnFactor))
            {
                if (factorField != null)
                    _baseEnemySpawnFactor = Convert.ToSingle(factorField.GetValue(lm));
                else if (factorProp != null)
                    _baseEnemySpawnFactor = Convert.ToSingle(factorProp.GetValue(lm));
                else
                    _baseEnemySpawnFactor = 1f;
            }

            if (factorField != null)
                factorField.SetValue(lm, _baseEnemySpawnFactor + settings.EnemySpawnBonusMultiplier);
            else if (factorProp != null)
                factorProp.SetValue(lm, _baseEnemySpawnFactor + settings.EnemySpawnBonusMultiplier);

            var bossField = AccessTools.Field(type, "forceBossSpawn");
            var bossProp = bossField == null ? AccessTools.Property(type, "forceBossSpawn") : null;
            if (bossField != null)
                bossField.SetValue(lm, settings.ForceBossSpawn);
            else
                bossProp?.SetValue(lm, settings.ForceBossSpawn);
        }
        catch
        {
        }
    }

    private static void ApplyHealthMultiplier(AICharacterController ai)
    {
        var cmc = ai.CharacterMainControl;
        var health = cmc != null ? cmc.Health : null;
        if (health == null)
            return;

        var multiplier = GetHealthMultiplier(GetAdditionalPlayerCount());
        if (multiplier <= 0f)
            return;

        float baseMax;
        try
        {
            baseMax = Mathf.Max(1f, health.MaxHealth);
        }
        catch
        {
            baseMax = 1f;
        }

        var targetMax = Mathf.Clamp(baseMax * multiplier, 1f, baseMax * MaxHealthMultiplier);
        float current;
        try
        {
            current = health.CurrentHealth;
        }
        catch
        {
            current = targetMax;
        }

        var scaledCurrent = baseMax > 0f ? Mathf.Clamp(current * (targetMax / baseMax), 0f, targetMax) : targetMax;
        HealthM.Instance?.ForceSetHealth(health, targetMax, scaledCurrent, true);
    }
}
