// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspicuous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;
//或许是永远沉睡的伤害统计，又或者现在的你看到了它把它唤醒，完善？......       2025/12/19 by:initloader
public class DamageStatsUI : MonoBehaviour
{
    private const float PANEL_PADDING = 14f;
    private const float ROW_HEIGHT = 56f;

    private bool _visible = true;
    private GUIStyle _panelStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _rowNameStyle;
    private GUIStyle _rowValueStyle;
    private Texture2D _panelBackground;

    private NetService Service => NetService.Instance;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    //private void Update()
    //{
    //    if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
    //        _visible = !_visible;
    //}

    //private void OnGUI()
    //{
    //    if (!_visible)
    //        return;

    //    if (_panelStyle == null)
    //        BuildStyles();

    //    var tracker = DamageStatsTracker.Instance;
    //    if (tracker == null)
    //        return;

    //    var stats = tracker.GetSortedStats();
    //    if (stats.Count == 0)
    //        return;

    //    float panelWidth = Mathf.Min(420f, Screen.width * 0.4f);
    //    float panelHeight = PANEL_PADDING * 2f + 28f + stats.Count * ROW_HEIGHT;
    //    float x = Screen.width - panelWidth - 20f;
    //    float y = (Screen.height - panelHeight) * 0.5f;

    //    var area = new Rect(x, y, panelWidth, panelHeight);
    //    GUI.color = new Color(1f, 1f, 1f, 0.82f);
    //    GUI.Box(area, GUIContent.none, _panelStyle);
    //    GUI.color = Color.white;

    //    var headerRect = new Rect(area.x + PANEL_PADDING, area.y + PANEL_PADDING, panelWidth - PANEL_PADDING * 2f, 24f);
    //    GUI.Label(headerRect, "伤害统计", _titleStyle);

    //    for (int i = 0; i < stats.Count; i++)
    //    {
    //        var entry = stats[i];
    //        float top = area.y + PANEL_PADDING + 28f + i * ROW_HEIGHT;
    //        DrawRow(entry.Key, entry.Value, new Rect(area.x + PANEL_PADDING, top, panelWidth - PANEL_PADDING * 2f, ROW_HEIGHT - 6f), i + 1);
    //    }
    //}

    private void DrawRow(string playerId, float damage, Rect rect, int rank)
    {
        var status = FindStatus(playerId);
        var name = ResolveDisplayName(status, playerId);
        var damageText = damage.ToString("F0");

        var rankRect = new Rect(rect.x, rect.y + 4f, 28f, rect.height);
        GUI.Label(rankRect, $"#{rank}", _rowNameStyle);

        float nameX = rankRect.xMax + 10f;
        var nameRect = new Rect(nameX, rect.y + 8f, rect.width - (nameX - rect.x) - 80f, 20f);
        GUI.Label(nameRect, name, _rowNameStyle);

        var damageRect = new Rect(rect.xMax - 72f, rect.y + 6f, 68f, rect.height);
        GUI.Label(damageRect, damageText, _rowValueStyle);
    }

    private void BuildStyles()
    {
        _panelBackground = new Texture2D(1, 1);
        _panelBackground.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.65f));
        _panelBackground.Apply();

        _panelStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = _panelBackground },
            border = new RectOffset(8, 8, 8, 8)
        };

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white }
        };

        _rowNameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.9f, 0.93f, 1f, 1f) }
        };

        _rowValueStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.86f, 0.48f, 1f) }
        };
    }

    private PlayerStatus FindStatus(string playerId)
    {
        var service = Service;
        if (service == null || string.IsNullOrEmpty(playerId))
            return null;

        if (service.IsSelfId(playerId) && service.localPlayerStatus != null)
            return service.localPlayerStatus;

        if (service.playerStatuses != null)
        {
            foreach (var kv in service.playerStatuses)
            {
                var st = kv.Value;
                if (st != null && !string.IsNullOrEmpty(st.EndPoint) && st.EndPoint.Equals(playerId, StringComparison.OrdinalIgnoreCase))
                    return st;
            }
        }

        if (service.clientPlayerStatuses != null && service.clientPlayerStatuses.TryGetValue(playerId, out var clientStatus))
            return clientStatus;

        return null;
    }

    private string ResolveDisplayName(PlayerStatus status, string fallback)
    {
        if (status != null)
        {
            if (!string.IsNullOrEmpty(status.PlayerName))
                return status.PlayerName;
        }

        return fallback;
    }

    private void OnDestroy()
    {
        if (_panelBackground != null)
            Destroy(_panelBackground);
    }
}
