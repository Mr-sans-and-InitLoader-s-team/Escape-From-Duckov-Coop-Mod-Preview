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
        private static VersionOverlayTMP _instance;
        private TextMeshProUGUI _label;
        private GradientFlowTMP _flow;

        private string _lastName;
        private string _lastVer;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                enabled = false;
                Destroy(this); // The shared parent also contains the network managers.
                return;
            }
            _instance = this;

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

            if (!force && name == _lastName && ver == _lastVer)
                return;

            _lastName = name;
            _lastVer = ver;
            if (_flow != null) _flow.enabled = true;

            if (_label != null)
            {
                _label.color = Color.white;
                _label.text = (name ?? "") + (string.IsNullOrEmpty(ver) ? "" : $" v{ver}");
                _flow.coloredCharacterCount = (name ?? "").Length;
                _root.sizeDelta = _label.GetPreferredValues(_label.text);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_canvas != null) Destroy(_canvas.gameObject);
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

            // One text mesh owns both name and version, so the two cannot be
            // laid out on top of each other during a canvas/layout rebuild.
            _label = CreateTMP(canvasGO.transform, "VersionText", baseFontSize, new Color32(255, 255, 255, 255));
            _label.alignment = TextAlignmentOptions.TopRight;
            _label.richText = false;
            _root = _label.rectTransform;
            _root.anchorMin = _root.anchorMax = Vector2.one;
            _root.pivot = Vector2.one;
            _root.anchoredPosition = new Vector2(-basePadding, -basePadding);

            _flow = _label.gameObject.AddComponent<GradientFlowTMP>();
            _flow.speed = gradientSpeed;
            _flow.pingPong = pingPong;
            _flow.unscaledTime = true;
            _flow.updateInterval = 0.05f;
            _flow.gradient = MakeRainbowGradient();
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

        public int coloredCharacterCount = -1;

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

            if (_text.havePropertiesChanged) _text.ForceMeshUpdate();
            var ti = _text.textInfo;
            if (ti == null || ti.characterCount == 0) return;

            // 计算整段文字 X 范围，用于归一化（保证跨字符连续平滑）
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            var colorCount = coloredCharacterCount < 0 ? ti.characterCount : Math.Min(coloredCharacterCount, ti.characterCount);

            for (int i = 0; i < colorCount; i++)
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
                        cols[vi + k] = i < colorCount ? (Color32)gradient.Evaluate(tt) : new Color32(255, 255, 255, 255);
                    }
                }

            }
            // Only upload colors; never resubmit mesh geometry from unused or
            // previously generated material slots after a TMP layout rebuild.
            _text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
        }
    }
}
