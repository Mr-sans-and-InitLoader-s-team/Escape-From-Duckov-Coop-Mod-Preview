using Steamworks;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace EscapeFromDuckovCoopMod
{
    public static class SteamLobbyHelper
    {
        public static void TriggerMultiplayerConnect(CSteamID hostSteamID)
        {
            try
            {
                Debug.Log($"[SteamLobbyHelper] ========== 开始连接流程 ==========");
                Debug.Log($"[SteamLobbyHelper] 主机Steam ID: {hostSteamID}");
                if (SteamEndPointMapper.Instance == null)
                {
                    Debug.LogError("[SteamLobbyHelper] SteamEndPointMapper not initialized");
                    return;
                }
                var virtualEndPoint = SteamEndPointMapper.Instance.RegisterSteamID(hostSteamID, 27015);
                Debug.Log($"[SteamLobbyHelper] Virtual endpoint: {virtualEndPoint}");
                Debug.Log($"[SteamLobbyHelper] Waiting for P2P session...");
                SteamEndPointMapper.Instance.StartCoroutine(
                    SteamEndPointMapper.Instance.WaitForP2PSessionEstablished(hostSteamID, (success) =>
                    {
                        if (success)
                        {
                            Debug.Log($"[SteamLobbyHelper] P2P session ready, connecting");
                            NetService.Instance.ConnectToHost(virtualEndPoint.Address.ToString(), virtualEndPoint.Port);
                        }
                        else
                        {
                            Debug.LogError($"[SteamLobbyHelper] P2P session failed, unable to connect");
                        }
                    }, 10f)
                );
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamLobbyHelper] Connect trigger failed: {ex}");
                Debug.LogError($"[SteamLobbyHelper] 堆栈: {ex.StackTrace}");
            }
        }

        public static void TriggerMultiplayerHost()
        {
            NetService.Instance.StartNetwork(true, keepSteamLobby: true);
        }
    }
}
