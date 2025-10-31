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

public sealed class NetAiTag : MonoBehaviour
{
    public int aiId;
    public string nameOverride; // 主机下发的显示名（纯文本
    public int? iconTypeOverride; // 来自主机的 CharacterIconTypes（int）
    public bool? showNameOverride; // 主机裁决是否显示名字

    private void Awake()
    {
        Guard();
    }

    private void OnEnable()
    {
        Guard();
    }

    private void Guard()
    {
        try
        {
            var cmc = GetComponent<CharacterMainControl>();
            var mod = ModBehaviourF.Instance;
            if (!cmc || mod == null) return;

            if (!AITool.IsRealAI(cmc)) Destroy(this);
        }
        catch
        {
        }
    }
}