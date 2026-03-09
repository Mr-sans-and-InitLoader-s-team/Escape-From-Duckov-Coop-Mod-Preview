using Duckov.Scenes;
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

/// <summary>
/// 场景加载同步等待界面 - 全屏显示同步进度
/// </summary>
public class WaitingSynchronizationUI : MonoBehaviour
{
    private static WaitingSynchronizationUI _instance;
    public static WaitingSynchronizationUI Instance => _instance;

    private Canvas _canvas;
    private GameObject _panel;
    private CanvasGroup _canvasGroup; // ✅ 用于淡入淡出效果
    private TMP_Text _titleText;
    private TMP_Text _syncStatusText;
    private TMP_Text _syncPercentText;
    private TMP_Text _countdownText;
    private TMP_Text _mapInfoText;
    private TMP_Text _timeInfoText;
    private TMP_Text _weatherInfoText;
    private GameObject _playerListContainer;
    private GameObject _loadingAnimation;
    private float _loadingRotation = 0f;

    // 同步任务状态
    private Dictionary<string, SyncTaskStatus> _syncTasks = new Dictionary<string, SyncTaskStatus>();
    private bool _allTasksCompleted = false;


    // 淡出协程引用
    private Coroutine _fadeOutCoroutine = null;

    // 倒计时相关
    private GameObject _countdownContainer;
    private float _countdownDuration = 10f;
    private float _countdownRemaining;
    private bool _countdownRunning = false;
    private readonly List<GlowPulse> _glowPulses = new List<GlowPulse>();
    private static Sprite _glowFallbackSprite;

    private class GlowPulse
    {
        public RectTransform Rect;
        public CanvasGroup CanvasGroup;
        public float Duration;
        public float Timer;
        public float StartScale;
        public float EndScale;
        public float StartAlpha;
        public float EndAlpha;
    }

    public class SyncTaskStatus
    {
        public string Name;
        public bool IsCompleted;
        public string Details;
    }

    private void Awake()
    {
        Debug.Log("[SYNC_UI] Awake() 被调用");

        if (_instance != null && _instance != this)
        {
            Debug.Log("[SYNC_UI] 已存在实例，销毁当前对象");
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[SYNC_UI] 实例已创建并设置为DontDestroyOnLoad");

        CreateUI();
        Debug.Log("[SYNC_UI] UI创建完成");

        // ✅ 初始化时直接隐藏，不需要淡出效果
        if (_panel != null)
        {
            _panel.SetActive(false);
        }
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
        }
        Debug.Log("[SYNC_UI] 初始隐藏完成");
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    private void CreateUI()
    {
        // 创建Canvas
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 9999;

        var canvasScaler = gameObject.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1920, 1080);

        gameObject.AddComponent<GraphicRaycaster>();

        // 创建全屏背景
        _panel = new GameObject("SyncPanel");
        _panel.transform.SetParent(transform);

        var panelRect = _panel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;
        panelRect.anchoredPosition = Vector2.zero;

        var panelImage = _panel.AddComponent<Image>();

        // ✅ 添加 CanvasGroup 用于淡入淡出效果
        _canvasGroup = _panel.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 1f;

        // 加载背景图片
        LoadBackgroundImage(panelImage);

        // 添加半透明黑色遮罩层降低背景亮度
        var darkOverlay = new GameObject("DarkOverlay");
        darkOverlay.transform.SetParent(_panel.transform);
        var overlayRect = darkOverlay.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.sizeDelta = Vector2.zero;
        overlayRect.anchoredPosition = Vector2.zero;

        var overlayImage = darkOverlay.AddComponent<Image>();
        overlayImage.color = new Color(0, 0, 0, 0.94f);

        // ========== 中央倒计时红色方块 ==========
        _countdownContainer = new GameObject("CountdownContainer");
        _countdownContainer.transform.SetParent(_panel.transform);
        var countdownRect = _countdownContainer.AddComponent<RectTransform>();
        countdownRect.anchorMin = new Vector2(0.5f, 0.5f);
        countdownRect.anchorMax = new Vector2(0.5f, 0.5f);
        countdownRect.pivot = new Vector2(0.5f, 0.5f);
        countdownRect.sizeDelta = new Vector2(220, 120);
        countdownRect.anchoredPosition = Vector2.zero;

        CreateGlowLayer(_countdownContainer.transform, new Vector2(230, 140), 0.35f, 0.9f);
        CreateGlowLayer(_countdownContainer.transform, new Vector2(270, 180), 0.22f, 0.75f);
        CreateGlowLayer(_countdownContainer.transform, new Vector2(330, 230), 0.12f, 0.6f);

        CreatePulsingGlowLayer(_countdownContainer.transform, new Vector2(230, 140), 1.0f, 1.35f, 0.45f, 0f, 1.4f, 0f);
        CreatePulsingGlowLayer(_countdownContainer.transform, new Vector2(270, 180), 1.05f, 1.45f, 0.3f, 0f, 1.8f, 0.6f);
        CreatePulsingGlowLayer(_countdownContainer.transform, new Vector2(320, 220), 1.1f, 1.55f, 0.25f, 0f, 2.2f, 1.2f);

        var countdownImage = _countdownContainer.AddComponent<Image>();
        countdownImage.color = new Color(0.9f, 0.05f, 0.05f, 1f);

        // 添加更柔和的红色发光阴影
        var outline = _countdownContainer.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 0.25f, 0.25f, 0.85f);
        outline.effectDistance = new Vector2(8f, 8f);

        _countdownText = CreateSimpleText("CountdownText", _countdownContainer.transform, 40, FontStyles.Bold);
        _countdownText.alignment = TextAlignmentOptions.Center;
        _countdownText.enableWordWrapping = false;
        _countdownText.overflowMode = TextOverflowModes.Overflow;
        var countdownTextRect = _countdownText.GetComponent<RectTransform>();
        countdownTextRect.anchorMin = Vector2.zero;
        countdownTextRect.anchorMax = Vector2.one;
        countdownTextRect.sizeDelta = Vector2.zero;
        countdownTextRect.anchoredPosition = Vector2.zero;
        _countdownText.text = "00:10:000";

        // ========== 底部中间同步状态区域 ==========
        var bottomSyncPanel = new GameObject("BottomSyncPanel");
        bottomSyncPanel.transform.SetParent(_panel.transform);
        var bottomSyncRect = bottomSyncPanel.AddComponent<RectTransform>();
        bottomSyncRect.anchorMin = new Vector2(0.3f, 0);
        bottomSyncRect.anchorMax = new Vector2(0.7f, 0);
        bottomSyncRect.anchoredPosition = new Vector2(0, 100);
        bottomSyncRect.sizeDelta = new Vector2(0, 120);

        var bottomSyncLayout = bottomSyncPanel.AddComponent<HorizontalLayoutGroup>();
        bottomSyncLayout.childAlignment = TextAnchor.MiddleCenter;
        bottomSyncLayout.spacing = 20;
        bottomSyncLayout.childControlHeight = false;
        bottomSyncLayout.childControlWidth = false;
        bottomSyncLayout.childForceExpandHeight = false;
        bottomSyncLayout.childForceExpandWidth = false;

        // 加载动画
        _loadingAnimation = CreateLoadingAnimation(bottomSyncPanel.transform);
        var loadingRect = _loadingAnimation.GetComponent<RectTransform>();
        loadingRect.sizeDelta = new Vector2(40, 40);

        // 保留加载动画，移除进度文本
    }

    private void Update()
    {
        // 旋转加载动画
        if (_loadingAnimation != null && _loadingAnimation.activeSelf)
        {
            _loadingRotation += 360f * Time.deltaTime; // 每秒旋转360度
            if (_loadingRotation >= 360f) _loadingRotation -= 360f;
            _loadingAnimation.transform.rotation = Quaternion.Euler(0, 0, -_loadingRotation);
        }

        // 更新倒计时
        UpdateCountdown();
        AnimateGlowPulses();

        // 检查是否所有任务完成
        if (!_allTasksCompleted && _panel != null && _panel.activeSelf)
        {
            CheckAndHideIfComplete();
        }
    }

    private void UpdateCountdown()
    {
        if (!_countdownRunning || _countdownContainer == null || _countdownText == null) return;
        if (_panel == null || !_panel.activeSelf) return;

        _countdownRemaining -= Time.deltaTime;
        if (_countdownRemaining < 0f)
        {
            _countdownRemaining = 0f;
        }

        _countdownText.text = FormatCountdown(_countdownRemaining);

        if (_countdownRemaining <= 0f)
        {
            _countdownRunning = false;
            Hide();
        }
    }

    private string FormatCountdown(float seconds)
    {
        var time = TimeSpan.FromSeconds(Mathf.Max(0f, seconds));
        return $"{time.Minutes:00}:{time.Seconds:00}:{time.Milliseconds:000}";
    }

    private void AnimateGlowPulses()
    {
        if (_glowPulses.Count == 0 || _countdownContainer == null || !_countdownContainer.activeSelf) return;

        foreach (var pulse in _glowPulses)
        {
            if (pulse.Rect == null || pulse.CanvasGroup == null || pulse.Duration <= 0f) continue;

            pulse.Timer += Time.deltaTime;
            float progress = (pulse.Timer % pulse.Duration) / pulse.Duration;
            float scale = Mathf.Lerp(pulse.StartScale, pulse.EndScale, progress);
            float alpha = Mathf.Lerp(pulse.StartAlpha, pulse.EndAlpha, progress);

            pulse.Rect.localScale = Vector3.one * scale;
            pulse.CanvasGroup.alpha = alpha;
        }
    }

    private void UpdateProgressDisplay()
    {
        if (_syncStatusText == null || _syncPercentText == null) return;
        if (!_panel.activeSelf) return;

        try
        {
            int completed = _syncTasks.Count(t => t.Value.IsCompleted);
            int total = _syncTasks.Count;

            if (total == 0)
            {
                _syncStatusText.text = CoopLocalization.Get("ui.waiting.initializing");
                _syncPercentText.text = "0%";
                return;
            }

            // 计算百分比
            float percent = (float)completed / total * 100f;
            _syncPercentText.text = $"{percent:F0}%";

            // 根据进度改变颜色
            if (percent >= 100f)
            {
                _syncPercentText.color = new Color(0.5f, 1f, 0.5f, 1f); // 绿色
            }
            else if (percent >= 50f)
            {
                _syncPercentText.color = new Color(1f, 1f, 0.5f, 1f); // 黄色
            }
            else
            {
                _syncPercentText.color = new Color(1f, 0.7f, 0.5f, 1f); // 橙色
            }

            // 显示当前正在执行的任务
            var currentTask = _syncTasks.FirstOrDefault(t => !t.Value.IsCompleted);
            if (currentTask.Value != null)
            {
                string detail = string.IsNullOrEmpty(currentTask.Value.Details) ? "" : $" - {currentTask.Value.Details}";
                _syncStatusText.text = $"{currentTask.Value.Name}{detail}";
            }
            else
            {
                _syncStatusText.text = CoopLocalization.Get("ui.waiting.syncComplete");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SYNC_UI] 更新进度显示失败: {ex.Message}");
        }
    }

    private void UpdateMapAndWeatherInfo()
    {
        if (_mapInfoText == null || _timeInfoText == null || _weatherInfoText == null) return;
        if (!_panel.activeSelf) return;

        try
        {
            // 更新地图信息
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            _mapInfoText.text = CoopLocalization.Get("ui.waiting.map", MultiSceneCore.Instance.DisplayName);

            // 更新游戏时间 - 使用 GameClock
            try
            {
                var day = GameClock.Day;
                var timeOfDay = GameClock.TimeOfDay;
                var hours = timeOfDay.Hours;
                var minutes = timeOfDay.Minutes;
                _timeInfoText.text = CoopLocalization.Get("ui.waiting.time", day, hours, minutes);
            }
            catch
            {
                _timeInfoText.text = CoopLocalization.Get("ui.waiting.timeUnknown");
            }

            // 更新天气信息 - 使用 TimeOfDayController
            try
            {
                if (TimeOfDayController.Instance != null)
                {
                    var currentWeather = TimeOfDayController.Instance.CurrentWeather;
                    var weatherName = TimeOfDayController.GetWeatherNameByWeather(currentWeather);
                    _weatherInfoText.text = CoopLocalization.Get("ui.waiting.weather", weatherName);
                }
                else
                {
                    _weatherInfoText.text = CoopLocalization.Get("ui.waiting.weatherUnknown");
                }
            }
            catch
            {
                _weatherInfoText.text = CoopLocalization.Get("ui.waiting.weatherUnknown");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SYNC_UI] 更新地图天气信息失败: {ex.Message}");
        }
    }


    private void CheckAndHideIfComplete()
    {
        if (_syncTasks.Count == 0) return;

        bool allComplete = _syncTasks.All(t => t.Value.IsCompleted);
        if (allComplete)
        {
            _allTasksCompleted = true;
            StartCoroutine(HideAfterDelay(1f)); // 1秒后隐藏
        }
    }

    private IEnumerator HideAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Hide();
    }

    private void LoadBackgroundImage(Image targetImage)
    {
        try
        {
            // 获取模组目录路径
            var modPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var bgPath = Path.Combine(modPath, "Assets", "bg.png");

            Debug.Log($"[SYNC_UI] 尝试加载背景图片: {bgPath}");

            if (File.Exists(bgPath))
            {
                // 读取图片文件
                var fileData = File.ReadAllBytes(bgPath);

                // 创建 Texture2D
                var texture = new Texture2D(2, 2);
                if (texture.LoadImage(fileData))
                {
                    // 创建 Sprite
                    var sprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f)
                    );

                    // 设置为背景
                    targetImage.sprite = sprite;
                    targetImage.color = Color.white; // 使用白色以显示原始图片
                    targetImage.type = Image.Type.Simple;
                    targetImage.preserveAspect = false; // 拉伸填充整个屏幕

                    Debug.Log($"[SYNC_UI] 背景图片加载成功: {texture.width}x{texture.height}");
                }
                else
                {
                    Debug.LogWarning("[SYNC_UI] 无法解析图片数据，使用纯黑背景");
                    targetImage.color = Color.black;
                }
            }
            else
            {
                Debug.LogWarning($"[SYNC_UI] 背景图片不存在: {bgPath}，使用纯黑背景");
                targetImage.color = Color.black;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SYNC_UI] 加载背景图片失败: {ex.Message}");
            targetImage.color = Color.black;
        }
    }

    private void CreateGlowLayer(Transform parent, Vector2 size, float alpha, float intensity)
    {
        var glowObj = new GameObject($"GlowLayer_{size.x}x{size.y}");
        glowObj.transform.SetParent(parent);

        var rect = glowObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;

        var glowImage = glowObj.AddComponent<Image>();
        glowImage.sprite = GetGlowSprite();
        glowImage.type = Image.Type.Sliced;
        glowImage.raycastTarget = false;
        glowImage.color = new Color(1f, 0f, 0f, alpha);

        var canvasGroup = glowObj.AddComponent<CanvasGroup>();
        canvasGroup.alpha = intensity;

        glowObj.transform.SetAsFirstSibling();
    }

    private void CreatePulsingGlowLayer(Transform parent, Vector2 size, float startScale, float endScale, float startAlpha, float endAlpha, float duration, float initialOffset)
    {
        var glowObj = new GameObject("GlowPulseLayer");
        glowObj.transform.SetParent(parent);

        var rect = glowObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one * startScale;

        var glowImage = glowObj.AddComponent<Image>();
        glowImage.sprite = GetGlowSprite();
        glowImage.type = Image.Type.Sliced;
        glowImage.raycastTarget = false;
        glowImage.color = new Color(1f, 0f, 0f, startAlpha);

        var canvasGroup = glowObj.AddComponent<CanvasGroup>();
        canvasGroup.alpha = startAlpha;

        glowObj.transform.SetAsFirstSibling();

        _glowPulses.Add(new GlowPulse
        {
            Rect = rect,
            CanvasGroup = canvasGroup,
            Duration = Mathf.Max(0.01f, duration),
            Timer = initialOffset,
            StartScale = startScale,
            EndScale = endScale,
            StartAlpha = startAlpha,
            EndAlpha = endAlpha
        });
    }

    private Sprite GetGlowSprite()
    {
        if (_glowFallbackSprite != null) return _glowFallbackSprite;

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.SetPixels(new[]
        {
            Color.white, Color.white,
            Color.white, Color.white
        });
        texture.Apply();

        _glowFallbackSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 1f);
        _glowFallbackSprite.name = "GlowFallbackSprite";
        return _glowFallbackSprite;
    }

    private TMP_Text CreateSimpleText(string name, Transform parent, int fontSize, FontStyles fontStyle)
    {
        var textObj = new GameObject(name);
        textObj.transform.SetParent(parent);

        var rectTransform = textObj.AddComponent<RectTransform>();

        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = Color.white;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;

        return text;
    }

    private GameObject CreateButton(string name, Transform parent, string buttonText)
    {
        var buttonObj = new GameObject(name);
        buttonObj.transform.SetParent(parent);

        var buttonRect = buttonObj.AddComponent<RectTransform>();
        var button = buttonObj.AddComponent<Button>();
        var buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = new Color(0.2f, 0.4f, 0.6f, 0.9f);

        // 按钮文字
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(buttonObj.transform);
        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.anchoredPosition = Vector2.zero;

        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = buttonText;
        text.fontSize = 24;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;

        //// 按钮点击事件
        //button.onClick.AddListener(() =>
        //{
        //    if (SceneNet.Instance != null && SceneNet.Instance.currentRaidRoom != null)
        //    {
        //        SceneNet.Instance.Host_StartRaidRoom();
        //    }
        //});

        // 悬停效果
        var colors = button.colors;
        colors.normalColor = new Color(0.2f, 0.4f, 0.6f, 0.9f);
        colors.highlightedColor = new Color(0.3f, 0.5f, 0.7f, 1f);
        colors.pressedColor = new Color(0.15f, 0.35f, 0.55f, 1f);
        button.colors = colors;

        return buttonObj;
    }

    private GameObject CreateLoadingAnimation(Transform parent)
    {
        var loadingObj = new GameObject("LoadingAnimation");
        loadingObj.transform.SetParent(parent);

        var rectTransform = loadingObj.AddComponent<RectTransform>();
        var image = loadingObj.AddComponent<Image>();

        // 创建圆环加载动画（使用Unity基础图形）
        var texture = new Texture2D(64, 64);
        var pixels = new Color[64 * 64];

        // 绘制圆环
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float dx = x - 32f;
                float dy = y - 32f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

                // 圆环：外半径28，内半径22
                if (distance > 22 && distance < 28)
                {
                    // 渐变效果：从0度到270度渐变显示
                    float normalizedAngle = (angle + 180f) / 360f;
                    if (normalizedAngle < 0.75f) // 显示270度
                    {
                        float alpha = Mathf.Clamp01(normalizedAngle / 0.75f);
                        pixels[y * 64 + x] = new Color(0.7f, 0.9f, 1f, alpha);
                    }
                    else
                    {
                        pixels[y * 64 + x] = Color.clear;
                    }
                }
                else
                {
                    pixels[y * 64 + x] = Color.clear;
                }
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        var sprite = Sprite.Create(texture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
        image.sprite = sprite;

        return loadingObj;
    }


    // ========== 以下方法已移除，不再需要 ==========

    private GameObject CreatePlayerAvatar()
    {
        var avatarObj = new GameObject("Avatar");
        var rectTransform = avatarObj.AddComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(96, 96);

        var image = avatarObj.AddComponent<Image>();
        image.color = new Color(0.3f, 0.3f, 0.3f, 1f);

        var texture = new Texture2D(64, 64);
        var pixels = new Color[64 * 64];

        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float dx = x - 32f;
                float dy = y - 32f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                if (distance < 30)
                {
                    pixels[y * 64 + x] = new Color(0.3f, 0.5f, 0.7f, 1f);

                    float headDx = dx;
                    float headDy = dy + 8;
                    float headDist = Mathf.Sqrt(headDx * headDx + headDy * headDy);
                    if (headDist < 10)
                    {
                        pixels[y * 64 + x] = new Color(0.9f, 0.9f, 0.9f, 1f);
                    }

                    if (y < 28 && Mathf.Abs(dx) < 12)
                    {
                        pixels[y * 64 + x] = new Color(0.9f, 0.9f, 0.9f, 1f);
                    }
                }
                else
                {
                    pixels[y * 64 + x] = Color.clear;
                }
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        var sprite = Sprite.Create(texture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
        image.sprite = sprite;

        return avatarObj;
    }

    private GameObject CreatePlayerEntry(string playerName, string playerEndPoint)
    {
        // 创建玩家条目容器（水平布局）
        var entryObj = new GameObject($"Player_{playerName}");
        entryObj.transform.SetParent(_playerListContainer.transform);

        var entryRect = entryObj.AddComponent<RectTransform>();
        entryRect.sizeDelta = new Vector2(0, 112); // ✅ 放大高度到112（96头像+16间距）

        var horizontalLayout = entryObj.AddComponent<HorizontalLayoutGroup>();
        horizontalLayout.childAlignment = TextAnchor.MiddleLeft;
        horizontalLayout.spacing = 20; // ✅ 增大间距到20
        horizontalLayout.padding = new RectOffset(8, 8, 8, 8); // ✅ 增大内边距
        horizontalLayout.childControlHeight = false;
        horizontalLayout.childControlWidth = false;
        horizontalLayout.childForceExpandHeight = false;
        horizontalLayout.childForceExpandWidth = false;

        var displayName = playerName;

        // 添加头像
        var avatar = CreatePlayerAvatar();
        avatar.transform.SetParent(entryObj.transform);

        // 添加玩家名字
        var playerText = CreateSimpleText("Name", entryObj.transform, 52, FontStyles.Normal); // ✅ 放大字体到52
        playerText.text = displayName;
        playerText.alignment = TextAlignmentOptions.Left;
        playerText.color = new Color(0.95f, 0.95f, 0.95f, 1f);

        var textRect = playerText.GetComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(500, 96); // ✅ 放大文字区域到500x96

        return entryObj;
    }

    /// <summary>
    /// 显示同步等待界面
    /// </summary>
    public void Show()
    {
        Debug.Log($"[SYNC_UI] Show() 被调用，_panel={(_panel != null ? "存在" : "null")}, _canvas={(_canvas != null ? "存在" : "null")}");

        // ✅ 停止正在进行的淡出协程（如果有）
        if (_fadeOutCoroutine != null)
        {
            StopCoroutine(_fadeOutCoroutine);
            _fadeOutCoroutine = null;
            Debug.Log("[SYNC_UI] 停止淡出协程");
        }

        // ✅ 重置 alpha 为 1，确保完全显示
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 1f;
        }

        if (_canvas != null)
        {
            _canvas.enabled = true;
            Debug.Log($"[SYNC_UI] Canvas已启用，sortingOrder={_canvas.sortingOrder}");
        }

        if (_panel != null)
        {
            _panel.SetActive(true);
            Debug.Log("[SYNC_UI] Panel已激活");
        }

        _syncTasks.Clear();
        _allTasksCompleted = false;

        _countdownRemaining = _countdownDuration;
        _countdownRunning = true;
        if (_countdownContainer != null)
        {
            _countdownContainer.SetActive(true);
        }
        if (_countdownText != null)
        {
            _countdownText.text = FormatCountdown(_countdownRemaining);
        }

        Debug.Log("[SYNC_UI] 显示同步等待界面完成");
    }

    /// <summary>
    /// 隐藏同步等待界面（带淡出效果）
    /// </summary>
    public void Hide()
    {
        _countdownRunning = false;
        if (_countdownContainer != null)
        {
            _countdownContainer.SetActive(false);
        }

        if (_panel != null && _panel.activeSelf)
        {
            // 停止之前的淡出协程（如果有）
            if (_fadeOutCoroutine != null)
            {
                StopCoroutine(_fadeOutCoroutine);
            }

            // 启动淡出协程
            _fadeOutCoroutine = StartCoroutine(FadeOut());
        }
        else if (_panel != null)
        {
            _panel.SetActive(false);
        }

        Debug.Log("[SYNC_UI] 开始淡出隐藏同步等待界面");
    }

    /// <summary>
    /// 淡出协程
    /// </summary>
    private IEnumerator FadeOut()
    {
        if (_canvasGroup == null)
        {
            Debug.LogWarning("[SYNC_UI] CanvasGroup为空，直接隐藏");
            _panel.SetActive(false);
            yield break;
        }

        float duration = 0.5f; // 淡出持续时间（秒）
        float elapsed = 0f;
        float startAlpha = _canvasGroup.alpha;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = elapsed / duration;
            _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, normalizedTime);
            yield return null;
        }

        // 确保最终alpha为0
        _canvasGroup.alpha = 0f;

        // 淡出完成后隐藏面板
        _panel.SetActive(false);
        _fadeOutCoroutine = null;

        Debug.Log("[SYNC_UI] 淡出完成，隐藏同步等待界面");
    }

    /// <summary>
    /// 注册同步任务
    /// </summary>
    public void RegisterTask(string taskId, string taskName)
    {
        if (!_syncTasks.ContainsKey(taskId))
        {
            _syncTasks[taskId] = new SyncTaskStatus
            {
                Name = taskName,
                IsCompleted = false,
                Details = ""
            };
            Debug.Log($"[SYNC_UI] 注册任务: {taskName}");
        }
    }

    /// <summary>
    /// 更新任务状态
    /// </summary>
    public void UpdateTaskStatus(string taskId, bool isCompleted, string details = "")
    {
        if (_syncTasks.TryGetValue(taskId, out var task))
        {
            task.IsCompleted = isCompleted;
            task.Details = details;
            Debug.Log($"[SYNC_UI] 任务状态更新: {task.Name} - {(isCompleted ? "完成" : "进行中")} {details}");
        }
    }

    /// <summary>
    /// 标记任务完成
    /// </summary>
    public void CompleteTask(string taskId, string details = "")
    {
        UpdateTaskStatus(taskId, true, details);
    }

    /// <summary>
    /// 更新玩家列表
    /// </summary>
    public void UpdatePlayerList()
    {
        if (_playerListContainer == null) return;

        try
        {
            // 清空现有玩家
            foreach (Transform child in _playerListContainer.transform)
            {
                Destroy(child.gameObject);
            }

            // 获取当前玩家列表
            var mod = ModBehaviourF.Instance;
            if (mod == null || mod.playerStatuses == null)
            {
                var emptyText = CreateSimpleText("Empty", _playerListContainer.transform, 20, FontStyles.Italic);
                emptyText.text = CoopLocalization.Get("ui.waiting.loadingPlayers");
                emptyText.color = new Color(0.6f, 0.6f, 0.6f, 1f);
                emptyText.alignment = TextAlignmentOptions.Center;
                var emptyRect = emptyText.GetComponent<RectTransform>();
                emptyRect.sizeDelta = new Vector2(0, 40);
                return;
            }

            // 添加本地玩家
            if (mod.localPlayerStatus != null)
            {
                CreatePlayerEntry(mod.localPlayerStatus.PlayerName, mod.localPlayerStatus.EndPoint);
            }

            // 添加远程玩家
            foreach (var kv in mod.playerStatuses)
            {
                var status = kv.Value;
                if (status != null)
                {
                    CreatePlayerEntry(status.PlayerName, status.EndPoint);
                }
            }

            Debug.Log($"[SYNC_UI] 更新玩家列表完成，共 {mod.playerStatuses.Count + 1} 名玩家");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SYNC_UI] 更新玩家列表失败: {ex.Message}");
        }
    }
}