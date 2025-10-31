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

using System.Collections;
using System.Reflection;

namespace EscapeFromDuckovCoopMod;

[DisallowMultipleComponent]
public class AutoRequestHealthBar : MonoBehaviour
{
    private static readonly FieldInfo FI_character =
        typeof(Health).GetField("characterCached", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo FI_hasChar =
        typeof(Health).GetField("hasCharacter", BindingFlags.NonPublic | BindingFlags.Instance);

    [SerializeField] private int attempts = 30; // 最长重试次数（总计约 3 秒）
    [SerializeField] private float interval = 0.1f; // 每次重试间隔

    private void OnEnable()
    {
        StartCoroutine(Bootstrap());
    }

    private IEnumerator Bootstrap()
    {
        // 等一帧，确保层级/场景/UI 管线基本就绪??
        yield return null;
        yield return null;

        var cmc = GetComponent<CharacterMainControl>();
        var h = GetComponentInChildren<Health>(true);
        if (!h) yield break;

        // 绑定 Health⇄Character（远端克隆常见问题）
        try
        {
            FI_character?.SetValue(h, cmc);
            FI_hasChar?.SetValue(h, true);
        }
        catch
        {
        }

        for (var i = 0; i < attempts; i++)
        {
            if (!h) yield break;

            try
            {
                h.showHealthBar = true;
            }
            catch
            {
            }

            try
            {
                h.RequestHealthBar();
            }
            catch
            {
            }

            try
            {
                h.OnMaxHealthChange?.Invoke(h);
            }
            catch
            {
            }

            try
            {
                h.OnHealthChange?.Invoke(h);
            }
            catch
            {
            }

            yield return new WaitForSeconds(interval);
        }
    }
}