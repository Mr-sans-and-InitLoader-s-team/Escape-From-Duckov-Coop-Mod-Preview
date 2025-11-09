// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Steamworks;
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
    private TMP_Text _mapInfoText;
    private TMP_Text _timeInfoText;
    private TMP_Text _weatherInfoText;
    private GameObject _playerListContainer;
    private GameObject _loadingAnimation;
    private float _loadingRotation = 0f;

    // 同步任务状态
    private Dictionary<string, SyncTaskStatus> _syncTasks =
        new Dictionary<string, SyncTaskStatus>();
    private bool _allTasksCompleted = false;

    // Steam头像缓存
    private Dictionary<ulong, Sprite> _steamAvatarCache = new Dictionary<ulong, Sprite>();

    // 淡出协程引用
    private Coroutine _fadeOutCoroutine = null;

    // 自动进度增长（75%后启用）
    private bool _autoProgressEnabled = false;
    private float _autoProgressPercent = 0f;
    private float _lastAutoProgressTime = 0f;

    // 无敌状态管理
    private Health _invincibilityTargetHealth = null;
    private bool? _originalInvincibleState = null;

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

        // ========== 顶部标题 ==========
        _titleText = CreateSimpleText("Title", _panel.transform, 56, FontStyles.Bold);
        _titleText.text = "正在加载场景...";
        _titleText.alignment = TextAlignmentOptions.Center;
        var titleRect = _titleText.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.anchoredPosition = new Vector2(0, -80);
        titleRect.sizeDelta = new Vector2(0, 80);

        // ========== 右上角关闭按钮 ==========
        CreateCloseButton();

        // ========== 左侧玩家列表 ==========
        _playerListContainer = new GameObject("PlayerListContainer");
        _playerListContainer.transform.SetParent(_panel.transform);
        var playerListRect = _playerListContainer.AddComponent<RectTransform>();
        playerListRect.anchorMin = new Vector2(0.05f, 0.20f);
        playerListRect.anchorMax = new Vector2(0.30f, 0.85f);
        playerListRect.sizeDelta = Vector2.zero;
        playerListRect.anchoredPosition = Vector2.zero;

        var playerListLayout = _playerListContainer.AddComponent<VerticalLayoutGroup>();
        playerListLayout.childAlignment = TextAnchor.UpperLeft;
        playerListLayout.spacing = 24; // ✅ 放大间距到24
        playerListLayout.padding = new RectOffset(20, 20, 20, 20); // ✅ 放大内边距到20
        playerListLayout.childControlHeight = false;
        playerListLayout.childControlWidth = true;
        playerListLayout.childForceExpandHeight = false;
        playerListLayout.childForceExpandWidth = true;

        // ========== 右侧地图/天气信息面板 ==========
        var infoPanel = new GameObject("InfoPanel");
        infoPanel.transform.SetParent(_panel.transform);
        var infoPanelRect = infoPanel.AddComponent<RectTransform>();
        infoPanelRect.anchorMin = new Vector2(0.70f, 0.30f);
        infoPanelRect.anchorMax = new Vector2(0.95f, 0.85f);
        infoPanelRect.sizeDelta = Vector2.zero;
        infoPanelRect.anchoredPosition = Vector2.zero;

        // 添加半透明背景
        var infoBg = infoPanel.AddComponent<Image>();
        infoBg.color = new Color(0.1f, 0.1f, 0.1f, 0.6f);

        var infoLayout = infoPanel.AddComponent<VerticalLayoutGroup>();
        infoLayout.childAlignment = TextAnchor.UpperCenter;
        infoLayout.spacing = 20;
        infoLayout.padding = new RectOffset(20, 20, 20, 20);
        infoLayout.childControlHeight = false;
        infoLayout.childControlWidth = true;
        infoLayout.childForceExpandHeight = false;
        infoLayout.childForceExpandWidth = true;

        // 地图信息
        _mapInfoText = CreateSimpleText("MapInfo", infoPanel.transform, 28, FontStyles.Bold);
        _mapInfoText.text = "地图: 加载中...";
        _mapInfoText.alignment = TextAlignmentOptions.Center;
        _mapInfoText.color = new Color(0.9f, 0.9f, 0.5f, 1f);
        var mapRect = _mapInfoText.GetComponent<RectTransform>();
        mapRect.sizeDelta = new Vector2(0, 40);

        // 游戏时间
        _timeInfoText = CreateSimpleText("TimeInfo", infoPanel.transform, 24, FontStyles.Normal);
        _timeInfoText.text = "时间: --:--";
        _timeInfoText.alignment = TextAlignmentOptions.Center;
        _timeInfoText.color = new Color(0.7f, 0.9f, 1f, 1f);
        var timeRect = _timeInfoText.GetComponent<RectTransform>();
        timeRect.sizeDelta = new Vector2(0, 35);

        // 天气信息
        _weatherInfoText = CreateSimpleText(
            "WeatherInfo",
            infoPanel.transform,
            24,
            FontStyles.Normal
        );
        _weatherInfoText.text = "天气: 未知";
        _weatherInfoText.alignment = TextAlignmentOptions.Center;
        _weatherInfoText.color = new Color(0.7f, 0.9f, 1f, 1f);
        var weatherRect = _weatherInfoText.GetComponent<RectTransform>();
        weatherRect.sizeDelta = new Vector2(0, 35);

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

        // 同步状态文本容器
        var textContainer = new GameObject("TextContainer");
        textContainer.transform.SetParent(bottomSyncPanel.transform);
        var textContainerRect = textContainer.AddComponent<RectTransform>();
        textContainerRect.sizeDelta = new Vector2(500, 80);

        var textVerticalLayout = textContainer.AddComponent<VerticalLayoutGroup>();
        textVerticalLayout.childAlignment = TextAnchor.MiddleLeft;
        textVerticalLayout.spacing = 5;
        textVerticalLayout.childControlHeight = false;
        textVerticalLayout.childControlWidth = true;
        textVerticalLayout.childForceExpandHeight = false;
        textVerticalLayout.childForceExpandWidth = true;

        // 同步状态文本
        _syncStatusText = CreateSimpleText(
            "SyncStatus",
            textContainer.transform,
            28,
            FontStyles.Normal
        );
        _syncStatusText.text = "初始化中...";
        _syncStatusText.alignment = TextAlignmentOptions.Left;
        _syncStatusText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
        var statusRect = _syncStatusText.GetComponent<RectTransform>();
        statusRect.sizeDelta = new Vector2(0, 40);

        // 百分比文本
        _syncPercentText = CreateSimpleText(
            "SyncPercent",
            textContainer.transform,
            32,
            FontStyles.Bold
        );
        _syncPercentText.text = "0%";
        _syncPercentText.alignment = TextAlignmentOptions.Left;
        _syncPercentText.color = new Color(0.5f, 1f, 0.5f, 1f);
        var percentRect = _syncPercentText.GetComponent<RectTransform>();
        percentRect.sizeDelta = new Vector2(0, 40);
    }

    private void Update()
    {
        // 旋转加载动画
        if (_loadingAnimation != null && _loadingAnimation.activeSelf)
        {
            _loadingRotation += 360f * Time.deltaTime; // 每秒旋转360度
            if (_loadingRotation >= 360f)
                _loadingRotation -= 360f;
            _loadingAnimation.transform.rotation = Quaternion.Euler(0, 0, -_loadingRotation);
        }

        // 更新同步进度显示
        UpdateProgressDisplay();

        // 更新地图和天气信息
        UpdateMapAndWeatherInfo();

        // 检查是否所有任务完成
        if (!_allTasksCompleted && _panel != null && _panel.activeSelf)
        {
            CheckAndHideIfComplete();
        }
    }

    private void UpdateProgressDisplay()
    {
        if (_syncStatusText == null || _syncPercentText == null)
            return;
        if (!_panel.activeSelf)
            return;

        try
        {
            int completed = _syncTasks.Count(t => t.Value.IsCompleted);
            int total = _syncTasks.Count;

            if (total == 0)
            {
                _syncStatusText.text = "初始化中...";
                _syncPercentText.text = "0%";
                return;
            }

            // 计算百分比
            float percent = (float)completed / total * 100f;

            // ✅ 启用自动进度增长（达到75%后）
            if (percent >= 75f && !_autoProgressEnabled)
            {
                _autoProgressEnabled = true;
                _autoProgressPercent = percent;
                _lastAutoProgressTime = Time.time;
                Debug.Log($"[SYNC_UI] 启用自动进度增长，当前进度: {percent:F0}%");
            }

            // ✅ 自动进度增长逻辑（每秒+1%）
            if (_autoProgressEnabled)
            {
                float timeSinceLastUpdate = Time.time - _lastAutoProgressTime;
                if (timeSinceLastUpdate >= 1f)
                {
                    _autoProgressPercent += 1f;
                    _lastAutoProgressTime = Time.time;
                    Debug.Log($"[SYNC_UI] 自动进度增长: {_autoProgressPercent:F0}%");
                }

                // 使用自动进度（但不超过100%）
                percent = Mathf.Min(_autoProgressPercent, 100f);

                // ✅ 达到100%时立即关闭
                if (percent >= 100f)
                {
                    Debug.Log("[SYNC_UI] 进度达到100%，立即关闭UI");
                    _syncStatusText.text = "加载完成！";
                    Close(); // 立即关闭，无淡出效果
                    return;
                }
            }

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
            if (_autoProgressEnabled)
            {
                _syncStatusText.text = "即将完成...";
            }
            else
            {
                var currentTask = _syncTasks.FirstOrDefault(t => !t.Value.IsCompleted);
                if (currentTask.Value != null)
                {
                    string detail = string.IsNullOrEmpty(currentTask.Value.Details)
                        ? ""
                        : $" - {currentTask.Value.Details}";
                    _syncStatusText.text = $"{currentTask.Value.Name}{detail}";
                }
                else
                {
                    _syncStatusText.text = "同步完成！";
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SYNC_UI] 更新进度显示失败: {ex.Message}");
        }
    }

    private void UpdateMapAndWeatherInfo()
    {
        if (_mapInfoText == null || _timeInfoText == null || _weatherInfoText == null)
            return;
        if (!_panel.activeSelf)
            return;

        try
        {
            // 更新地图信息
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            _mapInfoText.text = $"地图: {GetMapDisplayName(currentScene.name)}";

            // 更新游戏时间 - 使用 GameClock
            try
            {
                var day = GameClock.Day;
                var timeOfDay = GameClock.TimeOfDay;
                var hours = timeOfDay.Hours;
                var minutes = timeOfDay.Minutes;
                _timeInfoText.text = $"时间: 第{day}天 {hours:D2}:{minutes:D2}";
            }
            catch
            {
                _timeInfoText.text = "时间: --:--";
            }

            // 更新天气信息 - 使用 TimeOfDayController
            try
            {
                if (TimeOfDayController.Instance != null)
                {
                    var currentWeather = TimeOfDayController.Instance.CurrentWeather;
                    var weatherName = TimeOfDayController.GetWeatherNameByWeather(currentWeather);
                    _weatherInfoText.text = $"天气: {weatherName}";
                }
                else
                {
                    _weatherInfoText.text = "天气: 未知";
                }
            }
            catch
            {
                _weatherInfoText.text = "天气: 未知";
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SYNC_UI] 更新地图天气信息失败: {ex.Message}");
        }
    }

    private string GetMapDisplayName(string sceneName)
    {
        // 🌏 使用统一的场景名称映射工具
        // 首先尝试从场景名称提取场景ID
        string sceneId = ExtractSceneId(sceneName);

        // 使用 SceneNameMapper 获取中文名称
        return Utils.SceneNameMapper.GetDisplayName(sceneId);
    }

    /// <summary>
    /// 从Unity场景名称提取场景ID
    /// 例如: "Base_Scenev2" -> "Base", "Level_Factory_Main" -> "Factory"
    /// </summary>
    private string ExtractSceneId(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
            return sceneName;

        // 处理 "Base_Scenev2" 格式
        if (sceneName.StartsWith("Base"))
            return "Base";

        // 处理 "Level_XXX_Main" 格式
        if (sceneName.StartsWith("Level_"))
        {
            var parts = sceneName.Split('_');
            if (parts.Length >= 2)
                return parts[1]; // 返回 "Factory", "Custom" 等
        }

        // 直接返回原始名称
        return sceneName;
    }

    private void CheckAndHideIfComplete()
    {
        if (_syncTasks.Count == 0)
            return;

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
            var modPath = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location
            );
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

    private TMP_Text CreateSimpleText(
        string name,
        Transform parent,
        int fontSize,
        FontStyles fontStyle
    )
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

    /// <summary>
    /// 创建右上角关闭按钮
    /// </summary>
    private void CreateCloseButton()
    {
        var closeButtonObj = new GameObject("CloseButton");
        closeButtonObj.transform.SetParent(_panel.transform);

        var buttonRect = closeButtonObj.AddComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(1, 1);
        buttonRect.anchorMax = new Vector2(1, 1);
        buttonRect.pivot = new Vector2(1, 1);
        buttonRect.anchoredPosition = new Vector2(-30, -30); // 距离右上角 30 像素
        buttonRect.sizeDelta = new Vector2(60, 60); // 60x60 的按钮

        var button = closeButtonObj.AddComponent<Button>();
        var buttonImage = closeButtonObj.AddComponent<Image>();
        buttonImage.color = new Color(0.8f, 0.2f, 0.2f, 0.8f); // 半透明红色

        // 直接在按钮上添加文字
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(closeButtonObj.transform);
        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.anchoredPosition = Vector2.zero;

        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = "X";
        text.fontSize = 40;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;

        // 按钮点击事件
        button.onClick.AddListener(() =>
        {
            Debug.Log("[SYNC_UI] 用户点击关闭按钮");
            Close(); // 立即关闭，无淡出效果
        });

        // 悬停效果
        var colors = button.colors;
        colors.normalColor = new Color(0.8f, 0.2f, 0.2f, 0.8f);
        colors.highlightedColor = new Color(1f, 0.3f, 0.3f, 1f);
        colors.pressedColor = new Color(0.6f, 0.1f, 0.1f, 1f);
        button.colors = colors;
    }

    // ========== 以下方法已移除，不再需要 ==========

    private GameObject CreatePlayerAvatar(ulong steamId = 0)
    {
        var avatarObj = new GameObject("Avatar");
        var rectTransform = avatarObj.AddComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(96, 96); // ✅ 放大到96x96

        var image = avatarObj.AddComponent<Image>();
        image.color = new Color(0.3f, 0.3f, 0.3f, 1f); // 默认灰色背景

        // ✅ Steam P2P 模式下加载真实的 Steam 头像
        bool isSteamMode = NetService.Instance?.TransportMode == NetworkTransportMode.SteamP2P;
        bool canLoadSteamAvatar = steamId > 0 && isSteamMode && SteamManager.Initialized;

        if (canLoadSteamAvatar)
        {
            Debug.Log($"[SYNC_UI] 准备加载Steam头像: SteamID={steamId}");

            // 先检查缓存
            if (_steamAvatarCache.TryGetValue(steamId, out var cachedSprite))
            {
                image.sprite = cachedSprite;
                image.color = Color.white;
                Debug.Log($"[SYNC_UI] ✓ 使用缓存的Steam头像: {steamId}");
            }
            else
            {
                // 异步加载Steam头像
                Debug.Log($"[SYNC_UI] 开始异步加载Steam头像: {steamId}");
                StartCoroutine(LoadSteamAvatar(new CSteamID(steamId), image));
            }
        }
        else
        {
            // 直连模式或无SteamID：创建默认头像
            if (!isSteamMode)
            {
                Debug.Log($"[SYNC_UI] 直连模式，使用默认头像");
            }
            else if (!SteamManager.Initialized)
            {
                Debug.LogWarning($"[SYNC_UI] Steam未初始化，使用默认头像");
            }
            else
            {
                Debug.LogWarning($"[SYNC_UI] SteamID无效({steamId})，使用默认头像");
            }

            // 创建默认头像（圆形 + 简单人像）
            var texture = new Texture2D(64, 64);
            var pixels = new Color[64 * 64];

            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float dx = x - 32f;
                    float dy = y - 32f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    // 圆形背景
                    if (distance < 30)
                    {
                        pixels[y * 64 + x] = new Color(0.3f, 0.5f, 0.7f, 1f);

                        // 简单人像：头部圆形
                        float headDx = dx;
                        float headDy = dy + 8;
                        float headDist = Mathf.Sqrt(headDx * headDx + headDy * headDy);
                        if (headDist < 10)
                        {
                            pixels[y * 64 + x] = new Color(0.9f, 0.9f, 0.9f, 1f);
                        }

                        // 身体
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
        }

        return avatarObj;
    }

    private IEnumerator LoadSteamAvatar(CSteamID steamId, Image targetImage)
    {
        if (targetImage == null)
        {
            Debug.LogWarning($"[SYNC_UI] targetImage为null: {steamId}");
            yield break;
        }

        Debug.Log($"[SYNC_UI] 开始加载Steam头像: {steamId}");

        // 尝试获取头像句柄（大头像）
        int avatarHandle = SteamFriends.GetLargeFriendAvatar(steamId);

        // 如果大头像不可用，尝试中等头像
        if (avatarHandle == -1)
        {
            avatarHandle = SteamFriends.GetMediumFriendAvatar(steamId);
        }

        // 等待头像加载完成
        int maxRetries = 10;
        int retryCount = 0;
        while (avatarHandle == -1 && retryCount < maxRetries)
        {
            yield return new WaitForSeconds(0.1f);
            avatarHandle = SteamFriends.GetLargeFriendAvatar(steamId);
            if (avatarHandle == -1)
            {
                avatarHandle = SteamFriends.GetMediumFriendAvatar(steamId);
            }
            retryCount++;
        }

        if (avatarHandle <= 0)
        {
            Debug.LogWarning($"[SYNC_UI] 无法获取Steam头像句柄: {steamId}");
            yield break;
        }

        if (avatarHandle > 0)
        {
            uint width,
                height;
            if (SteamUtils.GetImageSize(avatarHandle, out width, out height))
            {
                Debug.Log($"[SYNC_UI] Steam头像尺寸: {width}x{height}");
                if (width > 0 && height > 0)
                {
                    byte[] imageData = new byte[width * height * 4];
                    if (SteamUtils.GetImageRGBA(avatarHandle, imageData, (int)(width * height * 4)))
                    {
                        Texture2D texture = new Texture2D(
                            (int)width,
                            (int)height,
                            TextureFormat.RGBA32,
                            false
                        );
                        texture.LoadRawTextureData(imageData);
                        texture.Apply();

                        // 翻转图像（Steam图像是上下颠倒的）
                        for (int y = 0; y < height / 2; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                Color temp = texture.GetPixel(x, y);
                                texture.SetPixel(x, y, texture.GetPixel(x, (int)height - 1 - y));
                                texture.SetPixel(x, (int)height - 1 - y, temp);
                            }
                        }
                        texture.Apply();

                        Sprite sprite = Sprite.Create(
                            texture,
                            new Rect(0, 0, width, height),
                            new Vector2(0.5f, 0.5f)
                        );
                        _steamAvatarCache[steamId.m_SteamID] = sprite;

                        if (targetImage != null)
                        {
                            targetImage.sprite = sprite;
                            targetImage.color = Color.white;
                        }
                        Debug.Log($"[SYNC_UI] Steam头像加载成功并缓存: {steamId}");
                    }
                    else
                    {
                        Debug.LogWarning($"[SYNC_UI] GetImageRGBA 失败: {steamId}");
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[SYNC_UI] GetImageSize 失败: {steamId}");
            }
        }
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

        // ✅ Steam P2P 模式下获取 Steam 用户名
        string displayName = playerName;
        ulong steamId = 0;
        bool isSteamMode = NetService.Instance?.TransportMode == NetworkTransportMode.SteamP2P;

        if (isSteamMode && SteamManager.Initialized)
        {
            try
            {
                // 从EndPoint解析SteamID
                steamId = GetSteamIdFromEndPoint(playerEndPoint);

                if (steamId > 0)
                {
                    // 获取 Steam 用户名
                    var cSteamId = new CSteamID(steamId);
                    string steamUsername = "Unknown";

                    // 尝试从 LobbyManager 缓存获取
                    var lobbyManager = SteamLobbyManager.Instance;
                    if (lobbyManager != null && lobbyManager.IsInLobby)
                    {
                        steamUsername = lobbyManager.GetCachedMemberName(cSteamId);

                        // 缓存未命中，回退到 Steam API
                        if (string.IsNullOrEmpty(steamUsername))
                        {
                            steamUsername = SteamFriends.GetFriendPersonaName(cSteamId);
                            if (string.IsNullOrEmpty(steamUsername) || steamUsername == "[unknown]")
                            {
                                steamUsername =
                                    $"Player_{steamId.ToString().Substring(Math.Max(0, steamId.ToString().Length - 4))}";
                            }
                        }

                        // 判断是否是主机
                        var lobbyOwner = SteamMatchmaking.GetLobbyOwner(
                            lobbyManager.CurrentLobbyId
                        );
                        bool isHost = (steamId == lobbyOwner.m_SteamID);
                        string prefix = isHost ? "HOST" : "CLIENT";
                        displayName = $"{prefix}_{steamUsername}";
                    }
                    else
                    {
                        // 不在 Lobby 中，尝试直接获取（本地玩家）
                        if (steamId == SteamUser.GetSteamID().m_SteamID)
                        {
                            steamUsername = SteamFriends.GetPersonaName();
                            bool isHost = NetService.Instance?.IsServer ?? false;
                            string prefix = isHost ? "HOST" : "CLIENT";
                            displayName = $"{prefix}_{steamUsername}";
                        }
                        else
                        {
                            steamUsername = SteamFriends.GetFriendPersonaName(cSteamId);
                            if (
                                !string.IsNullOrEmpty(steamUsername)
                                && steamUsername != "[unknown]"
                            )
                            {
                                displayName = $"CLIENT_{steamUsername}";
                            }
                        }
                    }

                    Debug.Log($"[SYNC_UI] Steam 玩家: {displayName} (SteamID: {steamId})");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SYNC_UI] 获取Steam用户名失败: {ex.Message}");
            }
        }

        // 添加头像
        var avatar = CreatePlayerAvatar(steamId);
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

    private ulong GetSteamIdFromEndPoint(string endPoint)
    {
        if (string.IsNullOrEmpty(endPoint))
            return 0;

        // Steam P2P模式下，EndPoint包含SteamID信息
        // 尝试从SceneNet的玩家列表中获取SteamID
        try
        {
            var mod = ModBehaviourF.Instance;
            if (mod != null && mod.playerStatuses != null)
            {
                foreach (var kv in mod.playerStatuses)
                {
                    var status = kv.Value;
                    if (status?.EndPoint == endPoint)
                    {
                        // 尝试从ClientReportedId解析SteamID
                        if (!string.IsNullOrEmpty(status.ClientReportedId))
                        {
                            // ClientReportedId格式可能是 "Client:steamid64"
                            if (status.ClientReportedId.Contains(":"))
                            {
                                var parts = status.ClientReportedId.Split(':');
                                if (parts.Length > 1 && ulong.TryParse(parts[1], out ulong steamId))
                                {
                                    return steamId;
                                }
                            }
                            // 或者直接是steamid64
                            if (ulong.TryParse(status.ClientReportedId, out ulong directSteamId))
                            {
                                return directSteamId;
                            }
                        }
                        break;
                    }
                }
            }

            // 如果从玩家状态中找不到，尝试从EndPoint直接解析
            if (endPoint.Contains(":"))
            {
                var parts = endPoint.Split(':');
                foreach (var part in parts)
                {
                    if (ulong.TryParse(part, out ulong steamId) && steamId > 76561197960265728) // 最小的SteamID64
                    {
                        return steamId;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SYNC_UI] GetSteamIdFromEndPoint异常: {ex.Message}");
        }

        return 0;
    }

    /// <summary>
    /// 显示同步等待界面
    /// </summary>
    public void Show()
    {
        Debug.Log(
            $"[SYNC_UI] Show() 被调用，_panel={(_panel != null ? "存在" : "null")}, _canvas={(_canvas != null ? "存在" : "null")}"
        );

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

        // ✅ 重置自动进度状态
        _autoProgressEnabled = false;
        _autoProgressPercent = 0f;
        _lastAutoProgressTime = 0f;

        // ✅ 启用角色无敌
        EnableCharacterInvincibility();

        Debug.Log("[SYNC_UI] 显示同步等待界面完成");
    }

    /// <summary>
    /// 隐藏同步等待界面（带淡出效果）
    /// </summary>
    public void Hide()
    {
        // ✅ 强制解除角色无敌
        DisableCharacterInvincibility();

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
    /// 立即关闭同步等待界面（无淡出效果）
    /// </summary>
    public void Close()
    {
        // ✅ 强制解除角色无敌
        DisableCharacterInvincibility();

        // 停止淡出协程（如果有）
        if (_fadeOutCoroutine != null)
        {
            StopCoroutine(_fadeOutCoroutine);
            _fadeOutCoroutine = null;
        }

        // 立即隐藏
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
        }

        if (_panel != null)
        {
            _panel.SetActive(false);
        }

        if (_canvas != null)
        {
            _canvas.enabled = false;
        }

        Debug.Log("[SYNC_UI] 立即关闭同步等待界面");
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
                Details = "",
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
            Debug.Log(
                $"[SYNC_UI] 任务状态更新: {task.Name} - {(isCompleted ? "完成" : "进行中")} {details}"
            );
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
        if (_playerListContainer == null)
            return;

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
                var emptyText = CreateSimpleText(
                    "Empty",
                    _playerListContainer.transform,
                    20,
                    FontStyles.Italic
                );
                emptyText.text = "正在获取玩家列表...";
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

    /// <summary>
    /// 启用角色无敌状态
    /// </summary>
    private void EnableCharacterInvincibility()
    {
        try
        {
            var character = CharacterMainControl.Main;
            if (character == null)
            {
                Debug.LogWarning("[SYNC_UI] 无法启用无敌：角色为空");
                return;
            }

            var health = character.Health;
            if (health == null)
            {
                Debug.LogWarning("[SYNC_UI] 无法启用无敌：Health组件为空");
                return;
            }

            // 如果已经在追踪其他Health对象，先恢复
            if (_invincibilityTargetHealth != null && _invincibilityTargetHealth != health)
            {
                DisableCharacterInvincibility();
            }

            // 保存原始状态
            if (_originalInvincibleState == null)
            {
                _originalInvincibleState = health.Invincible;
                Debug.Log($"[SYNC_UI] 保存原始无敌状态: {_originalInvincibleState.Value}");
            }

            // 启用无敌
            if (!health.Invincible)
            {
                health.SetInvincible(true);
                Debug.Log("[SYNC_UI] ✅ 已启用角色无敌");
            }
            else
            {
                Debug.Log("[SYNC_UI] 角色已处于无敌状态");
            }

            _invincibilityTargetHealth = health;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SYNC_UI] 启用无敌失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// 解除角色无敌状态（恢复原始状态）
    /// </summary>
    private void DisableCharacterInvincibility()
    {
        try
        {
            if (_invincibilityTargetHealth != null && _originalInvincibleState != null)
            {
                _invincibilityTargetHealth.SetInvincible(_originalInvincibleState.Value);
                Debug.Log($"[SYNC_UI] ✅ 已恢复角色无敌状态为: {_originalInvincibleState.Value}");
            }
            else if (_invincibilityTargetHealth == null && _originalInvincibleState != null)
            {
                Debug.LogWarning("[SYNC_UI] Health对象已失效，无法恢复无敌状态");
            }

            _invincibilityTargetHealth = null;
            _originalInvincibleState = null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SYNC_UI] 解除无敌失败: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
