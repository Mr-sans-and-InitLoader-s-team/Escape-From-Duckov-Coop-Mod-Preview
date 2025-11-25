using System;
using System.Collections.Generic;
using System.Text;

namespace EscapeFromDuckovCoopMod
{
    public static class RPCScene
    {
        public static void HandleSceneVoteStart(RpcContext context, SceneVoteStartRpc message)
        {
            if (context.IsServer) return;

            var net = SceneNet.Instance;
            if (net == null) return;

            net.Client_OnSceneVoteStart(message);

            if (Spectator.Instance._spectatorActive)
                Spectator.Instance._spectatorEndOnVotePending = true;
        }

        public static void HandleSceneVoteRequest(RpcContext context, SceneVoteRequestRpc message)
        {
            if (!context.IsServer) return;

            if (Spectator.Instance._spectatorActive)
                Spectator.Instance._spectatorEndOnVotePending = true;

            var net = SceneNet.Instance;
            if (net == null) return;

            net.Host_BeginSceneVote_Simple(
                message.TargetSceneId,
                message.CurtainGuid,
                message.NotifyEvac,
                message.SaveToFile,
                message.UseLocation,
                message.LocationName);
        }

        public static void HandleSceneReadySet(RpcContext context, SceneReadySetRpc message)
        {
            var net = SceneNet.Instance;
            if (net == null) return;

            if (context.IsServer)
            {
                net.Server_OnSceneReadySet(context.Sender, message.IsReady);
                return;
            }

            var pid = message.PlayerId ?? string.Empty;
            if (!net.sceneReady.ContainsKey(pid) && net.sceneParticipantIds.Contains(pid))
                net.sceneReady[pid] = false;

            if (net.sceneReady.ContainsKey(pid))
            {
                net.sceneReady[pid] = message.IsReady;
                Debug.Log($"[SCENE] READY_SET -> {pid} = {message.IsReady}");
            }
            else
            {
                Debug.LogWarning($"[SCENE] READY_SET for unknown pid '{pid}'. participants=[{string.Join(",", net.sceneParticipantIds)}]");
            }
        }

        public static void HandleSceneBeginLoad(RpcContext context, SceneBeginLoadRpc message)
        {
            if (context.IsServer) return;

            var net = SceneNet.Instance;
            if (net == null) return;

            if (Spectator.Instance._spectatorActive && Spectator.Instance._spectatorEndOnVotePending)
            {
                Spectator.Instance._spectatorEndOnVotePending = false;
                net.sceneVoteActive = false;
                net.sceneReady.Clear();
                net.localReady = false;

                Spectator.Instance.EndSpectatorAndShowClosure();
                return;
            }

            net.Client_OnBeginSceneLoad(message);
        }

        public static void HandleSceneVoteCancel(RpcContext context, SceneVoteCancelRpc message)
        {
            var net = SceneNet.Instance;
            if (net == null) return;

            if (context.IsServer)
            {
                net.sceneVoteActive = false;
                net.sceneParticipantIds.Clear();
                net.sceneReady.Clear();
                net.localReady = false;
                EscapeFromDuckovCoopMod.Utils.SceneTriggerResetter.ResetAllSceneTriggers();
                return;
            }

            net.Client_OnVoteCancelled();

            if (Spectator.Instance._spectatorActive && Spectator.Instance._spectatorEndOnVotePending)
            {
                Spectator.Instance._spectatorEndOnVotePending = false;
                Spectator.Instance.EndSpectatorAndShowClosure();
            }
        }

        public static void HandleSpectatorForceEnd(RpcContext context, SpectatorForceEndRpc message)
        {
            if (context.IsServer) return;

            Spectator.Instance?.EndSpectatorAndShowClosure(true);
        }
    }
}
