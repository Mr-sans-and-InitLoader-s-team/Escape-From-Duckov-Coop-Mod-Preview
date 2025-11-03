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
using UnityEngine;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod.Utils;

/// <summary>
/// 场景触发器重置工具，用于重置 OnTriggerEnterEvent 组件的触发状态
/// 解决场景切换触发器只能触发一次的问题
/// </summary>
public static class SceneTriggerResetter
{
    private static System.Type _triggerEventType;
    private static FieldInfo _triggeredField;

    private static System.Type _teleporterType;
    private static FieldInfo _teleportFinishedTimeField;

    private static System.Type _interactableBaseType;
    private static FieldInfo _lastStopTimeField;
    private static FieldInfo _interactColliderField;
    private static FieldInfo _markerObjectField;
    private static System.Type _interactMarkerType;

    static SceneTriggerResetter()
    {
        // 从所有已加载的程序集中查找相关类型
        foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            if (_triggerEventType == null)
            {
                _triggerEventType = assembly.GetType("OnTriggerEnterEvent");
                if (_triggerEventType != null)
                {
                    Debug.Log($"[SceneTriggerResetter] 在程序集 {assembly.GetName().Name} 中找到 OnTriggerEnterEvent 类型");
                }
            }

            if (_teleporterType == null)
            {
                _teleporterType = assembly.GetType("Duckov.Scenes.MultiSceneTeleporter");
                if (_teleporterType != null)
                {
                    Debug.Log($"[SceneTriggerResetter] 在程序集 {assembly.GetName().Name} 中找到 MultiSceneTeleporter 类型");
                }
            }

            if (_interactableBaseType == null)
            {
                _interactableBaseType = assembly.GetType("InteractableBase");
                if (_interactableBaseType != null)
                {
                    Debug.Log($"[SceneTriggerResetter] 在程序集 {assembly.GetName().Name} 中找到 InteractableBase 类型");
                }
            }

            if (_triggerEventType != null && _teleporterType != null && _interactableBaseType != null)
            {
                break;
            }
        }

        if (_triggerEventType != null)
        {
            _triggeredField = _triggerEventType.GetField("triggered",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (_triggeredField == null)
            {
                Debug.LogError("[SceneTriggerResetter] 无法找到 OnTriggerEnterEvent.triggered 字段");
            }
        }
        else
        {
            Debug.LogError("[SceneTriggerResetter] 无法在任何程序集中找到 OnTriggerEnterEvent 类型");
        }

        if (_teleporterType != null)
        {
            _teleportFinishedTimeField = _teleporterType.GetField("timeWhenTeleportFinished",
                BindingFlags.Static | BindingFlags.NonPublic);

            if (_teleportFinishedTimeField == null)
            {
                Debug.LogWarning("[SceneTriggerResetter] 无法找到 MultiSceneTeleporter.timeWhenTeleportFinished 字段");
            }
        }

        if (_interactableBaseType != null)
        {
            _lastStopTimeField = _interactableBaseType.GetField("lastStopTime",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (_lastStopTimeField == null)
            {
                Debug.LogWarning("[SceneTriggerResetter] 无法找到 InteractableBase.lastStopTime 字段");
            }

            _interactColliderField = _interactableBaseType.GetField("interactCollider",
                BindingFlags.Instance | BindingFlags.Public);

            if (_interactColliderField == null)
            {
                Debug.LogWarning("[SceneTriggerResetter] 无法找到 InteractableBase.interactCollider 字段");
            }

            _markerObjectField = _interactableBaseType.GetField("markerObject",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (_markerObjectField == null)
            {
                Debug.LogWarning("[SceneTriggerResetter] 无法找到 InteractableBase.markerObject 字段");
            }
        }

        if (_triggeredField != null || _teleportFinishedTimeField != null || _lastStopTimeField != null)
        {
            Debug.Log("[SceneTriggerResetter] 成功初始化触发器重置工具");
        }
    }

    /// <summary>
    /// 重置当前场景中所有 OnTriggerEnterEvent 组件的触发状态
    /// </summary>
    public static void ResetAllSceneTriggers()
    {
        int totalResetCount = 0;

        // 1. 重置 OnTriggerEnterEvent 组件
        if (_triggerEventType != null && _triggeredField != null)
        {
            var triggerEvents = Object.FindObjectsOfType(_triggerEventType);

            if (triggerEvents != null && triggerEvents.Length > 0)
            {
                int resetCount = 0;
                foreach (var triggerEvent in triggerEvents)
                {
                    try
                    {
                        // 将 triggered 字段设置为 false
                        _triggeredField.SetValue(triggerEvent, false);
                        resetCount++;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[SceneTriggerResetter] 重置触发器失败: {ex.Message}");
                    }
                }

                totalResetCount += resetCount;
                Debug.Log($"[SceneTriggerResetter] 成功重置 {resetCount} 个 OnTriggerEnterEvent 触发器");
            }
        }

        // 2. 重置 MultiSceneTeleporter 的静态时间字段（清除传送冷却）
        if (_teleportFinishedTimeField != null)
        {
            try
            {
                // 将 timeWhenTeleportFinished 设置为负数，确保 Teleportable 立即为 true
                _teleportFinishedTimeField.SetValue(null, -999f);
                Debug.Log("[SceneTriggerResetter] 成功重置 MultiSceneTeleporter 传送冷却时间");
                totalResetCount++;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SceneTriggerResetter] 重置传送冷却失败: {ex.Message}");
            }
        }

        // 3. 重置所有 InteractableBase 的交互状态（关键修复！）
        if (_interactableBaseType != null && _lastStopTimeField != null)
        {
            var interactables = Object.FindObjectsOfType(_interactableBaseType);

            if (interactables != null && interactables.Length > 0)
            {
                int resetCount = 0;
                foreach (var interactable in interactables)
                {
                    try
                    {
                        var monoBehaviour = interactable as MonoBehaviour;
                        if (monoBehaviour == null) continue;

                        // 重新启用组件（如果被 disableOnFinish 禁用了）
                        if (!monoBehaviour.enabled)
                        {
                            monoBehaviour.enabled = true;
                        }

                        // 将 lastStopTime 设置为 -1f，清除交互冷却
                        if (_lastStopTimeField != null)
                        {
                            _lastStopTimeField.SetValue(interactable, -1f);
                        }

                        // 重新启用碰撞体
                        if (_interactColliderField != null)
                        {
                            var collider = _interactColliderField.GetValue(interactable) as Collider;
                            if (collider != null && !collider.enabled)
                            {
                                collider.enabled = true;
                            }
                        }

                        // 重新显示交互标记（UI提示）
                        if (_markerObjectField != null)
                        {
                            var marker = _markerObjectField.GetValue(interactable);
                            if (marker != null)
                            {
                                var markerGameObject = (marker as Component)?.gameObject;
                                if (markerGameObject != null && !markerGameObject.activeSelf)
                                {
                                    markerGameObject.SetActive(true);
                                }
                            }
                        }

                        resetCount++;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[SceneTriggerResetter] 重置交互状态失败: {ex.Message}");
                    }
                }

                totalResetCount += resetCount;
                Debug.Log($"[SceneTriggerResetter] 成功重置 {resetCount} 个 InteractableBase 交互状态");
            }
        }

        if (totalResetCount == 0)
        {
            Debug.LogWarning("[SceneTriggerResetter] 没有重置任何触发器或传送点");
        }
        else
        {
            Debug.Log($"[SceneTriggerResetter] 总共完成 {totalResetCount} 项重置操作");
        }
    }

    /// <summary>
    /// 重置特定游戏对象上的触发器状态
    /// </summary>
    /// <param name="triggerObject">包含 OnTriggerEnterEvent 组件的游戏对象</param>
    public static void ResetTrigger(GameObject triggerObject)
    {
        if (_triggerEventType == null || _triggeredField == null)
        {
            Debug.LogWarning("[SceneTriggerResetter] 触发器类型或字段未初始化，无法重置触发器");
            return;
        }

        if (triggerObject == null)
        {
            Debug.LogWarning("[SceneTriggerResetter] 触发器对象为空");
            return;
        }

        var triggerEvent = triggerObject.GetComponent(_triggerEventType);
        if (triggerEvent == null)
        {
            Debug.LogWarning($"[SceneTriggerResetter] 对象 {triggerObject.name} 上没有 OnTriggerEnterEvent 组件");
            return;
        }

        try
        {
            _triggeredField.SetValue(triggerEvent, false);
            Debug.Log($"[SceneTriggerResetter] 成功重置触发器: {triggerObject.name}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[SceneTriggerResetter] 重置触发器失败: {ex.Message}");
        }
    }
}

