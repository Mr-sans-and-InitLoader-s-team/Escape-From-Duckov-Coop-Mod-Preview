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
/// MModUI的布局构建器，负责创建和管理UI布局结构
/// </summary>
public class MModUILayoutBuilder
{
    private readonly MModUI _ui;
    private readonly MModUIComponents _components;

    public MModUILayoutBuilder(MModUI ui, MModUIComponents components)
    {
        _ui = ui;
        _components = components;
    }

    /// <summary>
    /// 创建主面板布局
    /// </summary>
    public void BuildMainPanel(Transform canvasTransform)
    {
        // 主面板容器 - 0.8倍缩放 (1400*0.8=1120, 980*0.8=784)
        _components.MainPanel = _ui.CreateModernPanel("MainPanel", canvasTransform, new Vector2(1010, 784), new Vector2(260, 90));
        _ui.MakeDraggable(_components.MainPanel);

        var mainLayout = _components.MainPanel.AddComponent<VerticalLayoutGroup>();
        mainLayout.padding = new RectOffset(0, 0, 0, 0);
        mainLayout.spacing = 0;
        mainLayout.childForceExpandHeight = false;
        mainLayout.childControlHeight = false;

        // 创建顶部标题栏
        BuildTitleBar(_components.MainPanel.transform);

        // 创建主内容区域（横向分栏）
        var contentArea = BuildContentArea(_components.MainPanel.transform);

        // 创建左侧服务器列表区域
        BuildLeftListContainer(contentArea.transform);

        // 创建右侧连接面板区域
        BuildRightPanel(contentArea.transform);
    }

    /// <summary>
    /// 创建顶部标题栏
    /// </summary>
    private void BuildTitleBar(Transform parent)
    {
        var titleBar = _ui.CreateTitleBar(parent);
        _ui.CreateText("Title", titleBar.transform, CoopLocalization.Get("ui.window.title"), 24, MModUI.ModernColors.TextPrimary, TextAlignmentOptions.Left, FontStyles.Bold);

        var spacer = new GameObject("Spacer");
        spacer.transform.SetParent(titleBar.transform, false);
        var spacerLayout = spacer.AddComponent<LayoutElement>();
        spacerLayout.flexibleWidth = 1;

        // 模式指示器
        var indicatorObj = new GameObject("Indicator");
        indicatorObj.transform.SetParent(titleBar.transform, false);
        var indicatorLayout = indicatorObj.AddComponent<LayoutElement>();
        indicatorLayout.preferredWidth = 12;
        indicatorLayout.preferredHeight = 12;
        _components.ModeIndicator = indicatorObj.AddComponent<Image>();
        _components.ModeIndicator.color = MModUI.ModernColors.Success;

        _components.ModeText = _ui.CreateText("ModeText", titleBar.transform,
            _ui.IsServer ? CoopLocalization.Get("ui.mode.server") : CoopLocalization.Get("ui.mode.client"), 16, MModUI.ModernColors.TextSecondary, TextAlignmentOptions.Left);

        _ui.CreateIconButton("CloseBtn", titleBar.transform, "x", () =>
        {
            _ui.showUI = false;
            _ui.StartCoroutine(_ui.AnimatePanel(_components.MainPanel, false));
        }, 36, MModUI.ModernColors.Error);
    }

    /// <summary>
    /// 创建主内容区域
    /// </summary>
    private GameObject BuildContentArea(Transform parent)
    {
        var contentArea = new GameObject("ContentArea");
        contentArea.transform.SetParent(parent, false);
        var contentLayout = contentArea.AddComponent<HorizontalLayoutGroup>();
        contentLayout.padding = new RectOffset(20, 20, 15, 15);
        contentLayout.spacing = 20;
        contentLayout.childForceExpandWidth = false;
        contentLayout.childForceExpandHeight = true;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = false;

        var contentLayoutElement = contentArea.AddComponent<LayoutElement>();
        contentLayoutElement.flexibleWidth = 1;
        contentLayoutElement.flexibleHeight = 1;

        return contentArea;
    }

    /// <summary>
    /// 创建左侧服务器列表容器
    /// </summary>
    private void BuildLeftListContainer(Transform parent)
    {
        var leftListContainer = new GameObject("LeftListContainer");
        leftListContainer.transform.SetParent(parent, false);
        var leftListLayout = leftListContainer.AddComponent<VerticalLayoutGroup>();
        leftListLayout.padding = new RectOffset(0, 0, 0, 0);
        leftListLayout.spacing = 0;
        leftListLayout.childForceExpandHeight = false;
        leftListLayout.childControlHeight = true;

        var leftListLayoutElement = leftListContainer.AddComponent<LayoutElement>();
        leftListLayoutElement.preferredWidth = 520;  // 650 * 0.8 = 520
        leftListLayoutElement.minHeight = 480;  // 600 * 0.8 = 480

        // Direct模式：局域网服务器列表
        BuildDirectServerListArea(leftListContainer.transform);

        // Steam模式：Steam房间列表
        BuildSteamServerListArea(leftListContainer.transform);
    }

    /// <summary>
    /// 创建Direct模式的服务器列表区域
    /// </summary>
    private void BuildDirectServerListArea(Transform parent)
    {
        _components.DirectServerListArea = new GameObject("DirectServerListArea");
        _components.DirectServerListArea.transform.SetParent(parent, false);
        var directListLayout = _components.DirectServerListArea.AddComponent<VerticalLayoutGroup>();
        directListLayout.padding = new RectOffset(0, 0, 0, 0);
        directListLayout.spacing = 12;
        directListLayout.childForceExpandHeight = false;
        directListLayout.childControlHeight = true;
        var directListLayoutElement = _components.DirectServerListArea.AddComponent<LayoutElement>();
        directListLayoutElement.flexibleWidth = 1;
        directListLayoutElement.flexibleHeight = 1;

        // 局域网服务器列表标题
        var lanHeader = _ui.CreateModernCard(_components.DirectServerListArea.transform, "LANHeader");
        var lanHeaderLayout = lanHeader.GetComponent<LayoutElement>();
        lanHeaderLayout.preferredHeight = 60;
        lanHeaderLayout.minHeight = 60;
        lanHeaderLayout.flexibleHeight = 0;

        var lanHeaderGroup = _ui.CreateHorizontalGroup(lanHeader.transform, "HeaderGroup");
        lanHeaderGroup.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(0, 0, 0, 0);

        _ui.CreateText("ListTitle", lanHeaderGroup.transform, CoopLocalization.Get("ui.hostList.title"), 20, MModUI.ModernColors.TextPrimary, TextAlignmentOptions.Left, FontStyles.Bold);

        var lanHeaderSpacer = new GameObject("Spacer");
        lanHeaderSpacer.transform.SetParent(lanHeaderGroup.transform, false);
        var lanHeaderSpacerLayout = lanHeaderSpacer.AddComponent<LayoutElement>();
        lanHeaderSpacerLayout.flexibleWidth = 1;

        _ui.CreateModernButton("RefreshBtn", lanHeaderGroup.transform, CoopLocalization.Get("ui.steam.refresh"), () =>
        {
            if (_ui.Service != null && !_ui.IsServer)
                CoopTool.SendBroadcastDiscovery();
        }, 120, MModUI.ModernColors.Primary, 38, 15);

        // 局域网服务器列表滚动视图
        var lanScrollView = _ui.CreateModernScrollView("LANServerListScroll", _components.DirectServerListArea.transform, 535);  // 550 * 0.8 = 440
        var lanScrollLayout = lanScrollView.GetComponent<LayoutElement>();
        lanScrollLayout.flexibleHeight = 1;
        _components.HostListContent = lanScrollView.transform.Find("Viewport/Content");

        // 局域网服务器列表状态栏
        var lanStatusBar = _ui.CreateStatusBar(_components.DirectServerListArea.transform);
        _components.StatusText = _ui.CreateText("Status", lanStatusBar.transform, $"[*] {_ui.status}", 14, MModUI.ModernColors.TextSecondary);

        var lanStatusSpacer = new GameObject("Spacer");
        lanStatusSpacer.transform.SetParent(lanStatusBar.transform, false);
        var lanStatusSpacerLayout = lanStatusSpacer.AddComponent<LayoutElement>();
        lanStatusSpacerLayout.flexibleWidth = 1;

        _ui.CreateText("Hint", lanStatusBar.transform, CoopLocalization.Get("ui.hint.toggleUI", "="), 12, MModUI.ModernColors.TextTertiary, TextAlignmentOptions.Right);
    }

    /// <summary>
    /// 创建Steam模式的服务器列表区域
    /// </summary>
    private void BuildSteamServerListArea(Transform parent)
    {
        _components.SteamServerListArea = new GameObject("SteamServerListArea");
        _components.SteamServerListArea.transform.SetParent(parent, false);
        var steamListLayout = _components.SteamServerListArea.AddComponent<VerticalLayoutGroup>();
        steamListLayout.padding = new RectOffset(0, 0, 0, 0);
        steamListLayout.spacing = 12;
        steamListLayout.childForceExpandHeight = false;
        steamListLayout.childControlHeight = true;
        var steamListLayoutElement = _components.SteamServerListArea.AddComponent<LayoutElement>();
        steamListLayoutElement.flexibleWidth = 1;
        steamListLayoutElement.flexibleHeight = 1;

        _ui.CreateSteamServerListUI(_components.SteamServerListArea.transform, _components);
    }

    /// <summary>
    /// 创建右侧连接面板区域
    /// </summary>
    private void BuildRightPanel(Transform parent)
    {
        // 右侧面板容器
        var rightPanel = new GameObject("RightPanel");
        rightPanel.transform.SetParent(parent, false);

        // 添加垂直布局组件
        var rightLayout = rightPanel.AddComponent<VerticalLayoutGroup>();
        rightLayout.padding = new RectOffset(0, 0, 0, 0);
        rightLayout.spacing = 0;
        rightLayout.childForceExpandHeight = true;
        rightLayout.childForceExpandWidth = true;
        rightLayout.childControlHeight = true;
        rightLayout.childControlWidth = true;

        var rightLayoutElement = rightPanel.AddComponent<LayoutElement>();
        rightLayoutElement.preferredWidth = 320;  // 400 * 0.8 = 320
        rightLayoutElement.flexibleHeight = 1;

        // 创建滚动视图
        var scrollView = _ui.CreateModernScrollView("RightPanelScroll", rightPanel.transform, 660);
        var scrollLayout = scrollView.GetComponent<LayoutElement>();
        scrollLayout.flexibleHeight = 1;
        scrollLayout.flexibleWidth = 1;

        // 获取滚动视图的Content区域
        var scrollContent = scrollView.transform.Find("Viewport/Content");

        // 传输模式选择器
        BuildTransportModeSelector(scrollContent);

        // 模式切换卡片
        BuildModeToggleCard(scrollContent);

        // 服务器信息卡片
        BuildServerInfoCard(scrollContent);

        // Direct模式面板
        BuildDirectModePanel(scrollContent);

        // Steam模式面板
        BuildSteamModePanel(scrollContent);

        // 快捷操作卡片
        BuildActionsCard(scrollContent);
    }

    /// <summary>
    /// 创建传输模式选择器
    /// </summary>
    private void BuildTransportModeSelector(Transform parent)
    {
        var transportCard = _ui.CreateModernCard(parent, "TransportModeCard");
        var transportCardLayout = transportCard.GetComponent<LayoutElement>();
        transportCardLayout.preferredHeight = 95;  // 95 * 0.8 = 76
        transportCardLayout.minHeight = 95;

        _ui.CreateSectionHeader(transportCard.transform, CoopLocalization.Get("ui.transport.label"));

        var transportButtonsRow = _ui.CreateHorizontalGroup(transportCard.transform, "TransportButtons");
        var transportRowLayout = transportButtonsRow.GetComponent<HorizontalLayoutGroup>();
        transportRowLayout.spacing = 10;
        transportRowLayout.padding = new RectOffset(0, 0, 0, 0);

        var directBtn = _ui.CreateModernButton("DirectMode", transportButtonsRow.transform, CoopLocalization.Get("ui.transport.mode.direct"),
            () => _ui.OnTransportModeChanged(NetworkTransportMode.Direct),
            -1, _ui.TransportMode == NetworkTransportMode.Direct ? MModUI.ModernColors.Primary : MModUI.GlassTheme.ButtonBg, 40, 14);

        var steamBtn = _ui.CreateModernButton("SteamMode", transportButtonsRow.transform, CoopLocalization.Get("ui.transport.mode.steam"),
            () => _ui.OnTransportModeChanged(NetworkTransportMode.SteamP2P),
            -1, _ui.TransportMode == NetworkTransportMode.SteamP2P ? MModUI.ModernColors.Primary : MModUI.GlassTheme.ButtonBg, 40, 14);
    }

    /// <summary>
    /// 创建模式切换卡片
    /// </summary>
    private void BuildModeToggleCard(Transform parent)
    {
        var modeCard = _ui.CreateModernCard(parent, "ModeCard");
        var modeCardLayout = modeCard.GetComponent<LayoutElement>();
        modeCardLayout.preferredHeight = 120;  // 120 * 0.8 = 96
        modeCardLayout.minHeight = 120;

        _ui.CreateSectionHeader(modeCard.transform, CoopLocalization.Get("ui.server.management"));
        _components.ModeInfoText = _ui.CreateText("ModeInfo", modeCard.transform,
            _ui.IsServer ? CoopLocalization.Get("ui.server.hint.waiting") : CoopLocalization.Get("ui.client.hint.browse"),
            14, MModUI.ModernColors.TextSecondary);

        _components.ModeToggleButton = _ui.CreateModernButton("ToggleMode", modeCard.transform,
            _ui.IsServer ? CoopLocalization.Get("ui.server.close") : CoopLocalization.Get("ui.server.create"),
            _ui.OnToggleServerMode, -1, _ui.IsServer ? MModUI.ModernColors.Error : MModUI.ModernColors.Success, 45, 17);

        // 保存按钮文本引用
        _components.ModeToggleButtonText = _components.ModeToggleButton.GetComponentInChildren<TextMeshProUGUI>();
    }

    /// <summary>
    /// 创建服务器信息卡片
    /// </summary>
    private void BuildServerInfoCard(Transform parent)
    {
        var serverInfoCard = _ui.CreateModernCard(parent, "ServerInfoCard");
        var serverInfoCardLayout = serverInfoCard.GetComponent<LayoutElement>();
        serverInfoCardLayout.preferredHeight = 140;  // 140 * 0.8 = 112
        serverInfoCardLayout.minHeight = 140;

        _ui.CreateSectionHeader(serverInfoCard.transform, CoopLocalization.Get("ui.server.info"));
        _components.ServerPortText = _ui.CreateInfoRow(serverInfoCard.transform, CoopLocalization.Get("ui.server.port"), $"{_ui.port}");
        _components.ConnectionCountText = _ui.CreateInfoRow(serverInfoCard.transform, CoopLocalization.Get("ui.server.connections"), "0");
        _ui.CreateInfoRow(serverInfoCard.transform, CoopLocalization.Get("ui.playerStatus.latency"), "0 ms");
    }

    /// <summary>
    /// 创建Direct模式面板
    /// </summary>
    private void BuildDirectModePanel(Transform parent)
    {
        _components.DirectModePanel = new GameObject("DirectModePanel");
        _components.DirectModePanel.transform.SetParent(parent, false);
        var directLayout = _components.DirectModePanel.AddComponent<VerticalLayoutGroup>();
        directLayout.spacing = 15;
        directLayout.childForceExpandHeight = false;
        directLayout.childControlHeight = true;
        var directLayoutElement = _components.DirectModePanel.AddComponent<LayoutElement>();
        directLayoutElement.flexibleWidth = 1;

        // 手动连接卡片
        var connectCard = _ui.CreateModernCard(_components.DirectModePanel.transform, "ConnectCard");
        var connectCardLayout = connectCard.GetComponent<LayoutElement>();
        connectCardLayout.preferredHeight = 220;  // 220 * 0.8 = 176
        connectCardLayout.minHeight = 220;

        _ui.CreateSectionHeader(connectCard.transform, CoopLocalization.Get("ui.manualConnect.title"));
        _components.IpInputField = _ui.CreateModernInputField("IPInput", connectCard.transform, CoopLocalization.Get("ui.manualConnect.ip"), _ui.manualIP);
        _components.IpInputField.onValueChanged.AddListener((value) => _ui.manualIP = value);

        _components.PortInputField = _ui.CreateModernInputField("PortInput", connectCard.transform, CoopLocalization.Get("ui.manualConnect.port"), _ui.manualPort);
        _components.PortInputField.onValueChanged.AddListener((value) => _ui.manualPort = value);

        _ui.CreateModernButton("ManualConnect", connectCard.transform, CoopLocalization.Get("ui.manualConnect.button"), _ui.OnManualConnect, -1, MModUI.ModernColors.Primary, 45, 17);

        _ui.CreateText("ConnectHint", connectCard.transform, CoopLocalization.Get("ui.manualConnect.hint"), 12, MModUI.ModernColors.TextTertiary, TextAlignmentOptions.Center);
    }

    /// <summary>
    /// 创建Steam模式面板
    /// </summary>
    private void BuildSteamModePanel(Transform parent)
    {
        _components.SteamModePanel = new GameObject("SteamModePanel");
        _components.SteamModePanel.transform.SetParent(parent, false);
        var steamLayout = _components.SteamModePanel.AddComponent<VerticalLayoutGroup>();
        steamLayout.spacing = 15;
        steamLayout.childForceExpandHeight = false;
        steamLayout.childControlHeight = true;
        var steamLayoutElement = _components.SteamModePanel.AddComponent<LayoutElement>();
        steamLayoutElement.flexibleWidth = 1;

        _ui.CreateSteamControlPanel(_components.SteamModePanel.transform);
    }

    /// <summary>
    /// 创建快捷操作卡片
    /// </summary>
    private void BuildActionsCard(Transform parent)
    {
        var actionsCard = _ui.CreateModernCard(parent, "ActionsCard");
        var actionsCardLayout = actionsCard.GetComponent<LayoutElement>();
        actionsCardLayout.preferredHeight = 170;  // 170 * 0.8 = 136
        actionsCardLayout.minHeight = 170;

        _ui.CreateSectionHeader(actionsCard.transform, CoopLocalization.Get("ui.actions.quickActions"));

        _ui.CreateModernButton("PlayerStatus", actionsCard.transform, CoopLocalization.Get("ui.playerStatus.toggle", _ui.togglePlayerStatusKey), () =>
        {
            _ui.showPlayerStatusWindow = !_ui.showPlayerStatusWindow;
            _ui.StartCoroutine(_ui.AnimatePanel(_components.PlayerStatusPanel, _ui.showPlayerStatusWindow));
        }, -1, MModUI.ModernColors.Info, 40, 15);

        _ui.CreateModernButton("Debug", actionsCard.transform, CoopLocalization.Get("ui.debug.printLootBoxes"), _ui.DebugPrintLootBoxes, -1, MModUI.ModernColors.Warning, 40, 15);
    }
}

