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

using System.Reflection;
using Duckov.Buffs;
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

internal class BuffLateBinder : MonoBehaviour
{
    private Buff _buff;
    private bool _done;
    private FieldInfo _fiEffects;

    private void Update()
    {
        if (_done || _buff == null)
        {
            Destroy(this);
            return;
        }

        // 取 CharacterItem（Buff 里已有安全 getter）
        var cmc = (_buff ? AccessTools.Field(typeof(Buff), "master")?.GetValue(_buff) as CharacterBuffManager : null)?.Master;
        var item = cmc ? cmc.CharacterItem : null;
        if (item == null || item.transform == null) return; // 还没就绪，下一帧再试

        // 1) 把 Buff 的 transform 挂到 CharacterItem 下
        _buff.transform.SetParent(item.transform, false);

        // 2) 给所有 effect 绑定 Item
        var effectsObj = _fiEffects?.GetValue(_buff) as IList<Effect>;
        if (effectsObj != null)
            for (var i = 0; i < effectsObj.Count; i++)
            {
                var e = effectsObj[i];
                if (e != null) e.SetItem(item);
            }

        // 一次性完成，移除自己
        _done = true;
        Destroy(this);
    }

    public void Init(Buff buff, FieldInfo fiEffects)
    {
        _buff = buff;
        _fiEffects = fiEffects;
    }
}