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

public sealed class NetAiVisibilityGuard : MonoBehaviour
{
    private bool _inited;
    private Light[] _lights;
    private ParticleSystem[] _particles;
    private Renderer[] _renderers;

    private void EnsureCache()
    {
        if (_inited) return;
        _renderers = GetComponentsInChildren<Renderer>(true);
        _lights = GetComponentsInChildren<Light>(true);
        _particles = GetComponentsInChildren<ParticleSystem>(true);
        _inited = true;
    }

    public void SetVisible(bool v)
    {
        EnsureCache();
        if (_renderers != null)
            foreach (var r in _renderers)
                if (r)
                    r.enabled = v;
        if (_lights != null)
            foreach (var l in _lights)
                if (l)
                    l.enabled = v;
        if (_particles != null)
            foreach (var ps in _particles)
            {
                if (!ps) continue;
                var em = ps.emission;
                em.enabled = v;
            }
        //这些可能是无意义的开关hhhhhh
    }
}