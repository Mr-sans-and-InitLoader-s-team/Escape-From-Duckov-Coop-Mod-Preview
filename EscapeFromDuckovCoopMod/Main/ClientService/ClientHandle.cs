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

    public void HandleClientStatusUpdate(NetPeer peer, NetDataReader reader)
    {
        var endPoint = reader.GetString();  // 客户端自报的EndPoint（Client:xxxxx），仅用于日志/调试
        var playerName = reader.GetString();
        var isInGame = reader.GetBool();
        var position = reader.GetVector3();
        var rotation = reader.GetQuaternion();
        var sceneId = reader.GetString();
        // ✅ 不再读取 faceJson，通过 PLAYER_APPEARANCE 包接收

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
        // ⚠️ 重要：服务器端必须保持使用 peer.EndPoint（虚拟IP格式），不能用客户端自报的 Client:xxxxx
        // st.EndPoint = endPoint;  // ❌ 错误：不要覆盖服务器端的虚拟IP EndPoint
        if (string.IsNullOrEmpty(st.EndPoint))
            st.EndPoint = peer.EndPoint.ToString();  // ✅ 使用服务器端的虚拟IP EndPoint

        st.ClientReportedId = endPoint;
        st.PlayerName = playerName;
        st.Latency = peer.Ping;
        st.IsInGame = isInGame;
        st.LastIsInGame = isInGame;
        st.Position = position;
        st.Rotation = rotation;
        // ✅ CustomFaceJson 通过 PLAYER_APPEARANCE 包单独接收
        st.EquipmentList = equipmentList;
        st.WeaponList = weaponList;
        st.SceneId = sceneId;

        if (isInGame && !remoteCharacters.ContainsKey(peer))
        {
            // ✅ 使用缓存或状态中的外观数据
            var faceJson = st.CustomFaceJson ?? string.Empty;
            CreateRemoteCharacter.CreateRemoteCharacterAsync(peer, position, rotation, faceJson).Forget();
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