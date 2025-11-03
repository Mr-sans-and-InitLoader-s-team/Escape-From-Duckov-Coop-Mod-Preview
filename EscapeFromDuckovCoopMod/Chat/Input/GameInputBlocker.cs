using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;

namespace EscapeFromDuckovCoopMod.Chat.Input
{
    /// <summary>
    /// 游戏输入阻止器 - 使用Harmony补丁拦截游戏输入
    /// </summary>
    public class GameInputBlocker : MonoBehaviour
    {
        [Header("输入阻止设置")]
        [SerializeField] private bool enableInputBlocking = true;
        [SerializeField] private bool blockKeyboardInput = true;
        [SerializeField] private bool blockMouseInput = false;
        [SerializeField] private bool enableDebugLog = false;

        /// <summary>
        /// 输入阻止状态改变事件
        /// </summary>
        public event Action<bool> OnInputBlockStateChanged;

        private static GameInputBlocker instance;
        private static bool isInputBlocked = false;
        private static readonly HashSet<KeyCode> blockedKeys = new HashSet<KeyCode>();
        private static readonly HashSet<int> blockedMouseButtons = new HashSet<int>();

        private Harmony harmonyInstance;
        private bool isPatchesApplied = false;

        /// <summary>
        /// 单例实例
        /// </summary>
        public static GameInputBlocker Instance => instance;

        /// <summary>
        /// 输入是否被阻止
        /// </summary>
        public static bool IsInputBlocked => isInputBlocked;

        /// <summary>
        /// Awake时设置单例
        /// </summary>
        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                Initialize();
            }
            else if (instance != this)
            {
                Debug.LogWarning("检测到重复的GameInputBlocker实例，销毁当前实例");
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 初始化输入阻止器
        /// </summary>
        private void Initialize()
        {
            try
            {
                // 创建Harmony实例
                harmonyInstance = new Harmony("EscapeFromDuckovCoopMod.Chat.InputBlocker");

                // 应用补丁
                ApplyPatches();

                LogDebug("游戏输入阻止器初始化完成");
            }
            catch (Exception ex)
            {
                Debug.LogError($"初始化游戏输入阻止器失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 应用Harmony补丁
        /// </summary>
        private void ApplyPatches()
        {
            if (isPatchesApplied)
                return;

            try
            {
                // 补丁Unity Input类的方法
                PatchUnityInputMethods();

                isPatchesApplied = true;
                LogDebug("Harmony补丁应用成功");
            }
            catch (Exception ex)
            {
                Debug.LogError($"应用Harmony补丁失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 补丁Unity Input方法
        /// </summary>
        private void PatchUnityInputMethods()
        {
            // 补丁Input.GetKey方法
            var getKeyMethod = typeof(UnityEngine.Input).GetMethod("GetKey", new[] { typeof(KeyCode) });
            if (getKeyMethod != null)
            {
                var getKeyPrefix = typeof(InputPatches).GetMethod("GetKeyPrefix");
                harmonyInstance.Patch(getKeyMethod, new HarmonyMethod(getKeyPrefix));
            }

            // 补丁Input.GetKeyDown方法
            var getKeyDownMethod = typeof(UnityEngine.Input).GetMethod("GetKeyDown", new[] { typeof(KeyCode) });
            if (getKeyDownMethod != null)
            {
                var getKeyDownPrefix = typeof(InputPatches).GetMethod("GetKeyDownPrefix");
                harmonyInstance.Patch(getKeyDownMethod, new HarmonyMethod(getKeyDownPrefix));
            }

            // 补丁Input.GetKeyUp方法
            var getKeyUpMethod = typeof(UnityEngine.Input).GetMethod("GetKeyUp", new[] { typeof(KeyCode) });
            if (getKeyUpMethod != null)
            {
                var getKeyUpPrefix = typeof(InputPatches).GetMethod("GetKeyUpPrefix");
                harmonyInstance.Patch(getKeyUpMethod, new HarmonyMethod(getKeyUpPrefix));
            }

            // 补丁Input.GetMouseButton方法
            var getMouseButtonMethod = typeof(UnityEngine.Input).GetMethod("GetMouseButton", new[] { typeof(int) });
            if (getMouseButtonMethod != null)
            {
                var getMouseButtonPrefix = typeof(InputPatches).GetMethod("GetMouseButtonPrefix");
                harmonyInstance.Patch(getMouseButtonMethod, new HarmonyMethod(getMouseButtonPrefix));
            }

            // 补丁Input.GetMouseButtonDown方法
            var getMouseButtonDownMethod = typeof(UnityEngine.Input).GetMethod("GetMouseButtonDown", new[] { typeof(int) });
            if (getMouseButtonDownMethod != null)
            {
                var getMouseButtonDownPrefix = typeof(InputPatches).GetMethod("GetMouseButtonDownPrefix");
                harmonyInstance.Patch(getMouseButtonDownMethod, new HarmonyMethod(getMouseButtonDownPrefix));
            }

            // 补丁Input.GetMouseButtonUp方法
            var getMouseButtonUpMethod = typeof(UnityEngine.Input).GetMethod("GetMouseButtonUp", new[] { typeof(int) });
            if (getMouseButtonUpMethod != null)
            {
                var getMouseButtonUpPrefix = typeof(InputPatches).GetMethod("GetMouseButtonUpPrefix");
                harmonyInstance.Patch(getMouseButtonUpMethod, new HarmonyMethod(getMouseButtonUpPrefix));
            }
        }

        /// <summary>
        /// 移除Harmony补丁
        /// </summary>
        private void RemovePatches()
        {
            if (!isPatchesApplied || harmonyInstance == null)
                return;

            try
            {
                harmonyInstance.UnpatchAll("EscapeFromDuckovCoopMod.Chat.InputBlocker");
                isPatchesApplied = false;
                LogDebug("Harmony补丁已移除");
            }
            catch (Exception ex)
            {
                Debug.LogError($"移除Harmony补丁失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置输入阻止状态
        /// </summary>
        /// <param name="blocked">是否阻止输入</param>
        public void SetInputBlocked(bool blocked)
        {
            if (isInputBlocked == blocked)
                return;

            isInputBlocked = blocked;
            OnInputBlockStateChanged?.Invoke(blocked);
            LogDebug($"输入阻止状态: {blocked}");
        }

        /// <summary>
        /// 添加要阻止的按键
        /// </summary>
        /// <param name="keyCode">按键码</param>
        public void AddBlockedKey(KeyCode keyCode)
        {
            blockedKeys.Add(keyCode);
            LogDebug($"添加阻止按键: {keyCode}");
        }

        /// <summary>
        /// 移除要阻止的按键
        /// </summary>
        /// <param name="keyCode">按键码</param>
        public void RemoveBlockedKey(KeyCode keyCode)
        {
            blockedKeys.Remove(keyCode);
            LogDebug($"移除阻止按键: {keyCode}");
        }

        /// <summary>
        /// 清空所有阻止的按键
        /// </summary>
        public void ClearBlockedKeys()
        {
            blockedKeys.Clear();
            LogDebug("清空所有阻止按键");
        }

        /// <summary>
        /// 添加要阻止的鼠标按钮
        /// </summary>
        /// <param name="mouseButton">鼠标按钮</param>
        public void AddBlockedMouseButton(int mouseButton)
        {
            blockedMouseButtons.Add(mouseButton);
            LogDebug($"添加阻止鼠标按钮: {mouseButton}");
        }

        /// <summary>
        /// 移除要阻止的鼠标按钮
        /// </summary>
        /// <param name="mouseButton">鼠标按钮</param>
        public void RemoveBlockedMouseButton(int mouseButton)
        {
            blockedMouseButtons.Remove(mouseButton);
            LogDebug($"移除阻止鼠标按钮: {mouseButton}");
        }

        /// <summary>
        /// 清空所有阻止的鼠标按钮
        /// </summary>
        public void ClearBlockedMouseButtons()
        {
            blockedMouseButtons.Clear();
            LogDebug("清空所有阻止鼠标按钮");
        }

        /// <summary>
        /// 检查按键是否应该被阻止
        /// </summary>
        /// <param name="keyCode">按键码</param>
        /// <returns>是否应该被阻止</returns>
        public static bool ShouldBlockKey(KeyCode keyCode)
        {
            if (!isInputBlocked || instance == null || !instance.enableInputBlocking)
                return false;

            // 如果禁用键盘输入阻止，返回false
            if (!instance.blockKeyboardInput)
                return false;

            // 检查是否在特定阻止列表中
            if (blockedKeys.Count > 0)
            {
                return blockedKeys.Contains(keyCode);
            }

            // 默认阻止所有键盘输入（除了一些特殊键）
            return !IsSpecialKey(keyCode);
        }

        /// <summary>
        /// 检查鼠标按钮是否应该被阻止
        /// </summary>
        /// <param name="mouseButton">鼠标按钮</param>
        /// <returns>是否应该被阻止</returns>
        public static bool ShouldBlockMouseButton(int mouseButton)
        {
            if (!isInputBlocked || instance == null || !instance.enableInputBlocking)
                return false;

            // 如果禁用鼠标输入阻止，返回false
            if (!instance.blockMouseInput)
                return false;

            // 检查是否在特定阻止列表中
            if (blockedMouseButtons.Count > 0)
            {
                return blockedMouseButtons.Contains(mouseButton);
            }

            // 默认不阻止鼠标输入
            return false;
        }

        /// <summary>
        /// 检查是否为特殊按键（不应该被阻止）
        /// </summary>
        /// <param name="keyCode">按键码</param>
        /// <returns>是否为特殊按键</returns>
        private static bool IsSpecialKey(KeyCode keyCode)
        {
            // 这些按键通常不应该被阻止
            switch (keyCode)
            {
                case KeyCode.Escape:
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Tab:
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                case KeyCode.LeftWindows:
                case KeyCode.RightWindows:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 启用输入阻止
        /// </summary>
        public void EnableInputBlocking()
        {
            enableInputBlocking = true;
            LogDebug("输入阻止已启用");
        }

        /// <summary>
        /// 禁用输入阻止
        /// </summary>
        public void DisableInputBlocking()
        {
            enableInputBlocking = false;
            SetInputBlocked(false);
            LogDebug("输入阻止已禁用");
        }

        /// <summary>
        /// 调试日志输出
        /// </summary>
        /// <param name="message">日志消息</param>
        private void LogDebug(string message)
        {
            if (enableDebugLog)
            {
                Debug.Log($"[GameInputBlocker] {message}");
            }
        }

        /// <summary>
        /// 组件销毁时清理
        /// </summary>
        private void OnDestroy()
        {
            RemovePatches();

            if (instance == this)
            {
                instance = null;
                isInputBlocked = false;
            }
        }

        /// <summary>
        /// 静态方法：创建GameInputBlocker实例
        /// </summary>
        /// <returns>GameInputBlocker实例</returns>
        public static GameInputBlocker CreateInstance()
        {
            if (instance != null)
            {
                return instance;
            }

            var go = new GameObject("GameInputBlocker");
            var blocker = go.AddComponent<GameInputBlocker>();
            return blocker;
        }

        /// <summary>
        /// 静态方法：获取或创建实例
        /// </summary>
        /// <returns>GameInputBlocker实例</returns>
        public static GameInputBlocker GetOrCreateInstance()
        {
            if (instance == null)
            {
                return CreateInstance();
            }
            return instance;
        }
    }

    /// <summary>
    /// Harmony输入补丁类
    /// </summary>
    public static class InputPatches
    {
        /// <summary>
        /// GetKey方法前缀补丁
        /// </summary>
        /// <param name="keycode">按键码</param>
        /// <param name="__result">返回结果</param>
        /// <returns>是否跳过原方法</returns>
        public static bool GetKeyPrefix(KeyCode keycode, ref bool __result)
        {
            if (GameInputBlocker.ShouldBlockKey(keycode))
            {
                __result = false;
                return false; // 跳过原方法
            }
            return true; // 执行原方法
        }

        /// <summary>
        /// GetKeyDown方法前缀补丁
        /// </summary>
        /// <param name="keycode">按键码</param>
        /// <param name="__result">返回结果</param>
        /// <returns>是否跳过原方法</returns>
        public static bool GetKeyDownPrefix(KeyCode keycode, ref bool __result)
        {
            if (GameInputBlocker.ShouldBlockKey(keycode))
            {
                __result = false;
                return false; // 跳过原方法
            }
            return true; // 执行原方法
        }

        /// <summary>
        /// GetKeyUp方法前缀补丁
        /// </summary>
        /// <param name="keycode">按键码</param>
        /// <param name="__result">返回结果</param>
        /// <returns>是否跳过原方法</returns>
        public static bool GetKeyUpPrefix(KeyCode keycode, ref bool __result)
        {
            if (GameInputBlocker.ShouldBlockKey(keycode))
            {
                __result = false;
                return false; // 跳过原方法
            }
            return true; // 执行原方法
        }

        /// <summary>
        /// GetMouseButton方法前缀补丁
        /// </summary>
        /// <param name="button">鼠标按钮</param>
        /// <param name="__result">返回结果</param>
        /// <returns>是否跳过原方法</returns>
        public static bool GetMouseButtonPrefix(int button, ref bool __result)
        {
            if (GameInputBlocker.ShouldBlockMouseButton(button))
            {
                __result = false;
                return false; // 跳过原方法
            }
            return true; // 执行原方法
        }

        /// <summary>
        /// GetMouseButtonDown方法前缀补丁
        /// </summary>
        /// <param name="button">鼠标按钮</param>
        /// <param name="__result">返回结果</param>
        /// <returns>是否跳过原方法</returns>
        public static bool GetMouseButtonDownPrefix(int button, ref bool __result)
        {
            if (GameInputBlocker.ShouldBlockMouseButton(button))
            {
                __result = false;
                return false; // 跳过原方法
            }
            return true; // 执行原方法
        }

        /// <summary>
        /// GetMouseButtonUp方法前缀补丁
        /// </summary>
        /// <param name="button">鼠标按钮</param>
        /// <param name="__result">返回结果</param>
        /// <returns>是否跳过原方法</returns>
        public static bool GetMouseButtonUpPrefix(int button, ref bool __result)
        {
            if (GameInputBlocker.ShouldBlockMouseButton(button))
            {
                __result = false;
                return false; // 跳过原方法
            }
            return true; // 执行原方法
        }
    }
}