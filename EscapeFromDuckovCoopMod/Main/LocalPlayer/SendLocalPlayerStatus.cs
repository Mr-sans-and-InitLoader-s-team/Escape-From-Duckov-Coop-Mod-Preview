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

using System;
using System.Collections.Generic;

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
    private Vector3 _lastSentPosition;
    private double _lastSentTime;

    public void Init()
    {
        Instance = this;
    }

    public void SendPlayerStatusUpdate()
    {
        if (!IsServer) return;

        var statuses = new List<PlayerStatusPayload>();

        var localEquipment = LocalPlayerManager.Instance.GetLocalEquipment();
        var localWeapons = LocalPlayerManager.Instance.GetLocalWeapons();

        statuses.Add(new PlayerStatusPayload
        {
            PlayerId = localPlayerStatus.EndPoint,
            PlayerName = localPlayerStatus.PlayerName,
            Latency = 0,
            IsInGame = localPlayerStatus.IsInGame,
            Position = localPlayerStatus.Position,
            Rotation = localPlayerStatus.Rotation,
            SceneId = localPlayerStatus.SceneId,
            CustomFaceJson = localPlayerStatus.CustomFaceJson,
            Equipment = localEquipment.ToArray(),
            Weapons = localWeapons.ToArray()
        });

        foreach (var kvp in playerStatuses)
        {
            var st = kvp.Value;
            if (st == null) continue;

            var equipmentList = st.EquipmentList ?? new List<EquipmentSyncData>();
            var weaponList = st.WeaponList ?? new List<WeaponSyncData>();

            statuses.Add(new PlayerStatusPayload
            {
                PlayerId = st.EndPoint,
                PlayerName = st.PlayerName,
                Latency = st.Latency,
                IsInGame = st.IsInGame,
                Position = st.Position,
                Rotation = st.Rotation,
                SceneId = st.SceneId,
                CustomFaceJson = st.CustomFaceJson,
                Equipment = equipmentList.ToArray(),
                Weapons = weaponList.ToArray()
            });
        }

        var rpc = new PlayerStatusUpdateRpc
        {
            Players = statuses.ToArray()
        };

        CoopTool.SendRpc(in rpc);
    }


    public void SendPositionUpdate()
    {
        if (localPlayerStatus == null || !networkStarted) return;

        var main = CharacterMainControl.Main;
        if (!main) return;

        var tr = main.transform;
        var mr = main.modelRoot ? main.modelRoot.transform : null;

        var pos = tr.position;
        var fwd = mr ? mr.forward : tr.forward;
        if (fwd.sqrMagnitude < 1e-12f) fwd = Vector3.forward;

        var now = Time.unscaledTimeAsDouble;
        var vel = Vector3.zero;
        if (_lastSentTime > 0d)
        {
            var dt = now - _lastSentTime;
            if (dt > 1e-6) vel = (pos - _lastSentPosition) / (float)dt;
        }

        _lastSentPosition = pos;
        _lastSentTime = now;

        var rpc = new PlayerPositionUpdateRpc
        {
            EndPoint = localPlayerStatus.EndPoint,
            Position = pos,
            Forward = fwd,
            Velocity = vel,
            Timestamp = now
        };

        CoopTool.SendRpc(in rpc);
    }

    public void SendEquipmentUpdate(EquipmentSyncData equipmentData)
    {
        if (localPlayerStatus == null || !networkStarted) return;

        var rpc = new EquipmentUpdateRpc
        {
            PlayerId = localPlayerStatus.EndPoint,
            SlotHash = equipmentData.SlotHash,
            ItemId = equipmentData.ItemId ?? string.Empty
        };

        CoopTool.SendRpc(in rpc);
    }


    public void SendWeaponUpdate(WeaponSyncData weaponSyncData)
    {
        if (localPlayerStatus == null || !networkStarted) return;

        var rpc = new WeaponUpdateRpc
        {
            PlayerId = localPlayerStatus.EndPoint,
            SlotHash = weaponSyncData.SlotHash,
            ItemId = weaponSyncData.ItemId ?? string.Empty
        };

        CoopTool.SendRpc(in rpc);
    }

    public void SendAnimationStatus()
    {
        if (!networkStarted) return;

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

        var rpc = new PlayerAnimationSyncRpc
        {
            PlayerId = localPlayerStatus?.EndPoint,
            MoveSpeed = anim.GetFloat("MoveSpeed"),
            MoveDirX = anim.GetFloat("MoveDirX"),
            MoveDirY = anim.GetFloat("MoveDirY"),
            IsDashing = anim.GetBool("Dashing"),
            IsAttacking = anim.GetBool("Attack"),
            HandState = anim.GetInteger("HandState"),
            GunReady = anim.GetBool("GunReady"),
            StateHash = stateHash,
            NormTime = normTime
        };

        CoopTool.SendRpc(in rpc);
    }
}