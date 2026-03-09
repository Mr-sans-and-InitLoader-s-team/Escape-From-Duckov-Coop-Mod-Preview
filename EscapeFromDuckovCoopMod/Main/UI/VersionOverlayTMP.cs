using EscapeFromDuckovCoopMod.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod
{

    public class VersionOverlayTMP : MonoBehaviour
    {
        // 参考分辨率 & 基础参数（和你原先 IMGUI 一致）
        public Vector2 referenceResolution = new Vector2(1920f, 1080f);
        public float basePadding = 14f;
        public float baseFontSize = 17f;

        // 渐变流动速度：越小越慢（0.02~0.08 比较舒服）
        [Range(0f, 1f)] public float gradientSpeed = 0.6f;

        // 是否来回流动（true = 左->右->左；false = 循环流动）
        public bool pingPong = false;

        // 可选：热键开关显示
        public KeyCode toggleKey = KeyCode.None; 
        public bool visible = true;

        private Canvas _canvas;
        private RectTransform _root;
        private TextMeshProUGUI _nameText;
        private TextMeshProUGUI _verText;
        private GradientFlowTMP _flow;

        private string _lastName;
        private string _lastVer;

        public static readonly Color32 NewYearRed = new Color32(255, 40, 40, 255);
        private bool _lastFestivalFlag;
        void Awake()
        {
            // 单例防重复（跨场景）
            var existing = FindObjectOfType<VersionOverlayTMP>();
            if (existing != null && existing != this)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
            BuildUI();
            RefreshText(force: true);
            ApplyVisible();
        }

        void Update()
        {
            if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
            {
                visible = !visible;
                ApplyVisible();
            }

            RefreshText(force: false);
        }

        private void ApplyVisible()
        {
            if (_canvas != null) _canvas.enabled = visible;
        }


        private void RefreshText(bool force)
        {
            string name = BuildInfo.Name;
            string ver = BuildInfo.ModVersion;

            bool festival = CnyUtil.IsChuxiOrSpringFestivalToday();

            if (!force && name == _lastName && ver == _lastVer && festival == _lastFestivalFlag)
                return;

            _lastName = name;
            _lastVer = ver;
            _lastFestivalFlag = festival;

            if (festival)
            {
                // ✅ 节日模式：加前缀 + 全红 + 关闭渐变脚本（不然它会每帧改颜色）
                if (_flow != null) _flow.enabled = false;

                if (_nameText != null)
                {
                    _nameText.color = NewYearRed;
                    _nameText.text = $"新年快乐! - {name ?? ""}";
                }

                if (_verText != null)
                {
                    _verText.color = NewYearRed;
                    _verText.text = string.IsNullOrEmpty(ver) ? "" : $" v{ver}";
                }
            }
            else
            {
                // ✅ 平时模式：恢复渐变 + 正常显示
                if (_flow != null) _flow.enabled = true;

                if (_nameText != null)
                {
                    // 颜色交给 _flow 去染（这里给个默认白色无所谓）
                    _nameText.color = Color.white;
                    _nameText.text = name ?? "";
                }

                if (_verText != null)
                {
                    _verText.color = Color.white;
                    _verText.text = string.IsNullOrEmpty(ver) ? "" : $" v{ver}";
                }
            }
        }

        private void BuildUI()
        {
            var canvasGO = new GameObject("VersionOverlayCanvas");
            canvasGO.transform.SetParent(transform, false);

            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = short.MaxValue;

            // ✅ 关键：让运行时 Canvas 变成全屏 RectTransform（否则默认在中间一小块）
            var canvasRT = canvasGO.GetComponent<RectTransform>();
            canvasRT.anchorMin = Vector2.zero;
            canvasRT.anchorMax = Vector2.one;
            canvasRT.pivot = new Vector2(0.5f, 0.5f);
            canvasRT.anchoredPosition = Vector2.zero;
            canvasRT.sizeDelta = Vector2.zero;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var raycaster = canvasGO.AddComponent<GraphicRaycaster>();
            raycaster.enabled = false;

            // 右上角容器
            var rootGO = new GameObject("VersionRoot");
            rootGO.transform.SetParent(canvasGO.transform, false);

            _root = rootGO.AddComponent<RectTransform>();
            _root.anchorMin = new Vector2(1f, 1f);
            _root.anchorMax = new Vector2(1f, 1f);
            _root.pivot = new Vector2(1f, 1f);
            _root.anchoredPosition = new Vector2(-basePadding, -basePadding);

            // 横向排版： [Name][ vX.Y.Z]
            var hlg = rootGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.UpperRight;
            hlg.spacing = 2f;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            var fitter = rootGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Name（渐变流动）
            _nameText = CreateTMP(rootGO.transform, "NameText", baseFontSize, new Color32(255, 255, 255, 255));
            _nameText.alignment = TextAlignmentOptions.TopRight;

            _flow = _nameText.gameObject.AddComponent<GradientFlowTMP>();
            _flow.speed = gradientSpeed;
            _flow.pingPong = pingPong;
            _flow.unscaledTime = true;
            _flow.updateInterval = 0.05f; // 20fps 更新颜色足够顺滑，且更省性能

            // 你想要的彩虹渐变（可自行改色）
            _flow.gradient = MakeRainbowGradient();

            // Version（纯白）
            _verText = CreateTMP(rootGO.transform, "VersionText", baseFontSize, new Color32(255, 255, 255, 255));
            _verText.alignment = TextAlignmentOptions.TopRight;
        }

        private static TextMeshProUGUI CreateTMP(Transform parent, string name, float fontSize, Color32 color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.text = "";

            return tmp;
        }

        private static Gradient MakeRainbowGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
            new GradientColorKey(new Color32(255, 0, 0, 255), 0f),    // 红
            new GradientColorKey(new Color32(255, 128, 0, 255), 0.2f), // 橙
            new GradientColorKey(new Color32(255, 255, 0, 255), 0.4f), // 黄
            new GradientColorKey(new Color32(0, 255, 0, 255), 0.6f),   // 绿
            new GradientColorKey(new Color32(0, 128, 255, 255), 0.8f), // 蓝
            new GradientColorKey(new Color32(180, 0, 255, 255), 1f),   // 紫
                },
                new[]
                {
            new GradientAlphaKey(1f, 0f),
            new GradientAlphaKey(1f, 1f),
                }
            );
            return g;
        }
    }

    /// <summary>
    /// 真·平滑渐变：直接改 TMP 的顶点颜色（每个字符四个顶点按 X 位置取 Gradient）
    /// 并支持缓慢流动（offset 随时间变化）。
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class GradientFlowTMP : MonoBehaviour
    {
        public Gradient gradient;
        [Range(0f, 1f)] public float speed = 0.05f;     // 越小越慢
        public bool pingPong = true;                    // true=往返流动
        public bool unscaledTime = true;                // 不受 TimeScale 影响
        public float updateInterval = 0.05f;            // 多久更新一次颜色（省性能）

        private TMP_Text _text;
        private float _timer;

        void Awake()
        {
            _text = GetComponent<TMP_Text>();
        }

        void LateUpdate()
        {
            if (_text == null || gradient == null) return;

            _timer += Time.unscaledDeltaTime;
            if (updateInterval > 0f && _timer < updateInterval)
                return;
            _timer = 0f;

            _text.ForceMeshUpdate();
            var ti = _text.textInfo;
            if (ti == null || ti.characterCount == 0) return;

            // 计算整段文字 X 范围，用于归一化（保证跨字符连续平滑）
            float minX = float.MaxValue;
            float maxX = float.MinValue;

            for (int i = 0; i < ti.characterCount; i++)
            {
                var ch = ti.characterInfo[i];
                if (!ch.isVisible) continue;

                int mi = ch.materialReferenceIndex;
                int vi = ch.vertexIndex;
                var v = ti.meshInfo[mi].vertices;

                minX = Mathf.Min(minX, v[vi + 0].x, v[vi + 1].x, v[vi + 2].x, v[vi + 3].x);
                maxX = Mathf.Max(maxX, v[vi + 0].x, v[vi + 1].x, v[vi + 2].x, v[vi + 3].x);
            }

            float width = Mathf.Max(0.0001f, maxX - minX);

            float t = unscaledTime ? Time.unscaledTime : Time.time;
            float phase = t * Mathf.Max(0.0001f, speed);
            float offset01 = pingPong ? Mathf.PingPong(phase, 1f) : Mathf.Repeat(phase, 1f);

            // 按顶点 x 位置取渐变色 => 每个字内部也是平滑过渡
            for (int m = 0; m < ti.meshInfo.Length; m++)
            {
                var meshInfo = ti.meshInfo[m];
                var verts = meshInfo.vertices;
                var cols = meshInfo.colors32;

                for (int i = 0; i < ti.characterCount; i++)
                {
                    var ch = ti.characterInfo[i];
                    if (!ch.isVisible || ch.materialReferenceIndex != m) continue;

                    int vi = ch.vertexIndex;

                    // 4 个顶点分别计算颜色
                    for (int k = 0; k < 4; k++)
                    {
                        float nx = (verts[vi + k].x - minX) / width;      // 0..1
                        float tt = Mathf.Repeat(nx + offset01, 1f);       // 加时间偏移 -> 流动
                        cols[vi + k] = (Color32)gradient.Evaluate(tt);
                    }
                }

                meshInfo.mesh.colors32 = cols;
                _text.UpdateGeometry(meshInfo.mesh, m);
            }
        }
    }
}
