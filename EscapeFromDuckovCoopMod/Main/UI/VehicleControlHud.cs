using System;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public sealed class VehicleControlHud : MonoBehaviour
{
    private const float PanelWidth = 430f;
    private const float PanelHeight = 98f;
    private const float VisibleLeft = 24f;
    private const float BottomOffset = 79f;
    private const float RequestCooldown = 2f;
    private const float SlideInSpeed = 7.5f;
    private const float SlideOutSpeed = 8.5f;

    private GUIStyle _panelStyle;
    private GUIStyle _shadowStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _controllerStyle;
    private GUIStyle _metaStyle;
    private GUIStyle _requesterStyle;
    private GUIStyle _keyStyle;
    private GUIStyle _hintStyle;
    private Texture2D _accentTexture;
    private Texture2D _dividerTexture;
    private float _nextRequestTime;
    private float _slide;
    private bool _hasSnapshot;
    private HudSnapshot _snapshot;

    private NetService Service => NetService.Instance;

    private void Update()
    {
        var hasState = TryGetHudState(out var state);
        if (hasState)
            HandleControlInput(state);

        var shouldShow = hasState && ShouldShowHud(state);
        Animate(shouldShow);

        if (shouldShow)
        {
            UpdateSnapshot(state);
            _hasSnapshot = true;
        }
        else if (_slide <= 0.001f)
        {
            _hasSnapshot = false;
        }
    }

    private void OnGUI()
    {
        if (!_hasSnapshot || _slide <= 0.001f)
            return;

        EnsureStyles();

        var scale = Mathf.Clamp(Mathf.Min(Screen.width / 1920f, Screen.height / 1080f), 0.78f, 1.18f);
        UpdateScaledFonts(scale);

        var width = PanelWidth * scale;
        var height = PanelHeight * scale;
        var eased = EaseOutCubic(Mathf.Clamp01(_slide));
        var hiddenX = -width - 18f * scale;
        var visibleX = VisibleLeft * scale;
        var rect = new Rect(
            Mathf.Lerp(hiddenX, visibleX, eased),
            Screen.height - height - BottomOffset * scale,
            width,
            height);

        var oldColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(_slide * 1.35f));

        GUI.Box(new Rect(rect.x + 5f * scale, rect.y + 6f * scale, rect.width, rect.height), GUIContent.none, _shadowStyle);
        GUI.Box(rect, GUIContent.none, _panelStyle);
        GUI.DrawTexture(new Rect(rect.x, rect.y, 4f * scale, rect.height), _accentTexture);
        GUI.DrawTexture(new Rect(rect.x + 18f * scale, rect.y + 60f * scale, rect.width - 36f * scale, 1f), _dividerTexture);

        GUI.Label(new Rect(rect.x + 18f * scale, rect.y + 12f * scale, 150f * scale, 24f * scale), _snapshot.Title, _titleStyle);
        GUI.Label(new Rect(rect.x + 156f * scale, rect.y + 12f * scale, rect.width - 174f * scale, 24f * scale), _snapshot.Controller, _controllerStyle);

        GUI.Label(new Rect(rect.x + 18f * scale, rect.y + 38f * scale, 126f * scale, 20f * scale), _snapshot.Riders, _metaStyle);
        if (!string.IsNullOrEmpty(_snapshot.Requester))
            GUI.Label(new Rect(rect.x + 150f * scale, rect.y + 38f * scale, rect.width - 168f * scale, 20f * scale), _snapshot.Requester, _requesterStyle);

        var keyRect = new Rect(rect.x + 18f * scale, rect.y + 68f * scale, 50f * scale, 22f * scale);
        GUI.Box(keyRect, _snapshot.Key, _keyStyle);
        GUI.Label(new Rect(rect.x + 78f * scale, rect.y + 68f * scale, rect.width - 96f * scale, 22f * scale), _snapshot.Action, _hintStyle);

        GUI.color = oldColor;
    }

    private void HandleControlInput(HudState state)
    {
        if (!Input.GetKeyDown(KeyCode.F2))
            return;

        if (MModUI.Instance != null && MModUI.Instance.IsChatTyping())
            return;

        if (!string.IsNullOrEmpty(state.PendingRequester) &&
            string.Equals(state.AuthorityId, state.LocalId, StringComparison.Ordinal) &&
            !string.Equals(state.PendingRequester, state.LocalId, StringComparison.Ordinal))
        {
            RPCVehicle.ApproveVehicleControl(state.VehicleId, state.PendingRequester);
            MModUI.ShowTip(CoopLocalization.Get("vehicle.control.tip.approved", RPCVehicle.ResolvePlayerDisplayName(state.PendingRequester)), 3f);
            return;
        }

        if (string.Equals(state.AuthorityId, state.LocalId, StringComparison.Ordinal))
            return;

        if (Time.unscaledTime < _nextRequestTime)
            return;

        _nextRequestTime = Time.unscaledTime + RequestCooldown;
        RPCVehicle.RequestVehicleControl(state.VehicleId);
        MModUI.ShowTip(CoopLocalization.Get("vehicle.control.tip.requested"), 3f);
    }

    private bool TryGetHudState(out HudState state)
    {
        state = default;

        var service = Service;
        var vehicleStatus = SendLocalVehicleStatus.Instance;
        if (service == null || vehicleStatus == null || !service.networkStarted)
            return false;

        if (!vehicleStatus.TryGetLocalVehicleInfo(out var vehicleId, out _))
            return false;

        var localId = service.GetPlayerId(null) ?? string.Empty;
        var authorityId = vehicleStatus.GetVehicleAuthority(vehicleId) ?? string.Empty;
        var pendingRequester = vehicleStatus.GetPendingAuthorityRequester(vehicleId) ?? string.Empty;
        var riderCount = RPCPlayer.CountRemoteRidersOnVehicle(vehicleId);
        if (!RPCPlayer.IsPrimaryVehicleRider(localId, vehicleId))
            riderCount++;

        state = new HudState(vehicleId, localId, authorityId, pendingRequester, Mathf.Max(1, riderCount));
        return true;
    }

    private static bool ShouldShowHud(HudState state)
    {
        return state.RiderCount >= 2 || !string.IsNullOrEmpty(state.PendingRequester);
    }

    private void Animate(bool shouldShow)
    {
        var target = shouldShow ? 1f : 0f;
        var speed = shouldShow ? SlideInSpeed : SlideOutSpeed;
        _slide = Mathf.MoveTowards(_slide, target, Time.unscaledDeltaTime * speed);
    }

    private void UpdateSnapshot(HudState state)
    {
        var authorityName = string.IsNullOrEmpty(state.AuthorityId)
            ? CoopLocalization.Get("vehicle.control.unassigned")
            : RPCVehicle.ResolvePlayerDisplayName(state.AuthorityId);

        _snapshot.Title = CoopLocalization.Get("vehicle.control.title");
        _snapshot.Controller = CoopLocalization.Get("vehicle.control.current", authorityName);
        _snapshot.Riders = CoopLocalization.Get("vehicle.control.riders", state.RiderCount);
        _snapshot.Requester = string.IsNullOrEmpty(state.PendingRequester)
            ? string.Empty
            : CoopLocalization.Get("vehicle.control.requester", RPCVehicle.ResolvePlayerDisplayName(state.PendingRequester));
        _snapshot.Key = CoopLocalization.Get("vehicle.control.key");
        _snapshot.Action = BuildActionText(state.LocalId, state.AuthorityId, state.PendingRequester);
    }

    private static string BuildActionText(string localId, string authorityId, string pendingRequester)
    {
        if (!string.IsNullOrEmpty(pendingRequester) &&
            string.Equals(authorityId, localId, StringComparison.Ordinal) &&
            !string.Equals(pendingRequester, localId, StringComparison.Ordinal))
        {
            return CoopLocalization.Get("vehicle.control.action.approve");
        }

        if (string.Equals(authorityId, localId, StringComparison.Ordinal))
            return CoopLocalization.Get("vehicle.control.action.controlling");

        if (!string.IsNullOrEmpty(pendingRequester) &&
            string.Equals(pendingRequester, localId, StringComparison.Ordinal))
        {
            return CoopLocalization.Get("vehicle.control.action.pending");
        }

        return CoopLocalization.Get("vehicle.control.action.request");
    }

    private void EnsureStyles()
    {
        if (_panelStyle != null)
            return;

        _panelStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(16, 16, 10, 10)
        };
        _panelStyle.normal.background = MakeTex(new Color(0.055f, 0.062f, 0.072f, 0.92f));

        _shadowStyle = new GUIStyle(GUI.skin.box);
        _shadowStyle.normal.background = MakeTex(new Color(0f, 0f, 0f, 0.34f));

        _titleStyle = CreateLabelStyle(TextAnchor.MiddleLeft, 16, FontStyle.Bold, new Color(1f, 1f, 1f, 0.96f));
        _controllerStyle = CreateLabelStyle(TextAnchor.MiddleRight, 14, FontStyle.Bold, new Color(0.90f, 0.75f, 0.35f, 1f));
        _metaStyle = CreateLabelStyle(TextAnchor.MiddleLeft, 13, FontStyle.Normal, new Color(1f, 1f, 1f, 0.78f));
        _requesterStyle = CreateLabelStyle(TextAnchor.MiddleRight, 13, FontStyle.Normal, new Color(0.68f, 0.82f, 1f, 0.92f));
        _hintStyle = CreateLabelStyle(TextAnchor.MiddleLeft, 14, FontStyle.Bold, new Color(1f, 1f, 1f, 0.94f));

        _keyStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };
        _keyStyle.normal.background = MakeTex(new Color(0.90f, 0.75f, 0.35f, 0.96f));
        _keyStyle.normal.textColor = new Color(0.08f, 0.075f, 0.055f, 1f);

        _accentTexture = MakeTex(new Color(0.90f, 0.75f, 0.35f, 0.96f));
        _dividerTexture = MakeTex(new Color(1f, 1f, 1f, 0.14f));
    }

    private void UpdateScaledFonts(float scale)
    {
        _titleStyle.fontSize = Mathf.RoundToInt(16f * scale);
        _controllerStyle.fontSize = Mathf.RoundToInt(14f * scale);
        _metaStyle.fontSize = Mathf.RoundToInt(13f * scale);
        _requesterStyle.fontSize = Mathf.RoundToInt(13f * scale);
        _keyStyle.fontSize = Mathf.RoundToInt(14f * scale);
        _hintStyle.fontSize = Mathf.RoundToInt(14f * scale);
    }

    private static GUIStyle CreateLabelStyle(TextAnchor alignment, int fontSize, FontStyle fontStyle, Color textColor)
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = alignment,
            fontSize = fontSize,
            fontStyle = fontStyle,
            clipping = TextClipping.Clip
        };
        style.normal.textColor = textColor;
        return style;
    }

    private static float EaseOutCubic(float t)
    {
        t = 1f - Mathf.Clamp01(t);
        return 1f - t * t * t;
    }

    private static Texture2D MakeTex(Color color)
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }

    private struct HudState
    {
        public readonly int VehicleId;
        public readonly string LocalId;
        public readonly string AuthorityId;
        public readonly string PendingRequester;
        public readonly int RiderCount;

        public HudState(int vehicleId, string localId, string authorityId, string pendingRequester, int riderCount)
        {
            VehicleId = vehicleId;
            LocalId = localId;
            AuthorityId = authorityId;
            PendingRequester = pendingRequester;
            RiderCount = riderCount;
        }
    }

    private struct HudSnapshot
    {
        public string Title;
        public string Controller;
        public string Riders;
        public string Requester;
        public string Key;
        public string Action;
    }
}
