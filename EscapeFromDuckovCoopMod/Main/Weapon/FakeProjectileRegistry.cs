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

using System;
using System.Collections.Generic;

namespace EscapeFromDuckovCoopMod;

public static class FakeProjectileRegistry
{
    private static readonly HashSet<Projectile> _fakes = new();

    [ThreadStatic]
    private static Projectile _current;

    public static void Register(Projectile proj)
    {
        if (proj == null) return;
        _fakes.Add(proj);
    }

    public static void Unregister(Projectile proj)
    {
        if (proj == null) return;
        _fakes.Remove(proj);
        if (_current == proj) _current = null;
    }

    public static bool IsFake(Projectile proj)
    {
        return proj != null && _fakes.Contains(proj);
    }

    public static void BeginFrame(Projectile proj)
    {
        _current = IsFake(proj) ? proj : null;
    }

    public static void EndFrame(Projectile proj)
    {
        if (_current == proj)
            _current = null;
    }

    public static bool IsCurrentFake => _current != null && IsFake(_current);
}
