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

namespace EscapeFromDuckovCoopMod;

public class Door
{
    [ThreadStatic] public static bool _applyingDoor; // 客户端正在应用网络下发，避免误触发本地拦截
    private static readonly FieldInfo DoorKeyField = AccessTools.Field(typeof(global::Door), "doorClosedDataKeyCached");
    private static readonly MethodInfo DoorGetKeyMethod = AccessTools.Method(typeof(global::Door), "GetKey");
    private static readonly MethodInfo DoorSetClosedMethod = AccessTools.Method(typeof(global::Door), "SetClosed",
        new[] { typeof(bool), typeof(bool) });
    private static readonly MethodInfo DoorOpenMethod = AccessTools.Method(typeof(global::Door), "Open");
    private static readonly MethodInfo DoorCloseMethod = AccessTools.Method(typeof(global::Door), "Close");
    private NetService Service => NetService.Instance;

    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;


    // 与 Door.GetKey 一致的稳定 Key：Door_{round(pos*10)} 的 GetHashCode
    public static int ComputeDoorKey(Transform t)
    {
        if (!t) return 0;
        var p = t.position * 10f;
        var k = new Vector3Int(
            Mathf.RoundToInt(p.x),
            Mathf.RoundToInt(p.y),
            Mathf.RoundToInt(p.z)
        );
        return $"Door_{k}".GetHashCode();
    }

    internal static int TryGetDoorKey(global::Door door, bool allowCompute = true)
    {
        if (!door) return 0;

        var key = 0;

        if (DoorKeyField != null)
            try
            {
                key = (int)DoorKeyField.GetValue(door);
            }
            catch
            {
                key = 0;
            }

        if (key == 0 && DoorGetKeyMethod != null)
            try
            {
                key = (int)DoorGetKeyMethod.Invoke(door, null);
            }
            catch
            {
                key = 0;
            }

        if (key == 0 && allowCompute)
        {
            key = ComputeDoorKey(door.transform);
            if (key != 0 && DoorKeyField != null)
                try
                {
                    DoorKeyField.SetValue(door, key);
                }
                catch
                {
                }
        }

        return key;
    }

    // 通过 key 找场景里的 Door（优先用其缓存字段 doorClosedDataKeyCached）
    public global::Door FindDoorByKey(int key)
    {
        if (key == 0) return null;
        if (CoopSyncDatabase.Environment.Doors.TryGetDoor(key, out var door) && door)
            return door;

        return null;
    }

    // 客户端：请求把某门设为 closed/open
    public void Client_RequestDoorSetState(global::Door d, bool closed)
    {
        if (IsServer || connectedPeer == null || d == null) return;

        var key = TryGetDoorKey(d);
        if (key == 0) return;

        CoopSyncDatabase.Environment.Doors.Register(d);

        var w = writer;
        if (w == null) return;
        w.Reset();
        w.Put((byte)Op.DOOR_REQ_SET);
        w.Put(key);
        w.Put(closed);
        connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
    }

    // 主机：处理客户端的设门请求
    public void Server_HandleDoorSetRequest(NetPeer peer, NetPacketReader reader)
    {
        if (!IsServer) return;
        var key = reader.GetInt();
        var closed = reader.GetBool();

        var door = FindDoorByKey(key);
        if (!door) return;

        CoopSyncDatabase.Environment.Doors.Register(door);

        // 调原生 API，走动画/存档/切 NavMesh
        if (closed) door.Close();
        else door.Open();
        // Postfix 里会统一广播；为保险也可在此再广播一次（双发也没坏处）
        // Server_BroadcastDoorState(key, closed);
    }

    // 主机：广播一条门状态
    public void Server_BroadcastDoorState(int key, bool closed)
    {
        if (!IsServer) return;
        var w = writer;
        if (w == null) return;
        w.Reset();
        w.Put((byte)Op.DOOR_STATE);
        w.Put(key);
        w.Put(closed);
        netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
    }

    // 客户端：应用门状态（反射调用 SetClosed，确保 NavMeshCut/插值/存档一致）
    public void Client_ApplyDoorState(int key, bool closed)
    {
        if (IsServer) return;
        var door = FindDoorByKey(key);
        if (!door) return;

        CoopSyncDatabase.Environment.Doors.Register(door);

        try
        {
            _applyingDoor = true;

            if (DoorSetClosedMethod != null)
            {
                DoorSetClosedMethod.Invoke(door, new object[] { closed, true });
            }
            else
            {
                if (closed)
                    DoorCloseMethod?.Invoke(door, null);
                else
                    DoorOpenMethod?.Invoke(door, null);
            }
        }
        finally
        {
            _applyingDoor = false;
        }
    }
}