#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS || UNITY_STANDALONE || UNITY_WEBGL || UNITY_SERVER
#define UNITY_SUPPORT
#endif

using EscapeFromDuckovCoopMod.Utils.Logger.Core;
using EscapeFromDuckovCoopMod.Utils.Logger.Logs;
using ILogHandler = EscapeFromDuckovCoopMod.Utils.Logger.Core.ILogHandler;

namespace EscapeFromDuckovCoopMod.Utils.Logger.Tools
{
    public static class AutoExceptionCaptureExtension
    {
        // 订阅者采用弱引用的 object，支持 ILogHandler<Log> 与 ILogHandler 两种接口
        private static volatile WeakReference<object>[] _subscribersSnapshot = Array.Empty<WeakReference<object>>();

        private static readonly object _subscribersSync = new object();

        /// <summary>
        /// 启用或禁用自动捕获未处理异常功能
        /// </summary>
        /// <param name="handler"></param>
        /// <param name="enabled"></param>
        public static void EnableAutoCaptureException(this ILogHandler<Log> handler, bool enabled) => EnableInternal(handler, enabled);

        /// <summary>
        /// 启用或禁用自动捕获未处理异常功能
        /// </summary>
        /// <param name="handler"></param>
        /// <param name="enabled"></param>
        public static void EnableAutoCaptureException(this ILogHandler handler, bool enabled) => EnableInternal(handler, enabled);

        private static void EnableInternal(object handler, bool enabled)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            // 仅支持引用类型订阅者，值类型会装箱，无法稳定移除
            if (handler is ValueType) throw new NotSupportedException("不支持值类型的处理器，请使用引用类型");

            lock (_subscribersSync)
            {
                CleanupDeadSubscribers();

                if (enabled)
                {

                    var oldSubscribers = _subscribersSnapshot;
                    // 已存在则返回（按引用相等去重）
                    if (IndexOf(oldSubscribers, handler) >= 0) return;
                    // 不存在则添加订阅
                    int oldLength = oldSubscribers.Length;
                    var newSubscribers = new WeakReference<object>[oldLength + 1];
                    Array.Copy(oldSubscribers, 0, newSubscribers, 0, oldLength);
                    newSubscribers[oldLength] = new WeakReference<object>(handler);
                    _subscribersSnapshot = newSubscribers;
                    if (oldLength == 0) SubscribeEvent();

                }
                else
                {
                    var oldSubscribers = _subscribersSnapshot;
                    int oldLength = oldSubscribers.Length;
                    int index = IndexOf(oldSubscribers, handler);
                    if (index < 0) return;
                    if (oldLength == 1)
                    {
                        _subscribersSnapshot = Array.Empty<WeakReference<object>>();
                        UnsubscribeEvent();
                        return;
                    }
                    var newSubscribers = new WeakReference<object>[oldLength - 1];
                    if (index > 0)
                    {
                        Array.Copy(oldSubscribers, 0, newSubscribers, 0, index);
                    }
                    if (index < oldLength - 1)
                    {
                        Array.Copy(oldSubscribers, index + 1, newSubscribers, index, oldLength - index - 1);
                    }
                    _subscribersSnapshot = newSubscribers;
                }
            }
        }

        private static void CleanupDeadSubscribers()
        {
            var oldSubscribers = _subscribersSnapshot;
            int aliveCount = 0;
            for (int i = 0; i < oldSubscribers.Length; i++)
            {
                if (oldSubscribers[i].TryGetTarget(out _)) aliveCount++;
            }
            // 无需变更
            if (aliveCount == oldSubscribers.Length) return;

            if (aliveCount == 0)
            {
                _subscribersSnapshot = Array.Empty<WeakReference<object>>();
                UnsubscribeEvent();
                return;
            }

            var newSubscribers = new WeakReference<object>[aliveCount];
            int temp = 0;
            for (int i = 0; i < oldSubscribers.Length; i++)
            {
                if (oldSubscribers[i].TryGetTarget(out _)) newSubscribers[temp++] = oldSubscribers[i];
            }
            _subscribersSnapshot = newSubscribers;

        }

        // 在 arr 中寻找 target 的引用相等索引
        private static int IndexOf(WeakReference<object>[] arr, object target)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i].TryGetTarget(out var obj) && ReferenceEquals(obj, target)) return i;
            }
            return -1;
        }

        private static void SubscribeEvent()
        {
#if UNITY_SUPPORT
            UnityEngine.Application.logMessageReceivedThreaded += UnityLogHandler;
#endif
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;

        }

        private static void UnsubscribeEvent()
        {
#if UNITY_SUPPORT
            UnityEngine.Application.logMessageReceivedThreaded -= UnityLogHandler;
#endif
            AppDomain.CurrentDomain.UnhandledException -= UnhandledExceptionHandler;
        }

        /// <summary>
        /// 分发到当前快照；当某订阅者抛异常，将异常转为 Log 并广播给其他订阅者
        /// </summary>
        /// <param name="log"></param>
        private static void Broadcast(Log log)
        {
            var snapshot = _subscribersSnapshot;
            if (snapshot.Length == 0) return;

            int aliveCount = 0;
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (!snapshot[i].TryGetTarget(out var obj)) continue;
                aliveCount++;
                try
                {
                    if (obj is ILogHandler<Log> typed)
                    {
                        typed.Log(log);
                    }
                    else if (obj is ILogHandler generic)
                    {
                        generic.Log(log);
                    }
                    // 其他类型忽略（允许未来扩展）
                }
                catch (Exception ex)
                {
                    // 将订阅者异常包装成日志，广播给其他订阅者（跳过当前 i）
                    var errorLog = new Log(LogLevel.Error, $"[SubscriberError] {ex.Message}\n{ex.StackTrace}");
                    DeliverToOthers(snapshot, i, errorLog);
                }
            }

            // 若快照内没有任何存活订阅者，则尝试清空并退订
            if (aliveCount == 0)
            {
                lock (_subscribersSync)
                {
                    // 二次检查，避免竞争
                    var oldSubscribers = _subscribersSnapshot;
                    bool anyAlive = false;
                    for (int i = 0; i < oldSubscribers.Length; i++)
                    {
                        if (oldSubscribers[i].TryGetTarget(out _))
                        {
                            anyAlive = true;
                            break;
                        }
                    }
                    if (!anyAlive && oldSubscribers.Length > 0)
                    {
                        _subscribersSnapshot = Array.Empty<WeakReference<object>>();
                        UnsubscribeEvent();
                    }
                }
            }
        }

        /// <summary>
        /// 将 log 发送给除 skipIndex 外的其他订阅者；二次异常直接吞掉，避免级联
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="skipIndex"></param>
        /// <param name="log"></param>
        private static void DeliverToOthers(WeakReference<object>[] snapshot, int skipIndex, Log log)
        {
            for (int j = 0; j < snapshot.Length; j++)
            {
                if (j == skipIndex) continue;
                if (!snapshot[j].TryGetTarget(out var obj)) continue;

                try
                {
                    if (obj is ILogHandler<Log> typed)
                    {
                        typed.Log(log);
                    }
                    else if (obj is ILogHandler generic)
                    {
                        generic.Log(log);
                    }
                }
                catch
                {
                    // 防止递归与级联异常
                }
            }
        }

#if UNITY_SUPPORT
        private static void UnityLogHandler(string condition, string stackTrace, UnityEngine.LogType type)
        {
            if (type != UnityEngine.LogType.Exception) return;
            Broadcast(new Log(LogLevel.Error, $"{condition}\n{stackTrace}"));
        }
#endif

        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                // 注意：e.IsTerminating 可能为 true，请避免阻塞或重 IO
                Broadcast(new Log(LogLevel.Error, $"[Unhandled] {ex.Message}\n{ex.StackTrace}"));
            }
        }
    }
}


