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

public class Buff_
{
    private NetService Service => NetService.Instance;


    private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;

    public void HandlePlayerBuffSelfApply(NetPacketReader r)
    {
        var weaponTypeId = r.GetInt(); // overrideWeaponID（通常就是武器/手雷的 Item.TypeID）
        var buffId = r.GetInt(); // 兜底的 buff id
        ApplyBuffToSelf_Client(weaponTypeId, buffId).Forget();
    }

    public void HandleBuffProxyApply(NetPacketReader r)
    {
        var hostId = r.GetString(); // e.g. "Host:9050"
        var weaponTypeId = r.GetInt();
        var buffId = r.GetInt();
        ApplyBuffProxy_Client(hostId, weaponTypeId, buffId).Forget();
    }

    public async UniTask ApplyBuffToSelf_Client(int weaponTypeId, int buffId)
    {
        var me = LevelManager.Instance ? LevelManager.Instance.MainCharacter : null;
        if (!me) return;

        var buff = await COOPManager.ResolveBuffAsync(weaponTypeId, buffId);
        if (buff != null) me.AddBuff(buff, null, weaponTypeId);
    }

    public async UniTask ApplyBuffProxy_Client(string playerId, int weaponTypeId, int buffId)
    {
        if (NetService.Instance.IsSelfId(playerId)) return; // 不应该给本地自己用这个分支
        if (!clientRemoteCharacters.TryGetValue(playerId, out var go) || go == null)
        {
            // 远端主机克隆还没生成？先记下来，等 CreateRemoteCharacterForClient 时补发
            if (!CoopTool._cliPendingProxyBuffs.TryGetValue(playerId, out var list))
                list = CoopTool._cliPendingProxyBuffs[playerId] = new List<(int, int)>();
            list.Add((weaponTypeId, buffId));
            return;
        }

        var cmc = go.GetComponent<CharacterMainControl>();
        if (!cmc) return;

        var buff = await COOPManager.ResolveBuffAsync(weaponTypeId, buffId);
        if (buff != null) cmc.AddBuff(buff, null, weaponTypeId);
    }
}