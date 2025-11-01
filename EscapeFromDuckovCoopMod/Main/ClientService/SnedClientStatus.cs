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

public class Send_ClientStatus : MonoBehaviour
{
    public static Send_ClientStatus Instance;

    private NetService Service => NetService.Instance;
    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;

    public void Init()
    {
        Debug.Log("ModBehaviour Awake");
        Instance = this;
    }

    public void SendClientStatusUpdate()
    {
        if (IsServer || connectedPeer == null) return;

        localPlayerStatus.CustomFaceJson = CustomFace.LoadLocalCustomFaceJson();
        var equipmentList = LoaclPlayerManager.Instance.GetLocalEquipment();
        var weaponList = LoaclPlayerManager.Instance.GetLocalWeapons();

        writer.Reset();
        writer.Put((byte)Op.CLIENT_STATUS_UPDATE); // opcode
        writer.Put(localPlayerStatus.EndPoint);
        writer.Put(localPlayerStatus.PlayerName);
        writer.Put(localPlayerStatus.IsInGame);
        writer.PutVector3(localPlayerStatus.Position);
        writer.PutQuaternion(localPlayerStatus.Rotation);

        writer.Put(localPlayerStatus?.SceneId ?? string.Empty);

        writer.Put(localPlayerStatus.CustomFaceJson ?? "");

        writer.Put(equipmentList.Count);
        foreach (var e in equipmentList) e.Serialize(writer);

        writer.Put(weaponList.Count);
        foreach (var w in weaponList) w.Serialize(writer);

        connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
    }
}