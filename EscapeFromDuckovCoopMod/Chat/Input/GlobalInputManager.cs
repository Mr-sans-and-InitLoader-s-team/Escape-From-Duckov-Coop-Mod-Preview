using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace EscapeFromDuckovCoopMod.Chat.Input
{
    /// <summary>
    /// 全局输入管理器 - 单例模式
    /// </summary>
    public class GlobalInputManager : MonoBehaviour
    {
        [Header("输入管理设置")]
        [SerializeField] private bool blockGameInputWhenChatActive = true;
        [SerializeField] private int chatInputPriority = 1000;
        [SerializeField] private bool enableDebugLog = false;

        /// <summary>
        /// 输入模式改变事件
        /// </summary>
        public event Action<InputMode> OnInputModeChanged;

        /// <summary>
        /// 输入阻止状态改变事件
        /// </summary>
        public event Action<bool> OnInputBlockStateChanged;

        private readonly Dictionary<int, IInputHandler> inputHandlers = new Dictionary<int, IInputHandler>();
        private readonly List<int> priorityOrder = new List<int>();
        private InputMode currentInputMode = InputMode.Game;
        private bool isInputBlocked = false;
        private IInputHandler activeHandler;

        /// <summary>
        /// 单例实例
        /// </summary>
        public static GlobalInputManager Instance { get; private set; }

        /// <summary>
        /// 当前输入模式
        /// </summary>
        public InputMode CurrentInputMode => currentInputMode;

        /// <summary>
        /// 输入是否被阻止
        /// </summary>
        public bool IsInputBlocked => isInputBlocked;

        /// <summary>
        /// 当前活动的输入处理器
        /// </summary>
        public IInputHandler ActiveHandler => activeHandler;

        /// <summary>
        /// Awake时设置单例
        /// </summary>
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                Initialize();
            }
            else if (Instance != this)
            {
                Debug.LogWarning("检测到重复的GlobalInputManager实例，销毁当前实例");
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 初始化输入管理器
        /// </summary>
        private void Initialize()
        {
            LogDebug("全局输入管理器初始化完成");
        }

        /// <summary>
        /// 注册输入处理器
        /// </summary>
        /// <param name="handler">输入处理器</param>
        /// <param name="priority">优先级（数值越大优先级越高）</param>
        public void RegisterInputHandler(IInputHandler handler, int priority)
        {
            if (handler == null)
            {
                Debug.LogWarning("尝试注册空的输入处理器");
                return;
            }

            // 移除已存在的处理器
            UnregisterInputHandler(handler);

            inputHandlers[priority] = handler;
            
            // 更新优先级排序
            if (!priorityOrder.Contains(priority))
            {
                priorityOrder.Add(priority);
                priorityOrder.Sort((a, b) => b.CompareTo(a)); // 降序排列
            }

            LogDebug($"注册输入处理器: {handler.GetType().Name}，优先级: {priority}");
        }

        /// <summary>
        /// 注销输入处理器
        /// </summary>
        /// <param name="handler">输入处理器</param>
        public void UnregisterInputHandler(IInputHandler handler)
        {
            if (handler == null)
                return;

            // 查找并移除处理器
            int priorityToRemove = -1;
            foreach (var kvp in inputHandlers)
            {
                if (kvp.Value == handler)
                {
                    priorityToRemove = kvp.Key;
                    break;
                }
            }

            if (priorityToRemove != -1)
            {
                inputHandlers.Remove(priorityToRemove);
                priorityOrder.Remove(priorityToRemove);

                if (activeHandler == handler)
                {
                    activeHandler = null;
                    UpdateActiveHandler();
                }

                LogDebug($"注销输入处理器: {handler.GetType().Name}");
            }
        }

        /// <summary>
        /// 设置输入模式
        /// </summary>
        /// <param name="mode">输入模式</param>
        public void SetInputMode(InputMode mode)
        {
            if (currentInputMode == mode)
                return;

            var previousMode = currentInputMode;
            currentInputMode = mode;

            // 更新输入阻止状态
            UpdateInputBlockState();

            // 更新活动处理器
            UpdateActiveHandler();

            OnInputModeChanged?.Invoke(mode);
            LogDebug($"输入模式改变: {previousMode} -> {mode}");
        }

        /// <summary>
        /// 强制设置输入阻止状态
        /// </summary>
        /// <param name="blocked">是否阻止</param>
        public void SetInputBlocked(bool blocked)
        {
            if (isInputBlocked == blocked)
                return;

            isInputBlocked = blocked;
            OnInputBlockStateChanged?.Invoke(blocked);
            LogDebug($"输入阻止状态: {blocked}");
        }

        /// <summary>
        /// 检查是否应该阻止游戏输入
        /// </summary>
        /// <returns>是否应该阻止</returns>
        public bool ShouldBlockGameInput()
        {
            return blockGameInputWhenChatActive && 
                   (currentInputMode == InputMode.Chat || isInputBlocked);
        }

        /// <summary>
        /// 处理输入事件
        /// </summary>
        /// <param name="inputEvent">输入事件</param>
        /// <returns>是否被处理</returns>
        public bool HandleInputEvent(InputEvent inputEvent)
        {
            if (inputEvent == null)
                return false;

            // 按优先级顺序处理输入
            foreach (var priority in priorityOrder)
            {
                if (inputHandlers.TryGetValue(priority, out var handler))
                {
                    if (handler.CanHandleInput() && handler.HandleInput(inputEvent))
                    {
                        LogDebug($"输入事件被处理: {handler.GetType().Name}");
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 获取最高优先级的可用处理器
        /// </summary>
        /// <returns>输入处理器</returns>
        public IInputHandler GetHighestPriorityHandler()
        {
            foreach (var priority in priorityOrder)
            {
                if (inputHandlers.TryGetValue(priority, out var handler) && handler.CanHandleInput())
                {
                    return handler;
                }
            }

            return null;
        }

        /// <summary>
        /// 检查指定优先级的处理器是否存在
        /// </summary>
        /// <param name="priority">优先级</param>
        /// <returns>是否存在</returns>
        public bool HasHandlerAtPriority(int priority)
        {
            return inputHandlers.ContainsKey(priority);
        }

        /// <summary>
        /// 获取所有注册的处理器
        /// </summary>
        /// <returns>处理器列表</returns>
        public IInputHandler[] GetAllHandlers()
        {
            var handlers = new List<IInputHandler>();
            foreach (var priority in priorityOrder)
            {
                if (inputHandlers.TryGetValue(priority, out var handler))
                {
                    handlers.Add(handler);
                }
            }
            return handlers.ToArray();
        }

        /// <summary>
        /// 清除所有输入处理器
        /// </summary>
        public void ClearAllHandlers()
        {
            inputHandlers.Clear();
            priorityOrder.Clear();
            activeHandler = null;
            LogDebug("所有输入处理器已清除");
        }

        /// <summary>
        /// Update中处理输入检测
        /// </summary>
        private void Update()
        {
            // 检测全局按键
            HandleGlobalInput();

            // 更新活动处理器状态
            UpdateActiveHandlerStatus();
        }

        /// <summary>
        /// 处理全局输入
        /// </summary>
        private void HandleGlobalInput()
        {
            // 检测ESC键
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                var escapeEvent = new InputEvent
                {
                    Type = InputEventType.KeyDown,
                    KeyCode = KeyCode.Escape,
                    Timestamp = Time.time
                };
                HandleInputEvent(escapeEvent);
            }

            // 检测Enter键
            if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                var enterEvent = new InputEvent
                {
                    Type = InputEventType.KeyDown,
                    KeyCode = KeyCode.Return,
                    Timestamp = Time.time
                };
                HandleInputEvent(enterEvent);
            }
        }

        /// <summary>
        /// 更新输入阻止状态
        /// </summary>
        private void UpdateInputBlockState()
        {
            bool shouldBlock = ShouldBlockGameInput();
            if (isInputBlocked != shouldBlock)
            {
                SetInputBlocked(shouldBlock);
            }
        }

        /// <summary>
        /// 更新活动处理器
        /// </summary>
        private void UpdateActiveHandler()
        {
            var newActiveHandler = GetHighestPriorityHandler();
            if (activeHandler != newActiveHandler)
            {
                if (activeHandler != null)
                {
                    activeHandler.OnDeactivated();
                }

                activeHandler = newActiveHandler;

                if (activeHandler != null)
                {
                    activeHandler.OnActivated();
                }

                LogDebug($"活动处理器更新: {activeHandler?.GetType().Name ?? "None"}");
            }
        }

        /// <summary>
        /// 更新活动处理器状态
        /// </summary>
        private void UpdateActiveHandlerStatus()
        {
            if (activeHandler != null)
            {
                activeHandler.Update();
            }
        }

        /// <summary>
        /// 调试日志输出
        /// </summary>
        /// <param name="message">日志消息</param>
        private void LogDebug(string message)
        {
            if (enableDebugLog)
            {
                Debug.Log($"[GlobalInputManager] {message}");
            }
        }

        /// <summary>
        /// 组件销毁时清理
        /// </summary>
        private void OnDestroy()
        {
            ClearAllHandlers();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 静态方法：创建GlobalInputManager实例
        /// </summary>
        /// <returns>GlobalInputManager实例</returns>
        public static GlobalInputManager CreateInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var go = new GameObject("GlobalInputManager");
            var manager = go.AddComponent<GlobalInputManager>();
            return manager;
        }

        /// <summary>
        /// 静态方法：获取或创建实例
        /// </summary>
        /// <returns>GlobalInputManager实例</returns>
        public static GlobalInputManager GetOrCreateInstance()
        {
            if (Instance == null)
            {
                return CreateInstance();
            }
            return Instance;
        }
    }

    /// <summary>
    /// 输入模式枚举
    /// </summary>
    public enum InputMode
    {
        /// <summary>
        /// 游戏输入模式
        /// </summary>
        Game = 0,

        /// <summary>
        /// 聊天输入模式
        /// </summary>
        Chat = 1,

        /// <summary>
        /// UI输入模式
        /// </summary>
        UI = 2,

        /// <summary>
        /// 暂停模式
        /// </summary>
        Paused = 3
    }

    /// <summary>
    /// 输入处理器接口
    /// </summary>
    public interface IInputHandler
    {
        /// <summary>
        /// 检查是否可以处理输入
        /// </summary>
        /// <returns>是否可以处理</returns>
        bool CanHandleInput();

        /// <summary>
        /// 处理输入事件
        /// </summary>
        /// <param name="inputEvent">输入事件</param>
        /// <returns>是否处理成功</returns>
        bool HandleInput(InputEvent inputEvent);

        /// <summary>
        /// 处理器被激活时调用
        /// </summary>
        void OnActivated();

        /// <summary>
        /// 处理器被停用时调用
        /// </summary>
        void OnDeactivated();

        /// <summary>
        /// 每帧更新
        /// </summary>
        void Update();
    }

    /// <summary>
    /// 输入事件类
    /// </summary>
    public class InputEvent
    {
        /// <summary>
        /// 事件类型
        /// </summary>
        public InputEventType Type { get; set; }

        /// <summary>
        /// 按键码
        /// </summary>
        public KeyCode KeyCode { get; set; }

        /// <summary>
        /// 鼠标按钮
        /// </summary>
        public int MouseButton { get; set; }

        /// <summary>
        /// 鼠标位置
        /// </summary>
        public Vector2 MousePosition { get; set; }

        /// <summary>
        /// 滚轮增量
        /// </summary>
        public float ScrollDelta { get; set; }

        /// <summary>
        /// 输入字符
        /// </summary>
        public char InputChar { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public float Timestamp { get; set; }

        /// <summary>
        /// 是否被处理
        /// </summary>
        public bool IsHandled { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public InputEvent()
        {
            Timestamp = Time.time;
        }
    }

    /// <summary>
    /// 输入事件类型枚举
    /// </summary>
    public enum InputEventType
    {
        /// <summary>
        /// 按键按下
        /// </summary>
        KeyDown,

        /// <summary>
        /// 按键抬起
        /// </summary>
        KeyUp,

        /// <summary>
        /// 鼠标按下
        /// </summary>
        MouseDown,

        /// <summary>
        /// 鼠标抬起
        /// </summary>
        MouseUp,

        /// <summary>
        /// 鼠标移动
        /// </summary>
        MouseMove,

        /// <summary>
        /// 鼠标滚轮
        /// </summary>
        MouseScroll,

        /// <summary>
        /// 字符输入
        /// </summary>
        CharInput
    }
}