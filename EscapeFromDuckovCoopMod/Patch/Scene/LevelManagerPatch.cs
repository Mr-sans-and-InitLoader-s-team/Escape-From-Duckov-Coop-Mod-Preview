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

using Duckov.UI;
using Duckov.Utilities;
using UnityEngine.EventSystems;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(LevelManager), "StartInit")]
internal static class Patch_Level_StartInit_Gate
{
    private static bool Prefix(LevelManager __instance, SceneLoadingContext context)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null) return true;
        
        // 在场景初始化前清理战利品缓存
        ClearLootCacheOnSceneChange();
        
        if (mod.IsServer) return true;

        var needGate = SceneNet.Instance.sceneVoteActive || (mod.networkStarted && !mod.IsServer);
        if (!needGate) return true;

        RunAsync(__instance, context).Forget();
        return false;
    }

    private static async UniTaskVoid RunAsync(LevelManager self, SceneLoadingContext ctx)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null) return;

        await SceneNet.Instance.Client_SceneGateAsync();

        try
        {
            var m = AccessTools.Method(typeof(LevelManager), "InitLevel", new[] { typeof(SceneLoadingContext) });
            if (m != null) m.Invoke(self, new object[] { ctx });
        }
        catch (Exception e)
        {
            Debug.LogError("[SCENE] StartInit gate -> InitLevel failed: " + e);
        }
    }
    
    /// <summary>
    /// 场景切换时清理战利品缓存，特别是出基地时
    /// </summary>
    private static void ClearLootCacheOnSceneChange()
    {
        try
        {
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var sceneName = currentScene.name;
            
            Debug.Log($"[LOOT] Scene changing to: {sceneName}");
            
            // 检查是否是出基地场景（Base_SceneV2）
            bool isLeavingBase = sceneName.Contains("Base_SceneV2") || sceneName.StartsWith("Level_");
            
            if (isLeavingBase)
            {
                Debug.Log($"[LOOT] Detected leaving base or entering new level, clearing loot cache");
                
                var lootManager = LootManager.Instance;
                if (lootManager != null)
                {
                    // 清理客户端战利品缓存
                    var clearedCount = lootManager._cliLootByUid.Count;
                    lootManager._cliLootByUid.Clear();
                    
                    // 清理待处理的重排序请求
                    lootManager._cliPendingReorder.Clear();
                    
                    // 清理待处理的拾取请求
                    lootManager._cliPendingTake.Clear();
                    
                    // 清理待处理的战利品状态
                    lootManager._pendingLootStatesByUid.Clear();
                    
                    Debug.Log($"[LOOT] Cleared {clearedCount} loot cache entries for scene change");
                }
                
                // 清理InteractableLootbox的静态字典
                try
                {
                    var inventoriesDict = InteractableLootbox.Inventories;
                    if (inventoriesDict != null)
                    {
                        var dictCount = inventoriesDict.Count;
                        inventoriesDict.Clear();
                        Debug.Log($"[LOOT] Cleared {dictCount} InteractableLootbox.Inventories entries");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LOOT] Failed to clear InteractableLootbox.Inventories: {e.Message}");
                }
                
                // 清理LevelManager的LootBoxInventories
                try
                {
                    var levelManager = LevelManager.Instance;
                    if (levelManager != null)
                    {
                        var lootBoxInventories = LevelManager.LootBoxInventories;
                        if (lootBoxInventories != null)
                        {
                            var levelDictCount = lootBoxInventories.Count;
                            lootBoxInventories.Clear();
                            Debug.Log($"[LOOT] Cleared {levelDictCount} LevelManager.LootBoxInventories entries");
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LOOT] Failed to clear LevelManager.LootBoxInventories: {e.Message}");
                }
            }
            else
            {
                Debug.Log($"[LOOT] Scene {sceneName} does not require loot cache clearing");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[LOOT] Failed to clear loot cache on scene change: {e}");
        }
    }
}

[HarmonyPatch(typeof(MapSelectionEntry), "OnPointerClick")]
internal static class Patch_Mapen_OnPointerClick
{
    private static bool Prefix(MapSelectionEntry __instance, PointerEventData eventData)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;
        if (!mod.IsServer) return false;
        
        // 服务端在开始场景投票前清理战利品缓存
        ClearServerLootCacheOnSceneVote(__instance.SceneID);
        
        SceneNet.Instance.IsMapSelectionEntry = true;
        SceneNet.Instance.Host_BeginSceneVote_Simple(__instance.SceneID, "", false, false, false, "OnPointerClick");
        return false;
    }
    
    /// <summary>
    /// 服务端在场景投票开始时清理战利品缓存
    /// </summary>
    private static void ClearServerLootCacheOnSceneVote(string targetSceneId)
    {
        try
        {
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var currentSceneName = currentScene.name;
            
            Debug.Log($"[LOOT] Server scene vote: from {currentSceneName} to {targetSceneId}");
            
            // 检查是否是离开基地或进入新关卡
            bool isLeavingBase = currentSceneName.Contains("Base_SceneV2") || targetSceneId.StartsWith("Level_");
            
            if (isLeavingBase)
            {
                Debug.Log($"[LOOT] Server detected leaving base or entering new level, clearing loot cache");
                
                var lootManager = LootManager.Instance;
                if (lootManager != null)
                {
                    // 清理服务端战利品缓存（但保留墓碑数据）
                    var clearedCount = 0;
                    var keysToRemove = new List<int>();
                    
                    foreach (var kv in lootManager._srvLootByUid)
                    {
                        var lootUid = kv.Key;
                        var inventory = kv.Value;
                        
                        // 检查是否是墓碑（通过检查是否有对应的游戏对象）
                        bool isTombstone = false;
                        try
                        {
                            var lootbox = LootboxDetectUtil.TryGetInventoryLootBox(inventory);
                            if (lootbox != null)
                            {
                                // 检查是否是墓碑类型的战利品箱
                                var loader = lootbox.GetComponent<LootBoxLoader>();
                                isTombstone = (loader == null); // 墓碑通常没有LootBoxLoader组件
                            }
                        }
                        catch
                        {
                            // 如果检查失败，保守起见不清理
                            isTombstone = true;
                        }
                        
                        // 只清理非墓碑的战利品缓存
                        if (!isTombstone)
                        {
                            keysToRemove.Add(lootUid);
                            clearedCount++;
                        }
                    }
                    
                    foreach (var key in keysToRemove)
                    {
                        lootManager._srvLootByUid.Remove(key);
                    }
                    
                    // 清理服务端战利品静音表
                    lootManager._srvLootMuteUntil.Clear();
                    
                    Debug.Log($"[LOOT] Server cleared {clearedCount} non-tombstone loot cache entries, preserved tombstones");
                }
            }
            else
            {
                Debug.Log($"[LOOT] Server scene vote from {currentSceneName} to {targetSceneId} does not require loot cache clearing");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[LOOT] Failed to clear server loot cache on scene vote: {e}");
        }
    }
}