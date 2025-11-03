using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace EscapeFromDuckovCoopMod.Chat.UI
{
    /// <summary>
    /// 聊天状态指示器UI组件
    /// </summary>
    public class ChatStatusIndicator : MonoBehaviour
    {
        [Header("状态指示器组件")]
        [SerializeField] private GameObject indicatorPanel;
        [SerializeField] private Image statusIcon;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Image backgroundImage;

        [Header("状态颜色设置")]
        [SerializeField] private Color normalColor = Color.green;
        [SerializeField] private Color inputActiveColor = Color.blue;
        [SerializeField] private Color errorColor = Color.red;
        [SerializeField] private Color warningColor = Color.yellow;

        [Header("动画设置")]
        [SerializeField] private float fadeInDuration = 0.3f;
        [SerializeField] private float fadeOutDuration = 0.2f;
        [SerializeField] private float displayDuration = 2f;
        [SerializeField] private bool enablePulseAnimation = true;
        [SerializeField] private float pulseSpeed = 2f;

        private ChatStatus currentStatus = ChatStatus.Hidden;
        private Coroutine displayCoroutine;
        private Coroutine pulseCoroutine;
        private CanvasGroup canvasGroup;

        /// <summary>
        /// 状态改变事件
        /// </summary>
        public event Action<ChatStatus> OnStatusChanged;

        /// <summary>
        /// 当前状态
        /// </summary>
        public ChatStatus CurrentStatus => currentStatus;

        /// <summary>
        /// 初始化状态指示器
        /// </summary>
        public void Initialize()
        {
            // 获取或添加CanvasGroup组件
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            // 初始状态为隐藏
            SetStatus(ChatStatus.Hidden, false);

            Debug.Log("聊天状态指示器初始化完成");
        }

        /// <summary>
        /// 设置状态
        /// </summary>
        /// <param name="status">状态</param>
        /// <param name="animated">是否使用动画</param>
        public void SetStatus(ChatStatus status, bool animated = true)
        {
            if (currentStatus == status)
                return;

            var previousStatus = currentStatus;
            currentStatus = status;

            UpdateStatusDisplay(animated);
            OnStatusChanged?.Invoke(status);

            Debug.Log($"聊天状态改变: {previousStatus} -> {status}");
        }

        /// <summary>
        /// 显示临时状态消息
        /// </summary>
        /// <param name="status">状态</param>
        /// <param name="message">消息内容</param>
        /// <param name="duration">显示时长</param>
        public void ShowTemporaryStatus(ChatStatus status, string message, float duration = 0f)
        {
            if (duration <= 0f)
                duration = displayDuration;

            // 停止之前的显示协程
            if (displayCoroutine != null)
            {
                StopCoroutine(displayCoroutine);
            }

            displayCoroutine = StartCoroutine(ShowTemporaryStatusCoroutine(status, message, duration));
        }

        /// <summary>
        /// 显示输入激活状态
        /// </summary>
        public void ShowInputActive()
        {
            SetStatus(ChatStatus.InputActive, true);
        }

        /// <summary>
        /// 显示正常状态
        /// </summary>
        public void ShowNormal()
        {
            SetStatus(ChatStatus.Normal, true);
        }

        /// <summary>
        /// 显示错误状态
        /// </summary>
        /// <param name="errorMessage">错误消息</param>
        public void ShowError(string errorMessage)
        {
            ShowTemporaryStatus(ChatStatus.Error, errorMessage);
        }

        /// <summary>
        /// 显示警告状态
        /// </summary>
        /// <param name="warningMessage">警告消息</param>
        public void ShowWarning(string warningMessage)
        {
            ShowTemporaryStatus(ChatStatus.Warning, warningMessage);
        }

        /// <summary>
        /// 隐藏指示器
        /// </summary>
        public void Hide()
        {
            SetStatus(ChatStatus.Hidden, true);
        }

        /// <summary>
        /// 更新状态显示
        /// </summary>
        /// <param name="animated">是否使用动画</param>
        private void UpdateStatusDisplay(bool animated)
        {
            switch (currentStatus)
            {
                case ChatStatus.Hidden:
                    UpdateHiddenStatus(animated);
                    break;
                case ChatStatus.Normal:
                    UpdateNormalStatus(animated);
                    break;
                case ChatStatus.InputActive:
                    UpdateInputActiveStatus(animated);
                    break;
                case ChatStatus.Error:
                    UpdateErrorStatus(animated);
                    break;
                case ChatStatus.Warning:
                    UpdateWarningStatus(animated);
                    break;
            }
        }

        /// <summary>
        /// 更新隐藏状态
        /// </summary>
        /// <param name="animated">是否使用动画</param>
        private void UpdateHiddenStatus(bool animated)
        {
            StopPulseAnimation();

            if (animated)
            {
                StartCoroutine(FadeOut());
            }
            else
            {
                SetAlpha(0f);
                if (indicatorPanel != null)
                    indicatorPanel.SetActive(false);
            }
        }

        /// <summary>
        /// 更新正常状态
        /// </summary>
        /// <param name="animated">是否使用动画</param>
        private void UpdateNormalStatus(bool animated)
        {
            StopPulseAnimation();

            if (statusIcon != null)
                statusIcon.color = normalColor;
            if (backgroundImage != null)
                backgroundImage.color = normalColor;
            if (statusText != null)
                statusText.text = "聊天就绪";

            ShowIndicator(animated);
        }

        /// <summary>
        /// 更新输入激活状态
        /// </summary>
        /// <param name="animated">是否使用动画</param>
        private void UpdateInputActiveStatus(bool animated)
        {
            if (statusIcon != null)
                statusIcon.color = inputActiveColor;
            if (backgroundImage != null)
                backgroundImage.color = inputActiveColor;
            if (statusText != null)
                statusText.text = "正在输入...";

            ShowIndicator(animated);
            StartPulseAnimation();
        }

        /// <summary>
        /// 更新错误状态
        /// </summary>
        /// <param name="animated">是否使用动画</param>
        private void UpdateErrorStatus(bool animated)
        {
            StopPulseAnimation();

            if (statusIcon != null)
                statusIcon.color = errorColor;
            if (backgroundImage != null)
                backgroundImage.color = errorColor;

            ShowIndicator(animated);
        }

        /// <summary>
        /// 更新警告状态
        /// </summary>
        /// <param name="animated">是否使用动画</param>
        private void UpdateWarningStatus(bool animated)
        {
            StopPulseAnimation();

            if (statusIcon != null)
                statusIcon.color = warningColor;
            if (backgroundImage != null)
                backgroundImage.color = warningColor;

            ShowIndicator(animated);
        }

        /// <summary>
        /// 显示指示器
        /// </summary>
        /// <param name="animated">是否使用动画</param>
        private void ShowIndicator(bool animated)
        {
            if (indicatorPanel != null)
                indicatorPanel.SetActive(true);

            if (animated)
            {
                StartCoroutine(FadeIn());
            }
            else
            {
                SetAlpha(1f);
            }
        }

        /// <summary>
        /// 淡入动画
        /// </summary>
        /// <returns>协程</returns>
        private IEnumerator FadeIn()
        {
            float elapsedTime = 0f;
            float startAlpha = canvasGroup != null ? canvasGroup.alpha : 0f;

            while (elapsedTime < fadeInDuration)
            {
                elapsedTime += Time.unscaledDeltaTime;
                float alpha = Mathf.Lerp(startAlpha, 1f, elapsedTime / fadeInDuration);
                SetAlpha(alpha);
                yield return null;
            }

            SetAlpha(1f);
        }

        /// <summary>
        /// 淡出动画
        /// </summary>
        /// <returns>协程</returns>
        private IEnumerator FadeOut()
        {
            float elapsedTime = 0f;
            float startAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;

            while (elapsedTime < fadeOutDuration)
            {
                elapsedTime += Time.unscaledDeltaTime;
                float alpha = Mathf.Lerp(startAlpha, 0f, elapsedTime / fadeOutDuration);
                SetAlpha(alpha);
                yield return null;
            }

            SetAlpha(0f);
            if (indicatorPanel != null)
                indicatorPanel.SetActive(false);
        }

        /// <summary>
        /// 显示临时状态协程
        /// </summary>
        /// <param name="status">状态</param>
        /// <param name="message">消息</param>
        /// <param name="duration">持续时间</param>
        /// <returns>协程</returns>
        private IEnumerator ShowTemporaryStatusCoroutine(ChatStatus status, string message, float duration)
        {
            var originalStatus = currentStatus;

            // 设置临时状态
            currentStatus = status;
            if (statusText != null)
                statusText.text = message;
            UpdateStatusDisplay(true);

            // 等待指定时间
            yield return new WaitForSecondsRealtime(duration);

            // 恢复原状态
            currentStatus = originalStatus;
            UpdateStatusDisplay(true);

            displayCoroutine = null;
        }

        /// <summary>
        /// 开始脉冲动画
        /// </summary>
        private void StartPulseAnimation()
        {
            if (!enablePulseAnimation)
                return;

            StopPulseAnimation();
            pulseCoroutine = StartCoroutine(PulseAnimation());
        }

        /// <summary>
        /// 停止脉冲动画
        /// </summary>
        private void StopPulseAnimation()
        {
            if (pulseCoroutine != null)
            {
                StopCoroutine(pulseCoroutine);
                pulseCoroutine = null;
            }
        }

        /// <summary>
        /// 脉冲动画协程
        /// </summary>
        /// <returns>协程</returns>
        private IEnumerator PulseAnimation()
        {
            while (true)
            {
                // 脉冲效果
                float time = Time.unscaledTime * pulseSpeed;
                float alpha = 0.5f + 0.5f * Mathf.Sin(time);

                if (statusIcon != null)
                {
                    var color = statusIcon.color;
                    color.a = alpha;
                    statusIcon.color = color;
                }

                yield return null;
            }
        }

        /// <summary>
        /// 设置透明度
        /// </summary>
        /// <param name="alpha">透明度值</param>
        private void SetAlpha(float alpha)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = alpha;
            }
        }

        /// <summary>
        /// 组件销毁时清理
        /// </summary>
        private void OnDestroy()
        {
            if (displayCoroutine != null)
            {
                StopCoroutine(displayCoroutine);
            }

            StopPulseAnimation();
        }
    }

    /// <summary>
    /// 聊天状态枚举
    /// </summary>
    public enum ChatStatus
    {
        /// <summary>
        /// 隐藏状态
        /// </summary>
        Hidden = 0,

        /// <summary>
        /// 正常状态
        /// </summary>
        Normal = 1,

        /// <summary>
        /// 输入激活状态
        /// </summary>
        InputActive = 2,

        /// <summary>
        /// 错误状态
        /// </summary>
        Error = 3,

        /// <summary>
        /// 警告状态
        /// </summary>
        Warning = 4
    }
}