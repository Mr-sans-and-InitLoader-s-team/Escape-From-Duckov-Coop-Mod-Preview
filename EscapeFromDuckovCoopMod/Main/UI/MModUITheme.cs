using System;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public enum UIThemeMode
{
    White = 0,
    Black = 1,
    Spring = 2,
    Summer = 3
}

internal static class MModUITheme
{
    internal static UIThemeMode CurrentMode { get; private set; } = UIThemeMode.Black;

    internal static UIThemeMode NormalizeMode(UIThemeMode mode)
    {
        if (!Enum.IsDefined(typeof(UIThemeMode), mode))
            return UIThemeMode.Black;

        if (mode == UIThemeMode.White)
            return UIThemeMode.Black;

        return mode;
    }

    internal static bool SetThemeMode(UIThemeMode mode)
    {
        mode = NormalizeMode(mode);

        if (CurrentMode == mode)
            return false;

        CurrentMode = mode;
        return true;
    }

    internal static bool UseLunarNewYearTheme => CurrentMode == UIThemeMode.Spring;
    internal static bool IsDarkTheme => CurrentMode == UIThemeMode.Black;
    internal static bool IsSummerTheme => CurrentMode == UIThemeMode.Summer;
    internal static bool ShouldShowNewYearDecorations => UseLunarNewYearTheme;

    internal static Color Primary => UseLunarNewYearTheme
        ? new Color(0.80f, 0.13f, 0.18f, 1f)
        : IsSummerTheme
            ? new Color(0.06f, 0.58f, 0.90f, 1f)
        : IsDarkTheme
            ? new Color(0.47f, 0.64f, 0.94f, 1f)
            : new Color(0.31f, 0.52f, 0.84f, 1f);

    internal static Color PrimaryHover => UseLunarNewYearTheme
        ? new Color(0.88f, 0.18f, 0.24f, 1f)
        : IsSummerTheme
            ? new Color(0.12f, 0.72f, 1f, 1f)
        : IsDarkTheme
            ? new Color(0.56f, 0.72f, 1f, 1f)
            : new Color(0.41f, 0.62f, 0.92f, 1f);

    internal static Color PrimaryActive => UseLunarNewYearTheme
        ? new Color(0.68f, 0.09f, 0.14f, 1f)
        : IsSummerTheme
            ? new Color(0.02f, 0.42f, 0.76f, 1f)
        : IsDarkTheme
            ? new Color(0.34f, 0.50f, 0.82f, 1f)
            : new Color(0.22f, 0.40f, 0.70f, 1f);

    internal static Color PrimaryText => new(1f, 1f, 1f, 0.96f);
    internal static Color BgDark => UseLunarNewYearTheme ? new Color(0.23f, 0.23f, 0.23f, 1f) : IsSummerTheme ? new Color(0.70f, 0.90f, 0.98f, 1f) : IsDarkTheme ? new Color(0.05f, 0.06f, 0.08f, 1f) : new Color(0.92f, 0.95f, 0.98f, 1f);
    internal static Color BgMedium => UseLunarNewYearTheme ? new Color(0.27f, 0.27f, 0.27f, 1f) : IsSummerTheme ? new Color(0.82f, 0.95f, 1f, 1f) : IsDarkTheme ? new Color(0.08f, 0.10f, 0.13f, 1f) : new Color(0.96f, 0.975f, 0.99f, 1f);
    internal static Color BgLight => UseLunarNewYearTheme ? new Color(0.32f, 0.32f, 0.32f, 1f) : IsSummerTheme ? new Color(1f, 1f, 1f, 1f) : IsDarkTheme ? new Color(0.12f, 0.14f, 0.18f, 1f) : new Color(1f, 1f, 1f, 1f);
    internal static Color TextPrimary => UseLunarNewYearTheme || IsDarkTheme ? new Color(1f, 1f, 1f, 0.96f) : IsSummerTheme ? new Color(0.02f, 0.18f, 0.32f, 0.96f) : new Color(0.10f, 0.12f, 0.16f, 0.96f);
    internal static Color TextSecondary => UseLunarNewYearTheme ? new Color(1f, 1f, 1f, 0.84f) : IsSummerTheme ? new Color(0.07f, 0.30f, 0.46f, 0.88f) : IsDarkTheme ? new Color(0.80f, 0.85f, 0.92f, 0.86f) : new Color(0.30f, 0.35f, 0.43f, 0.88f);
    internal static Color TextTertiary => UseLunarNewYearTheme ? new Color(1f, 1f, 1f, 0.66f) : IsSummerTheme ? new Color(0.12f, 0.42f, 0.58f, 0.78f) : IsDarkTheme ? new Color(0.62f, 0.68f, 0.76f, 0.76f) : new Color(0.47f, 0.53f, 0.62f, 0.78f);
    internal static Color Success => UseLunarNewYearTheme ? new Color(0.45f, 0.75f, 0.50f, 1f) : IsSummerTheme ? new Color(0.13f, 0.70f, 0.58f, 1f) : IsDarkTheme ? new Color(0.32f, 0.78f, 0.58f, 1f) : new Color(0.20f, 0.63f, 0.43f, 1f);
    internal static Color Warning => UseLunarNewYearTheme ? new Color(0.90f, 0.75f, 0.35f, 1f) : IsSummerTheme ? new Color(1f, 0.79f, 0.18f, 1f) : IsDarkTheme ? new Color(0.95f, 0.70f, 0.34f, 1f) : new Color(0.84f, 0.58f, 0.20f, 1f);
    internal static Color Error => UseLunarNewYearTheme ? new Color(0.85f, 0.45f, 0.40f, 1f) : IsSummerTheme ? new Color(0.93f, 0.36f, 0.32f, 1f) : IsDarkTheme ? new Color(0.94f, 0.45f, 0.50f, 1f) : new Color(0.82f, 0.30f, 0.34f, 1f);
    internal static Color Info => UseLunarNewYearTheme ? new Color(0.55f, 0.65f, 0.80f, 1f) : IsSummerTheme ? new Color(0.02f, 0.56f, 0.92f, 1f) : IsDarkTheme ? new Color(0.50f, 0.70f, 0.96f, 1f) : new Color(0.36f, 0.54f, 0.78f, 1f);
    internal static Color InputBg => UseLunarNewYearTheme ? new Color(0.33f, 0.33f, 0.33f, 1f) : IsSummerTheme ? new Color(1f, 1f, 1f, 0.86f) : IsDarkTheme ? new Color(0.12f, 0.15f, 0.20f, 0.86f) : new Color(1f, 1f, 1f, 0.78f);
    internal static Color InputBorder => UseLunarNewYearTheme ? new Color(0.42f, 0.42f, 0.42f, 1f) : IsSummerTheme ? new Color(0.20f, 0.67f, 0.96f, 0.44f) : IsDarkTheme ? new Color(0.45f, 0.55f, 0.70f, 0.40f) : new Color(0.74f, 0.79f, 0.86f, 0.62f);
    internal static Color Divider => UseLunarNewYearTheme ? new Color(1f, 1f, 1f, 0.24f) : IsSummerTheme ? new Color(0.06f, 0.48f, 0.76f, 0.18f) : IsDarkTheme ? new Color(1f, 1f, 1f, 0.12f) : new Color(0.43f, 0.49f, 0.58f, 0.16f);
    internal static Color GlassBg => UseLunarNewYearTheme ? new Color(0.80f, 0.13f, 0.18f, 0.75f) : IsSummerTheme ? new Color(0.68f, 0.90f, 1f, 0.70f) : IsDarkTheme ? new Color(0.06f, 0.08f, 0.11f, 0.72f) : new Color(1f, 1f, 1f, 0.54f);
    internal static Color Shadow => new(0f, 0f, 0f, UseLunarNewYearTheme ? 0.25f : IsDarkTheme ? 0.35f : IsSummerTheme ? 0.16f : 0.12f);

    internal static Color PanelBg => UseLunarNewYearTheme ? new Color(0.80f, 0.13f, 0.18f, 0.96f) : IsSummerTheme ? new Color(0.62f, 0.87f, 0.98f, 0.92f) : IsDarkTheme ? new Color(0.07f, 0.09f, 0.12f, 0.86f) : new Color(1f, 1f, 1f, 0.62f);
    internal static Color CardBg => UseLunarNewYearTheme ? new Color(0.73f, 0.10f, 0.15f, 0.95f) : IsSummerTheme ? new Color(1f, 1f, 1f, 0.72f) : IsDarkTheme ? new Color(0.10f, 0.12f, 0.16f, 0.78f) : new Color(1f, 1f, 1f, 0.56f);
    internal static Color ButtonBg => UseLunarNewYearTheme ? new Color(0.96f, 0.82f, 0.18f, 0.98f) : IsSummerTheme ? new Color(1f, 0.86f, 0.24f, 0.96f) : IsDarkTheme ? new Color(0.14f, 0.17f, 0.23f, 0.86f) : new Color(1f, 1f, 1f, 0.72f);
    internal static Color ButtonHover => UseLunarNewYearTheme ? new Color(1f, 0.88f, 0.30f, 1f) : IsSummerTheme ? new Color(1f, 0.92f, 0.42f, 1f) : IsDarkTheme ? new Color(0.20f, 0.25f, 0.33f, 0.95f) : new Color(0.88f, 0.93f, 1f, 0.90f);
    internal static Color ButtonActive => UseLunarNewYearTheme ? new Color(0.88f, 0.72f, 0.10f, 1f) : IsSummerTheme ? new Color(0.95f, 0.70f, 0.10f, 1f) : IsDarkTheme ? new Color(0.27f, 0.34f, 0.46f, 0.98f) : new Color(0.78f, 0.86f, 0.96f, 0.96f);
    internal static Color ButtonText => UseLunarNewYearTheme ? new Color(0.28f, 0.16f, 0.02f, 0.98f) : IsSummerTheme ? new Color(0.02f, 0.18f, 0.32f, 0.98f) : IsDarkTheme ? new Color(1f, 1f, 1f, 0.96f) : new Color(1f, 1f, 1f, 0.96f);
    internal static Color GlassInputBg => UseLunarNewYearTheme ? new Color(0.66f, 0.08f, 0.13f, 0.96f) : IsSummerTheme ? new Color(1f, 1f, 1f, 0.78f) : IsDarkTheme ? new Color(0.08f, 0.10f, 0.14f, 0.78f) : new Color(1f, 1f, 1f, 0.68f);
    internal static Color Accent => UseLunarNewYearTheme ? new Color(0.80f, 0.13f, 0.18f, 1f) : IsSummerTheme ? new Color(1f, 0.78f, 0.16f, 1f) : IsDarkTheme ? new Color(0.42f, 0.58f, 0.84f, 1f) : new Color(0.52f, 0.68f, 0.90f, 1f);
    internal static Color GlassText => UseLunarNewYearTheme || IsDarkTheme ? new Color(1f, 1f, 1f, 0.96f) : IsSummerTheme ? new Color(0.02f, 0.18f, 0.32f, 0.96f) : new Color(0.10f, 0.12f, 0.16f, 0.96f);
    internal static Color GlassTextSecondary => UseLunarNewYearTheme ? new Color(1f, 1f, 1f, 0.82f) : IsSummerTheme ? new Color(0.07f, 0.30f, 0.46f, 0.84f) : IsDarkTheme ? new Color(0.78f, 0.84f, 0.92f, 0.84f) : new Color(0.30f, 0.35f, 0.43f, 0.86f);
    internal static Color GlassDivider => UseLunarNewYearTheme ? new Color(1f, 1f, 1f, 0.14f) : IsSummerTheme ? new Color(0.06f, 0.48f, 0.76f, 0.16f) : IsDarkTheme ? new Color(1f, 1f, 1f, 0.10f) : new Color(0.36f, 0.42f, 0.50f, 0.14f);
    internal static Color ViewportBg => UseLunarNewYearTheme ? new Color(0.70f, 0.09f, 0.14f, 0.98f) : IsSummerTheme ? new Color(0.78f, 0.93f, 1f, 0.44f) : IsDarkTheme ? new Color(0.05f, 0.07f, 0.10f, 0.50f) : new Color(1f, 1f, 1f, 0.32f);
}
