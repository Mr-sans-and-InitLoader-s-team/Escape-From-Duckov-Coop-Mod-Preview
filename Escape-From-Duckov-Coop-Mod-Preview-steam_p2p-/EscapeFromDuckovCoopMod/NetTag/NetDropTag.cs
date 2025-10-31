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

using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

public class NetDropTag : MonoBehaviour
{
    public uint id;


    private void Awake()
    {
        //😆
    }

    private static void AddNetDropTag(GameObject go, uint id)
    {
        if (!go) return;
        var tag = go.GetComponent<NetDropTag>() ?? go.AddComponent<NetDropTag>();
        tag.id = id;
    }

    private static void AddNetDropTag(Item item, uint id)
    {
        try
        {
            var ag = item?.ActiveAgent;
            if (ag && ag.gameObject) AddNetDropTag(ag.gameObject, id);
        }
        catch
        {
        }
    }
}