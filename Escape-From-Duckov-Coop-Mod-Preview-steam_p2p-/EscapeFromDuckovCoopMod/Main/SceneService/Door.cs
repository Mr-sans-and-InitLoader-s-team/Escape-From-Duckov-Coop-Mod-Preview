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

using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

public class Door
{
    [ThreadStatic] public static bool _applyingDoor; // 客户端正在应用网络下发，避免误触发本地拦截
    private NetService Service => NetService.Instance;

    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;


    // 与 Door.GetKey 一致的稳定 Key：Door_{round(pos*10)} 的 GetHashCode
    public int ComputeDoorKey(Transform t)
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

    // 通过 key 找场景里的 Door（优先用其缓存字段 doorClosedDataKeyCached）
    public global::Door FindDoorByKey(int key)
    {
        if (key == 0) return null;
        var doors = Object.FindObjectsOfType<global::Door>(true);
        var fCache = AccessTools.Field(typeof(global::Door), "doorClosedDataKeyCached");
        var mGetKey = AccessTools.Method(typeof(global::Door), "GetKey");

        foreach (var d in doors)
        {
            if (!d) continue;
            var k = 0;
            try
            {
                k = (int)fCache.GetValue(d);
            }
            catch
            {
            }

            if (k == 0)
                try
                {
                    k = (int)mGetKey.Invoke(d, null);
                }
                catch
                {
                }

            if (k == key) return d;
        }

        return null;
    }

    // 客户端：请求把某门设为 closed/open
    public void Client_RequestDoorSetState(global::Door d, bool closed)
    {
        if (IsServer || connectedPeer == null || d == null) return;

        var key = 0;
        try
        {
            // 优先用缓存字段；无则重算（与 Door.GetKey 一致）
            key = (int)AccessTools.Field(typeof(global::Door), "doorClosedDataKeyCached").GetValue(d);
        }
        catch
        {
        }

        if (key == 0) key = ComputeDoorKey(d.transform);
        if (key == 0) return;

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

        try
        {
            _applyingDoor = true;

            var mSetClosed2 = AccessTools.Method(typeof(global::Door), "SetClosed",
                new[] { typeof(bool), typeof(bool) });
            if (mSetClosed2 != null)
            {
                mSetClosed2.Invoke(door, new object[] { closed, true });
            }
            else
            {
                if (closed)
                    AccessTools.Method(typeof(global::Door), "Close")?.Invoke(door, null);
                else
                    AccessTools.Method(typeof(global::Door), "Open")?.Invoke(door, null);
            }
        }
        finally
        {
            _applyingDoor = false;
        }
    }
}