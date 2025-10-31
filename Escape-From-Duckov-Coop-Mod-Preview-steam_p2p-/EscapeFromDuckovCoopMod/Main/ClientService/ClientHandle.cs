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

public class ClientHandle
{
    private NetService Service => NetService.Instance;

    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;
    private Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    private Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;
    private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;

    public void HandleClientStatusUpdate(NetPeer peer, NetPacketReader reader)
    {
        var endPoint = reader.GetString();
        var playerName = reader.GetString();
        var isInGame = reader.GetBool();
        var position = reader.GetVector3();
        var rotation = reader.GetQuaternion();
        var sceneId = reader.GetString();
        var customFaceJson = reader.GetString();

        var equipmentCount = reader.GetInt();
        var equipmentList = new List<EquipmentSyncData>();
        for (var i = 0; i < equipmentCount; i++)
            equipmentList.Add(EquipmentSyncData.Deserialize(reader));

        var weaponCount = reader.GetInt();
        var weaponList = new List<WeaponSyncData>();
        for (var i = 0; i < weaponCount; i++)
            weaponList.Add(WeaponSyncData.Deserialize(reader));

        if (!playerStatuses.ContainsKey(peer))
            playerStatuses[peer] = new PlayerStatus();

        var st = playerStatuses[peer];
        st.EndPoint = endPoint;
        st.PlayerName = playerName;
        st.Latency = peer.Ping;
        st.IsInGame = isInGame;
        st.LastIsInGame = isInGame;
        st.Position = position;
        st.Rotation = rotation;
        if (!string.IsNullOrEmpty(customFaceJson))
            st.CustomFaceJson = customFaceJson;
        st.EquipmentList = equipmentList;
        st.WeaponList = weaponList;
        st.SceneId = sceneId;

        if (isInGame && !remoteCharacters.ContainsKey(peer))
        {
            CreateRemoteCharacter.CreateRemoteCharacterAsync(peer, position, rotation, customFaceJson).Forget();
            foreach (var e in equipmentList) COOPManager.HostPlayer_Apply.ApplyEquipmentUpdate(peer, e.SlotHash, e.ItemId).Forget();
            foreach (var w in weaponList) COOPManager.HostPlayer_Apply.ApplyWeaponUpdate(peer, w.SlotHash, w.ItemId).Forget();
        }
        else if (isInGame)
        {
            if (remoteCharacters.TryGetValue(peer, out var go) && go != null)
            {
                go.transform.position = position;
                go.GetComponentInChildren<CharacterMainControl>().modelRoot.transform.rotation = rotation;
            }

            foreach (var e in equipmentList) COOPManager.HostPlayer_Apply.ApplyEquipmentUpdate(peer, e.SlotHash, e.ItemId).Forget();
            foreach (var w in weaponList) COOPManager.HostPlayer_Apply.ApplyWeaponUpdate(peer, w.SlotHash, w.ItemId).Forget();
        }

        playerStatuses[peer] = st;

        SendLocalPlayerStatus.Instance.SendPlayerStatusUpdate();
    }
}