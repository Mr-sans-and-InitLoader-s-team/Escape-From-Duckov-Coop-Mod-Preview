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

using Duckov;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[DisallowMultipleComponent]
public sealed class AISyncTracker : MonoBehaviour
{
    private AISyncService _service;
    private AISyncEntry _entry;
    private AICharacterController _controller;
    private Health _health;

    public void Initialize(AISyncService service, AICharacterController controller, AISyncEntry entry)
    {
        _service = service;
        _controller = controller;
        _entry = entry;

        if (!_health)
        {
            if (controller && controller.CharacterMainControl)
                _health = controller.CharacterMainControl.Health;
            if (!_health)
                _health = GetComponentInChildren<Health>(true);
        }

        if (_health != null)
        {
            _health.OnHealthChange.AddListener(OnHealthChange);
            _health.OnDeadEvent.AddListener(OnDeadEvent);
        }
    }

    private void Awake()
    {
        if (!_health)
            _health = GetComponentInChildren<Health>(true);
    }

    private void OnDestroy()
    {
        if (_health != null)
        {
            _health.OnHealthChange.RemoveListener(OnHealthChange);
            _health.OnDeadEvent.RemoveListener(OnDeadEvent);
        }

        if (_entry != null)
            _service?.Server_HandleTrackerDestroyed(_entry, _controller);

        _service = null;
        _entry = null;
        _controller = null;
        _health = null;
    }

    private void OnHealthChange(Health health)
    {
        if (_entry == null || _service == null || health == null) return;
        _service.Server_HandleHealthChanged(
            _entry,
            SafeGetMax(health),
            SafeGetCurrent(health),
            SafeGetBodyArmor(health),
            SafeGetHeadArmor(health));
    }

    private void OnDeadEvent(DamageInfo info)
    {
        if (_entry == null || _service == null) return;
        _service.Server_HandleDeath(
            _entry,
            SafeGetMax(_health),
            SafeGetCurrent(_health),
            SafeGetBodyArmor(_health),
            SafeGetHeadArmor(_health));
    }

    private static float SafeGetMax(Health health)
    {
        if (!health) return 0f;
        try { return health.MaxHealth; }
        catch { return 0f; }
    }

    private static float SafeGetCurrent(Health health)
    {
        if (!health) return 0f;
        try { return health.CurrentHealth; }
        catch { return 0f; }
    }

    private static float SafeGetBodyArmor(Health health)
    {
        if (!health) return 0f;
        try { return health.BodyArmor; }
        catch { return 0f; }
    }

    private static float SafeGetHeadArmor(Health health)
    {
        if (!health) return 0f;
        try { return health.HeadArmor; }
        catch { return 0f; }
    }
}
