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
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

[DisallowMultipleComponent]
public class HealthBarNameDisplay : MonoBehaviour
{
    [SerializeField] private string _playerId;
    [SerializeField] private string _fallbackName;

    private Health _health;
    private HealthBar _bar;
    private GameObject _container;
    private TextMeshProUGUI _nameText;

    private void OnEnable()
    {
        // no-op; configuration drives UI creation
        if (_bar.target != _health)
        {
            _container.SetActive(false);
        }
        if (_bar.target != _health)
        {
            _container.SetActive(false);
        }
     
    }

    private void OnDisable()
    {
        //TeardownUi();
        _container.SetActive(true);
    }

    private void OnDestroy()
    {
       // TeardownUi();
    }

    public void Configure(string playerId, string fallbackName, Health health, HealthBar healthBar)
    {
        _playerId = playerId;
        _fallbackName = fallbackName;
        _health = health;
        _bar = healthBar;

        BuildUi();
    }

    private void BuildUi()
    {
        if (!isActiveAndEnabled)
            return;

        if (_bar == null || _health == null || _bar.target != _health)
            return;

        var barTransform = _bar.transform as RectTransform;
        _container = new GameObject("NameDisplay");
        _container.transform.SetParent(_bar.transform, false);

        var rect = _container.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0, (float)(barTransform?.rect.height - 50f));

        var layout = _container.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 1f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;

        var nameObj = new GameObject("Name");
        nameObj.transform.SetParent(_container.transform, false);
        _nameText = nameObj.AddComponent<TextMeshProUGUI>();
        _nameText.fontSize = 24f;
        _nameText.color = Color.white;
        _nameText.alignment = TextAlignmentOptions.Center;
        _nameText.text = ResolveDisplayName();
       

        var layoutElement = _nameText.gameObject.AddComponent<LayoutElement>();
        layoutElement.minWidth = 40f;
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
    }

    private string ResolveDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(_fallbackName))
            return _fallbackName;

        return string.IsNullOrWhiteSpace(_playerId) ? string.Empty : _playerId;
    }
}
