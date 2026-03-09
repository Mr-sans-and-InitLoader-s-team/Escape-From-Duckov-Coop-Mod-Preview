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

using HarmonyLib;
using UnityEngine.InputSystem;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(CharacterInputControl), "Update")]
internal static class Patch_CharacterInputControl_Update_ChatBlock
{
    private static bool _forcedDisable;

    private static bool Prefix()
    {
        var typing = MModUI.Instance != null && MModUI.Instance.IsChatTyping();
        var playerInput = GameManager.MainPlayerInput;

        if (typing)
        {
            if (playerInput != null && playerInput.enabled)
            {
                playerInput.enabled = false;
                _forcedDisable = true;
            }

            return false; // skip original Update while typing
        }

        if (_forcedDisable)
        {
            if (playerInput != null && !playerInput.enabled)
                playerInput.enabled = true;
            _forcedDisable = false;
        }

        return true;
    }
}
