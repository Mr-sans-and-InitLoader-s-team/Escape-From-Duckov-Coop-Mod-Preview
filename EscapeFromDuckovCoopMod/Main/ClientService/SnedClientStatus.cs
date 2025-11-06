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
        var equipmentList = LocalPlayerManager.Instance.GetLocalEquipment();
        var weaponList = LocalPlayerManager.Instance.GetLocalWeapons();

        var rpcManager = Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null)
        {
            Debug.LogWarning("[ClientStatus] HybridRPCManager not available");
            return;
        }

        rpcManager.CallRPC("ClientStatusReport", Net.HybridP2P.RPCTarget.Server, 0, w =>
        {
            w.Put(localPlayerStatus.EndPoint);
            w.Put(localPlayerStatus.PlayerName);
            w.Put(localPlayerStatus.IsInGame);
            w.Put(localPlayerStatus.Position.x);
            w.Put(localPlayerStatus.Position.y);
            w.Put(localPlayerStatus.Position.z);
            w.Put(localPlayerStatus.Rotation.x);
            w.Put(localPlayerStatus.Rotation.y);
            w.Put(localPlayerStatus.Rotation.z);
            w.Put(localPlayerStatus.Rotation.w);
            w.Put(localPlayerStatus?.SceneId ?? string.Empty);
            w.Put(localPlayerStatus.CustomFaceJson ?? "");
            
            w.Put(equipmentList.Count);
            foreach (var e in equipmentList) e.Serialize(w);
            
            w.Put(weaponList.Count);
            foreach (var w2 in weaponList) w2.Serialize(w);
        });
    }
}