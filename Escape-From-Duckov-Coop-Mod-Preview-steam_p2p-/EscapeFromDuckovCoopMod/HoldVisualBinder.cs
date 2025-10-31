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

namespace EscapeFromDuckovCoopMod;

internal static class HoldVisualBinder
{
    public static void EnsureHeldVisuals(CharacterMainControl cmc)
    {
        if (!cmc) return;
        var model = cmc.characterModel;
        if (!model) return;

        try
        {
            // 近战：手持插槽
            var melee =
                (model.MeleeWeaponSocket ? model.MeleeWeaponSocket.GetComponentInChildren<ItemAgent_MeleeWeapon>(true) : null)
                ?? model.GetComponentInChildren<ItemAgent_MeleeWeapon>(true);
            if (melee && melee.Holder == null) melee.SetHolder(cmc);

            // 右手枪/主手
            var rGun =
                (model.RightHandSocket ? model.RightHandSocket.GetComponentInChildren<ItemAgent_Gun>(true) : null)
                ?? model.GetComponentInChildren<ItemAgent_Gun>(true);
            if (rGun && rGun.Holder == null) rGun.SetHolder(cmc);

            // 左手（有些道具/双持/灯具）
            var lGun = model.LefthandSocket ? model.LefthandSocket.GetComponentInChildren<ItemAgent_Gun>(true) : null;
            if (lGun && lGun.Holder == null) lGun.SetHolder(cmc);
        }
        catch
        {
            /* 仅做视觉兜底，静默失败即可 */
        }
    }
}