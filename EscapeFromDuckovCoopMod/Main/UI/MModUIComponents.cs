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

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

/// <summary>
/// 存储MModUI所有UI组件引用的容器类
/// </summary>
public class MModUIComponents
{
    // 主面板
    public GameObject MainPanel;
    public GameObject PlayerStatusPanel;
    public GameObject VotePanel;
    public GameObject SpectatorPanel;

    // 输入字段
    public TMP_InputField IpInputField;
    public TMP_InputField PortInputField;
    public TMP_InputField JsonInputField;  // 🆕 JSON 消息输入框

    // 文本组件
    public TMP_Text StatusText;
    public TMP_Text SteamStatusText;  // Steam模式专用状态文本
    public TMP_Text ServerPortText;
    public TMP_Text ConnectionCountText;
    public TMP_Text ModeToggleButtonText;
    public TMP_Text ModeInfoText;
    public TMP_Text ModeText;
    public TMP_Text SteamMaxPlayersText;

    // 图像组件
    public Image ModeIndicator;

    // 容器Transform
    public Transform HostListContent;
    public Transform PlayerListContent;
    public Transform SteamLobbyListContent;

    // 按钮
    public Button ModeToggleButton;
    public Button SteamCreateLeaveButton;
    public TMP_Text SteamCreateLeaveButtonText;

    // 模式面板
    public GameObject DirectModePanel;
    public GameObject SteamModePanel;

    // 左侧列表区域
    public GameObject DirectServerListArea;
    public GameObject SteamServerListArea;
}

