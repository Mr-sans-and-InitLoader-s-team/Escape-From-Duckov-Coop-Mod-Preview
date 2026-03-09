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

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(InputManager), "Update")]
internal static class Patch_InputManager_Update_ChatBlock
{
    private static bool Prefix()
    {
        // 当聊天输入框激活时，屏蔽其他 UI 呼出和操作
        var ui = MModUI.Instance;
        return ui == null || !ui.IsChatTyping();
    }
}
