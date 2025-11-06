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

public class SendLocalPlayerStatus : MonoBehaviour
{
    public static SendLocalPlayerStatus Instance;

    private NetService Service => NetService.Instance;
    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;
    private Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;

    public void Init()
    {
        Instance = this;
    }

    public void SendPlayerStatusUpdate()
    {
        if (!IsServer) return;

        var rpcManager = EscapeFromDuckovCoopMod.Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null) return;

        var statuses = new List<PlayerStatus> { localPlayerStatus };
        foreach (var kvp in playerStatuses) statuses.Add(kvp.Value);
        
        var tempWriter = new NetDataWriter();

        rpcManager.CallRPC("PlayerStatusFullSync", EscapeFromDuckovCoopMod.Net.HybridP2P.RPCTarget.AllClients, 0, w =>
        {
            w.Put(statuses.Count);

            foreach (var st in statuses)
            {
                w.Put(st.EndPoint);
                w.Put(st.PlayerName);
                w.Put(st.Latency);
                w.Put(st.IsInGame);
                w.Put(st.Position.x);
                w.Put(st.Position.y);
                w.Put(st.Position.z);
                w.Put(st.Rotation.x);
                w.Put(st.Rotation.y);
                w.Put(st.Rotation.z);
                w.Put(st.Rotation.w);

                var sid = st.SceneId;
                w.Put(sid ?? string.Empty);

                w.Put(st.CustomFaceJson ?? "");

                var equipmentList = st == localPlayerStatus ? LocalPlayerManager.Instance.GetLocalEquipment() : st.EquipmentList ?? new List<EquipmentSyncData>();
                w.Put(equipmentList.Count);
                foreach (var e in equipmentList)
                {
                    tempWriter.Reset();
                    e.Serialize(tempWriter);
                    w.Put(tempWriter.Data, 0, tempWriter.Length);
                }

                var weaponList = st == localPlayerStatus ? LocalPlayerManager.Instance.GetLocalWeapons() : st.WeaponList ?? new List<WeaponSyncData>();
                w.Put(weaponList.Count);
                foreach (var wep in weaponList)
                {
                    tempWriter.Reset();
                    wep.Serialize(tempWriter);
                    w.Put(tempWriter.Data, 0, tempWriter.Length);
                }
                
                w.Put((byte)st.NATType);
                w.Put(st.UseRelay);
            }
        }, DeliveryMethod.ReliableOrdered);
    }


    private int _positionUpdateCount = 0;
    private float _lastPosLogTime = 0;
    private float _lastPosSendTime = 0;
    private const float MIN_POS_SEND_INTERVAL = 0.033f;
    
    public void SendPositionUpdate()
    {
        if (localPlayerStatus == null || !networkStarted) return;

        var main = CharacterMainControl.Main;
        if (!main) return;
        
        float timeSinceLastSend = Time.realtimeSinceStartup - _lastPosSendTime;
        if (timeSinceLastSend < MIN_POS_SEND_INTERVAL)
        {
            return;
        }
        _lastPosSendTime = Time.realtimeSinceStartup;

        var tr = main.transform;
        var mr = main.modelRoot ? main.modelRoot.transform : null;

        var pos = tr.position;
        var fwd = mr ? mr.forward : tr.forward;
        if (fwd.sqrMagnitude < 1e-12f) fwd = Vector3.forward;

        var rot = Quaternion.LookRotation(fwd, Vector3.up);
        
        var velocity = Vector3.zero;
        var rb = main.GetComponent<Rigidbody>();
        if (rb != null)
        {
            velocity = rb.velocity;
        }
        else if (main.GetComponent<CharacterController>() is CharacterController cc)
        {
            velocity = cc.velocity;
        }
        
        var relay = EscapeFromDuckovCoopMod.Net.HybridP2P.HybridP2PRelay.Instance;
        if (relay != null && localPlayerStatus != null)
        {
            relay.RecordPosition(localPlayerStatus.EndPoint, pos, rot, velocity);
        }

        var rpcManager = EscapeFromDuckovCoopMod.Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null) return;

        var target = IsServer ? EscapeFromDuckovCoopMod.Net.HybridP2P.RPCTarget.AllClients : EscapeFromDuckovCoopMod.Net.HybridP2P.RPCTarget.Server;
        var rpcName = IsServer ? "PlayerPositionBroadcast" : "PlayerPositionUpdate";

        rpcManager.CallRPC(rpcName, target, 0, w =>
        {
            w.Put(localPlayerStatus.EndPoint);
            w.Put(pos.x);
            w.Put(pos.y);
            w.Put(pos.z);
            w.Put(fwd.x);
            w.Put(fwd.y);
            w.Put(fwd.z);
        }, DeliveryMethod.Unreliable);
        
        _positionUpdateCount++;
        if (Time.realtimeSinceStartup - _lastPosLogTime > 5f)
        {
            _positionUpdateCount = 0;
            _lastPosLogTime = Time.realtimeSinceStartup;
        }
    }

    public void SendEquipmentUpdate(EquipmentSyncData equipmentData)
    {
        if (localPlayerStatus == null || !networkStarted) return;

        var rpcManager = EscapeFromDuckovCoopMod.Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null) return;

        var target = IsServer ? EscapeFromDuckovCoopMod.Net.HybridP2P.RPCTarget.AllClients : EscapeFromDuckovCoopMod.Net.HybridP2P.RPCTarget.Server;

        rpcManager.CallRPC("EquipmentSync", target, 0, w =>
        {
            w.Put(localPlayerStatus.EndPoint);
            w.Put(equipmentData.SlotHash);
            w.Put(equipmentData.ItemId ?? "");
        }, DeliveryMethod.ReliableOrdered);
    }


    public void SendWeaponUpdate(WeaponSyncData weaponSyncData)
    {
        if (localPlayerStatus == null || !networkStarted) return;

        var rpcManager = EscapeFromDuckovCoopMod.Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null) return;

        var target = IsServer ? EscapeFromDuckovCoopMod.Net.HybridP2P.RPCTarget.AllClients : EscapeFromDuckovCoopMod.Net.HybridP2P.RPCTarget.Server;

        rpcManager.CallRPC("WeaponEquipSync", target, 0, w =>
        {
            w.Put(localPlayerStatus.EndPoint);
            w.Put(weaponSyncData.SlotHash);
            w.Put(weaponSyncData.ItemId ?? "");
        }, DeliveryMethod.ReliableOrdered);
    }

    public void SendAnimationStatus()
    {
        if (!networkStarted) return;

        var rpcManager = EscapeFromDuckovCoopMod.Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null) return;

        var mainControl = CharacterMainControl.Main;
        if (mainControl == null) return;

        var model = mainControl.modelRoot.Find("0_CharacterModel_Custom_Template(Clone)");
        if (model == null) return;

        var animCtrl = model.GetComponent<CharacterAnimationControl_MagicBlend>();
        if (animCtrl == null || animCtrl.animator == null) return;

        var anim = animCtrl.animator;
        var state = anim.GetCurrentAnimatorStateInfo(0);
        var stateHash = state.shortNameHash;
        var normTime = state.normalizedTime;

        if (IsServer)
        {
            rpcManager.CallRPC("AnimationSync", EscapeFromDuckovCoopMod.Net.HybridP2P.RPCTarget.AllClients, 0, w =>
            {
                w.Put(localPlayerStatus.EndPoint);
                w.Put(anim.GetFloat("MoveSpeed"));
                w.Put(anim.GetFloat("MoveDirX"));
                w.Put(anim.GetFloat("MoveDirY"));
                w.Put(anim.GetBool("Dashing"));
                w.Put(anim.GetBool("Attack"));
                w.Put(anim.GetInteger("HandState"));
                w.Put(anim.GetBool("GunReady"));
                w.Put(stateHash);
                w.Put(normTime);
            }, DeliveryMethod.Sequenced);
        }
        else
        {
            rpcManager.CallRPC("AnimationSyncRequest", EscapeFromDuckovCoopMod.Net.HybridP2P.RPCTarget.Server, 0, w =>
            {
                w.Put(anim.GetFloat("MoveSpeed"));
                w.Put(anim.GetFloat("MoveDirX"));
                w.Put(anim.GetFloat("MoveDirY"));
                w.Put(anim.GetBool("Dashing"));
                w.Put(anim.GetBool("Attack"));
                w.Put(anim.GetInteger("HandState"));
                w.Put(anim.GetBool("GunReady"));
                w.Put(stateHash);
                w.Put(normTime);
            }, DeliveryMethod.Sequenced);
        }
    }


    public void Net_ReportPlayerDeadTree(CharacterMainControl who)
    {
        if (!networkStarted || IsServer || who == null) return;

        var rpcManager = EscapeFromDuckovCoopMod.Net.HybridP2P.HybridRPCManager.Instance;
        if (rpcManager == null) return;

        var item = who.CharacterItem;
        if (item == null) return;

        var pos = who.transform.position;
        var rot = who.characterModel ? who.characterModel.transform.rotation : who.transform.rotation;

        var tempWriter = new NetDataWriter();
        
        rpcManager.CallRPC("PlayerDeadTree", EscapeFromDuckovCoopMod.Net.HybridP2P.RPCTarget.Server, 0, w =>
        {
            w.Put(pos.x);
            w.Put(pos.y);
            w.Put(pos.z);
            w.Put(rot.x);
            w.Put(rot.y);
            w.Put(rot.z);
            w.Put(rot.w);

            tempWriter.Reset();
            ItemTool.WriteItemSnapshot(tempWriter, item);
            w.Put(tempWriter.Data, 0, tempWriter.Length);
        }, DeliveryMethod.ReliableOrdered);
    }
}