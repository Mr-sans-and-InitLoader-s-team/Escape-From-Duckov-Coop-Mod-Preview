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

public static class PackFlag
{
    // ——flags 打包/解包——
    public static byte PackFlags(bool hasCurtain, bool useLoc, bool notifyEvac, bool saveToFile)
    {
        byte f = 0;
        if (hasCurtain) f |= 1 << 0;
        if (useLoc) f |= 1 << 1;
        if (notifyEvac) f |= 1 << 2;
        if (saveToFile) f |= 1 << 3;
        return f;
    }

    public static void UnpackFlags(byte f, out bool hasCurtain, out bool useLoc, out bool notifyEvac, out bool saveToFile)
    {
        hasCurtain = (f & (1 << 0)) != 0;
        useLoc = (f & (1 << 1)) != 0;
        notifyEvac = (f & (1 << 2)) != 0;
        saveToFile = (f & (1 << 3)) != 0;
    }
}