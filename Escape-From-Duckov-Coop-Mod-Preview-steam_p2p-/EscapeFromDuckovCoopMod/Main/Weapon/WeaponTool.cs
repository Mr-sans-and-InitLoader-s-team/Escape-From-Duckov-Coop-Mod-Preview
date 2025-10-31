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

public class WeaponTool
{
    private void TryStartVisualRecoil(ItemAgent_Gun gun)
    {
        if (!gun) return;
        try
        {
            Traverse.Create(gun).Method("StartVisualRecoil").GetValue();
            return;
        }
        catch
        {
        }

        try
        {
            // 兜底：等价于 StartVisualRecoil() 内部把 _recoilBack=true
            Traverse.Create(gun).Field<bool>("_recoilBack").Value = true;
        }
        catch
        {
        }
    }
}