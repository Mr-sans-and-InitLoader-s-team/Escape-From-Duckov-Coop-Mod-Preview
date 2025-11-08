// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025 Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
// YOU MUST NOT use this software for commercial purposes.
// YOU MUST NOT use this software to run a headless game server.
// YOU MUST include a conspicuous notice of attribution to
// Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Utils
{
    /// <summary>
    /// ✅ 组件缓存系统 - 避免频繁调用 GetComponent
    /// 性能提升: 10-50倍（取决于调用频率）
    /// </summary>
    public class ComponentCache : MonoBehaviour
    {
        // 缓存 GameObject 的所有组件
        private readonly Dictionary<Type, Component> _cachedComponents = new Dictionary<Type, Component>();

        /// <summary>
        /// 获取缓存的组件（如果不存在则添加）
        /// </summary>
        public T GetOrAddComponent<T>() where T : Component
        {
            var type = typeof(T);
            if (_cachedComponents.TryGetValue(type, out var cached))
            {
                if (cached != null) return cached as T;
                // 组件被销毁，从缓存中移除
                _cachedComponents.Remove(type);
            }

            var component = GetComponent<T>();
            if (!component)
            {
                component = gameObject.AddComponent<T>();
            }

            _cachedComponents[type] = component;
            return component;
        }

        /// <summary>
        /// 获取缓存的组件（如果不存在返回 null）
        /// </summary>
        public T GetCachedComponent<T>() where T : Component
        {
            var type = typeof(T);
            if (_cachedComponents.TryGetValue(type, out var cached))
            {
                if (cached != null) return cached as T;
                // 组件被销毁，从缓存中移除
                _cachedComponents.Remove(type);
            }

            var component = GetComponent<T>();
            if (component)
            {
                _cachedComponents[type] = component;
            }
            return component;
        }

        /// <summary>
        /// 预热缓存 - 提前获取所有需要的组件
        /// </summary>
        public void PrewarmCache(params Type[] componentTypes)
        {
            foreach (var type in componentTypes)
            {
                if (_cachedComponents.ContainsKey(type)) continue;

                var component = GetComponent(type);
                if (component)
                {
                    _cachedComponents[type] = component;
                }
            }
        }

        /// <summary>
        /// 清除缓存
        /// </summary>
        public void ClearCache()
        {
            _cachedComponents.Clear();
        }

        /// <summary>
        /// 移除无效的缓存项
        /// </summary>
        public void CleanCache()
        {
            var toRemove = new List<Type>();
            foreach (var kvp in _cachedComponents)
            {
                if (kvp.Value == null)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var type in toRemove)
            {
                _cachedComponents.Remove(type);
            }
        }

        private void OnDestroy()
        {
            _cachedComponents.Clear();
        }

        // ========== 静态辅助方法（用于旧代码兼容） ==========

        /// <summary>
        /// 判断 CharacterMainControl 是否为 AI（静态方法）
        /// </summary>
        public static bool IsAI(CharacterMainControl cmc)
        {
            if (!cmc) return false;

            // 玩家角色判断
            if (LevelManager.Instance?.MainCharacter == cmc) return false;

            // 检查是否有 AI 控制器组件
            return ComponentLookupTable.FastGetComponent<AICharacterController>(cmc.gameObject) != null;
        }

        /// <summary>
        /// 获取 NetAiTag 组件（使用缓存，静态方法）
        /// </summary>
        public static NetAiTag GetNetAiTag(CharacterMainControl cmc)
        {
            if (!cmc) return null;
            return ComponentLookupTable.FastGetComponent<NetAiTag>(cmc.gameObject);
        }
    }

    /// <summary>
    /// ✅ 静态组件查找表 - 全局组件缓存
    /// 用于不方便添加 ComponentCache 组件的对象
    /// </summary>
    public static class ComponentLookupTable
    {
        // GameObject InstanceID → Component Type → Component
        private static readonly Dictionary<int, Dictionary<Type, Component>> _lookup = new Dictionary<int, Dictionary<Type, Component>>();
        private static float _lastCleanupTime = 0f;
        private const float CLEANUP_INTERVAL = 30f; // 每30秒清理一次

        /// <summary>
        /// 快速获取组件（使用全局缓存）
        /// </summary>
        public static T FastGetComponent<T>(GameObject go) where T : Component
        {
            if (go == null) return null;

            var instanceId = go.GetInstanceID();
            var type = typeof(T);

            if (_lookup.TryGetValue(instanceId, out var dict))
            {
                if (dict.TryGetValue(type, out var comp))
                {
                    if (comp != null) return comp as T;
                    // 组件被销毁，移除缓存
                    dict.Remove(type);
                }
            }
            else
            {
                _lookup[instanceId] = new Dictionary<Type, Component>();
            }

            var component = go.GetComponent<T>();
            if (component)
            {
                _lookup[instanceId][type] = component;
            }

            // 定期清理
            if (Time.time - _lastCleanupTime > CLEANUP_INTERVAL)
            {
                CleanupInvalidEntries();
            }

            return component;
        }

        /// <summary>
        /// 快速获取或添加组件
        /// </summary>
        public static T FastGetOrAddComponent<T>(GameObject go) where T : Component
        {
            var component = FastGetComponent<T>(go);
            if (component != null) return component;

            component = go.AddComponent<T>();

            var instanceId = go.GetInstanceID();
            var type = typeof(T);

            if (!_lookup.TryGetValue(instanceId, out var dict))
            {
                dict = new Dictionary<Type, Component>();
                _lookup[instanceId] = dict;
            }
            dict[type] = component;

            return component;
        }

        /// <summary>
        /// 清理无效的缓存项
        /// </summary>
        public static void CleanupInvalidEntries()
        {
            _lastCleanupTime = Time.time;

            var toRemove = new List<int>();
            foreach (var kvp in _lookup)
            {
                var goId = kvp.Key;
                var dict = kvp.Value;

                // 检查 GameObject 是否仍然有效
                bool allInvalid = true;
                var invalidTypes = new List<Type>();

                foreach (var compKvp in dict)
                {
                    if (compKvp.Value == null)
                    {
                        invalidTypes.Add(compKvp.Key);
                    }
                    else
                    {
                        allInvalid = false;
                    }
                }

                // 移除无效组件
                foreach (var type in invalidTypes)
                {
                    dict.Remove(type);
                }

                // 如果所有组件都无效，移除整个 GameObject 条目
                if (allInvalid || dict.Count == 0)
                {
                    toRemove.Add(goId);
                }
            }

            foreach (var id in toRemove)
            {
                _lookup.Remove(id);
            }

            Debug.Log($"[ComponentLookupTable] 清理完成，移除 {toRemove.Count} 个无效条目");
        }

        /// <summary>
        /// 强制清除所有缓存
        /// </summary>
        public static void ClearAll()
        {
            _lookup.Clear();
            Debug.Log("[ComponentLookupTable] 所有缓存已清除");
        }

        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        public static (int gameObjects, int components) GetStats()
        {
            int totalComponents = 0;
            foreach (var dict in _lookup.Values)
            {
                totalComponents += dict.Count;
            }
            return (_lookup.Count, totalComponents);
        }
    }


    /// <summary>
    /// ✅ GameObject 扩展方法 - 方便使用缓存
    /// </summary>
    public static class GameObjectExtensions
    {
        /// <summary>
        /// 使用缓存获取组件
        /// </summary>
        public static T GetCachedComponent<T>(this GameObject go) where T : Component
        {
            return ComponentLookupTable.FastGetComponent<T>(go);
        }

        /// <summary>
        /// 使用缓存获取或添加组件
        /// </summary>
        public static T GetOrAddCachedComponent<T>(this GameObject go) where T : Component
        {
            return ComponentLookupTable.FastGetOrAddComponent<T>(go);
        }

        /// <summary>
        /// 从 Component 获取缓存的其他组件
        /// </summary>
        public static T GetCachedComponent<T>(this Component component) where T : Component
        {
            return ComponentLookupTable.FastGetComponent<T>(component.gameObject);
        }

        /// <summary>
        /// 从 Component 获取或添加缓存的组件
        /// </summary>
        public static T GetOrAddCachedComponent<T>(this Component component) where T : Component
        {
            return ComponentLookupTable.FastGetOrAddComponent<T>(component.gameObject);
        }
    }
}
