using HarmonyLib;
using ItemStatsSystem;
using Saves;
using System;
using System.Diagnostics;
using UnityEngine;

namespace EscapeFromDuckovCoopMod
{
    /// <summary>
    /// ✅ 性能监控：跟踪角色死亡流程，定位耗时操作
    /// </summary>
    [HarmonyPatch(typeof(LevelManager), "OnMainCharacterDie")]
    internal static class Patch_LevelManager_OnMainCharacterDie
    {
        private static void Prefix()
        {
            UnityEngine.Debug.Log($"[Death-Monitor] [{DateTime.Now:HH:mm:ss.fff}] ========== 角色死亡流程开始 ==========");
        }

        private static void Postfix()
        {
            UnityEngine.Debug.Log($"[Death-Monitor] [{DateTime.Now:HH:mm:ss.fff}] OnMainCharacterDie 完成");
        }
    }

    /// <summary>
    /// ✅ 监控 SaveFile 操作耗时
    /// </summary>
    [HarmonyPatch(typeof(SavesSystem), "SaveFile")]
    internal static class Patch_SavesSystem_SaveFile
    {
        private static Stopwatch _stopwatch;

        private static void Prefix(bool writeSaveTime)
        {
            _stopwatch = Stopwatch.StartNew();
            // UnityEngine.Debug.Log($"[Death-Monitor] [{DateTime.Now:HH:mm:ss.fff}] SaveFile 开始 (writeSaveTime={writeSaveTime})");
        }

        private static void Postfix()
        {
            _stopwatch?.Stop();
            // UnityEngine.Debug.Log($"[Death-Monitor] [{DateTime.Now:HH:mm:ss.fff}] SaveFile 完成，耗时: {_stopwatch?.ElapsedMilliseconds}ms");
        }
    }

    /// <summary>
    /// ✅ 监控 CreateFromItem（墓碑创建）操作耗时
    /// </summary>
    [HarmonyPatch(typeof(InteractableLootbox), nameof(InteractableLootbox.CreateFromItem))]
    internal static class Patch_InteractableLootbox_CreateFromItem
    {
        private static Stopwatch _stopwatch;

        private static void Prefix(Item item)
        {
            _stopwatch = Stopwatch.StartNew();
            UnityEngine.Debug.Log($"[Death-Monitor] [{DateTime.Now:HH:mm:ss.fff}] CreateFromItem 开始 (物品类型: {item?.GetType().Name})");
        }

        private static void Postfix(InteractableLootbox __result)
        {
            _stopwatch?.Stop();
            UnityEngine.Debug.Log($"[Death-Monitor] [{DateTime.Now:HH:mm:ss.fff}] CreateFromItem 完成，耗时: {_stopwatch?.ElapsedMilliseconds}ms");
        }
    }
}

