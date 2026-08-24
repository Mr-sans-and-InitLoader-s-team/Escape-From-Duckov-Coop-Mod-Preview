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

using Duckov.UI;
using System;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[DisallowMultipleComponent]
public class HealthBarNameDisplay : MonoBehaviour
{
    [SerializeField] private string _playerId;
    [SerializeField] private string _steamName;

    private Health _health;
    private HealthBar _bar;
    private GameObject _container;
    private TextMeshProUGUI _nameText;

    private static readonly MethodInfo GetActiveHealthBarMethod =
        AccessTools.DeclaredMethod(typeof(HealthBarManager), "GetActiveHealthBar", new[] { typeof(Health) });

    private void OnEnable()
    {
        RefreshUi();
    }

    private void OnDestroy()
    {
        TeardownUi();
    }

    public void Configure(string playerId, string steamName, Health health, HealthBar healthBar)
    {
        _playerId = playerId;
        _steamName = SanitizeName(steamName);
        _health = health;
        _bar = healthBar;

        RefreshUi();
    }

    public static void TryApplyToHealthBar(HealthBar healthBar, Health health)
    {
        if (!healthBar || !health)
            return;

        if (!TryResolvePlayerSteamName(health, out var playerId, out var steamName))
        {
            var existing = healthBar.GetComponent<HealthBarNameDisplay>();
            if (existing)
                existing.Configure(null, null, null, null);
            return;
        }

        var display = healthBar.GetComponent<HealthBarNameDisplay>() ??
                      healthBar.gameObject.AddComponent<HealthBarNameDisplay>();
        display.Configure(playerId, steamName, health, healthBar);
    }

    public static void TryRefreshRemoteCharacter(GameObject remoteObject, string playerId, string steamName)
    {
        if (!remoteObject || string.IsNullOrWhiteSpace(steamName))
            return;

        var health = remoteObject.GetComponentInChildren<Health>(true);
        if (!health)
            return;

        var healthBar = GetActiveHealthBar(health);
        if (!healthBar)
            return;

        var display = healthBar.GetComponent<HealthBarNameDisplay>() ??
                      healthBar.gameObject.AddComponent<HealthBarNameDisplay>();
        display.Configure(playerId, steamName, health, healthBar);
    }

    private void RefreshUi()
    {
        var displayName = ResolveDisplayName();
        if (string.IsNullOrEmpty(displayName) || !_bar || !_health || _bar.target != _health)
        {
            TeardownUi();
            return;
        }

        if (_container == null)
            BuildUi();

        if (_nameText != null)
            _nameText.text = displayName;

        if (_container != null && !_container.activeSelf)
            _container.SetActive(true);
    }

    private void BuildUi()
    {
        _container = new GameObject("SteamNameDisplay");
        _container.transform.SetParent(_bar.transform, false);

        var rect = _container.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 6f);
        rect.sizeDelta = new Vector2(260f, 28f);

        var nameObj = new GameObject("Name");
        nameObj.transform.SetParent(_container.transform, false);
        var nameRect = nameObj.AddComponent<RectTransform>();
        nameRect.anchorMin = Vector2.zero;
        nameRect.anchorMax = Vector2.one;
        nameRect.offsetMin = Vector2.zero;
        nameRect.offsetMax = Vector2.zero;

        _nameText = nameObj.AddComponent<TextMeshProUGUI>();
        _nameText.raycastTarget = false;
        _nameText.fontSize = 20f;
        _nameText.color = Color.white;
        _nameText.alignment = TextAlignmentOptions.Center;
        _nameText.enableWordWrapping = false;
        _nameText.overflowMode = TextOverflowModes.Overflow;
    }

    private void TeardownUi()
    {
        if (_container != null)
        {
            Destroy(_container);
            _container = null;
        }

        _nameText = null;
        _bar = null;
        _health = null;
        _playerId = null;
        _steamName = null;
    }

    private string ResolveDisplayName()
    {
        return SanitizeName(_steamName);
    }

    private static HealthBar GetActiveHealthBar(Health health)
    {
        if (!health || HealthBarManager.Instance == null || GetActiveHealthBarMethod == null)
            return null;

        try
        {
            return GetActiveHealthBarMethod.Invoke(HealthBarManager.Instance, new object[] { health }) as HealthBar;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryResolvePlayerSteamName(Health health, out string playerId, out string steamName)
    {
        playerId = null;
        steamName = null;

        var service = NetService.Instance;
        if (service == null || !service.networkStarted || !health)
            return false;

        var cmc = health.TryGetCharacter();
        if (cmc != null && cmc == CharacterMainControl.Main)
            return false;

        if (service.IsServer)
        {
            foreach (var kvp in service.remoteCharacters)
            {
                var remote = kvp.Value;
                if (!remote || !health.transform.IsChildOf(remote.transform))
                    continue;

                service.playerStatuses.TryGetValue(kvp.Key, out var status);
                playerId = service.GetPlayerId(kvp.Key);
                steamName = service.ResolvePeerSteamName(kvp.Key, status?.SteamName);
                if (status != null && !string.IsNullOrEmpty(steamName))
                    status.SteamName = steamName;
                return !string.IsNullOrEmpty(steamName);
            }
        }
        else
        {
            foreach (var kvp in service.clientRemoteCharacters)
            {
                var remote = kvp.Value;
                if (!remote || !health.transform.IsChildOf(remote.transform))
                    continue;

                playerId = kvp.Key;
                service.clientPlayerStatuses.TryGetValue(playerId, out var status);
                steamName = SanitizeName(status?.SteamName);
                return !string.IsNullOrEmpty(steamName);
            }
        }

        return false;
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        name = name.Trim();
        return string.Equals(name, "[unknown]", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : name;
    }
}
