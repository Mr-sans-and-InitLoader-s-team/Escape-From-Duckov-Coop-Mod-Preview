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

public sealed class FriendlyFireSync
{
    public bool FriendlyFirePlayersEnabled { get; private set; }
    private bool _pendingApply;

    private NetService Service => NetService.Instance;
    private bool IsServer => Service != null && Service.IsServer;
    private bool NetworkStarted => Service != null && Service.networkStarted;

    public void OnGeneralSettingsApplied(CoopGeneralSettings settings)
    {
        if (settings == null) return;

        FriendlyFirePlayersEnabled = settings.FriendlyFirePlayers;
        _pendingApply = true;
        TryApplyPending();
    }

    public void OnPeerConnected(NetPeer peer)
    {
        if (!IsServer || peer == null || !NetworkStarted) return;
        SendStateToPeer(peer);
    }

    public void OnNetworkStarted(bool isServer)
    {
        if (!isServer) return;
        TryApplyPending();
    }

    public void Client_HandleState(PlayerFriendlyFireStateRpc message)
    {
        FriendlyFirePlayersEnabled = message.Enabled;
        ApplyTeamToExistingPlayers();
    }

    public void OnRemoteCharacterCreated(CharacterMainControl cmc)
    {
        if (cmc == null || cmc == CharacterMainControl.Main) return;
        if (FriendlyFirePlayersEnabled)
            SafeSetTeam(cmc, Teams.middle);
        else
            SafeSetTeam(cmc, Teams.player);
    }

    public void UpdateState(bool enabled, bool broadcast)
    {
        FriendlyFirePlayersEnabled = enabled;
        ApplyTeamToExistingPlayers();

        if (!broadcast || !IsServer || !NetworkStarted) return;

        var rpc = new PlayerFriendlyFireStateRpc
        {
            Enabled = enabled
        };

        CoopTool.SendRpc(in rpc);
    }

    private void ApplyTeamToExistingPlayers()
    {
        var desiredTeamRemote = FriendlyFirePlayersEnabled ? Teams.middle : Teams.player;
        var desiredTeamSelf = Teams.player;

        var self = CharacterMainControl.Main;
        if (self) SafeSetTeam(self, FriendlyFirePlayersEnabled ? desiredTeamSelf : desiredTeamSelf);

        if (IsServer)
        {
            if (Service?.remoteCharacters != null)
                foreach (var kv in Service.remoteCharacters)
                    if (kv.Value)
                        SafeSetTeam(kv.Value.GetComponent<CharacterMainControl>(), desiredTeamRemote);
        }
        else
        {
            if (Service?.clientRemoteCharacters != null)
                foreach (var kv in Service.clientRemoteCharacters)
                    if (kv.Value)
                        SafeSetTeam(kv.Value.GetComponent<CharacterMainControl>(), desiredTeamRemote);
        }
    }

    private void SendStateToPeer(NetPeer peer)
    {
        var rpc = new PlayerFriendlyFireStateRpc
        {
            Enabled = FriendlyFirePlayersEnabled
        };

        CoopTool.SendRpcTo(peer, in rpc);
    }

    private static void SafeSetTeam(CharacterMainControl cmc, Teams team)
    {
        if (!cmc) return;

        try
        {
            cmc.SetTeam(team);
        }
        catch
        {
        }
    }

    private void TryApplyPending()
    {
        if (!_pendingApply) return;
        if (!IsServer || !NetworkStarted) return;

        _pendingApply = false;
        UpdateState(FriendlyFirePlayersEnabled, true);
    }
}
