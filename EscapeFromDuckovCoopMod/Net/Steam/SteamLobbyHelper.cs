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
                    Debug.LogError("[SteamLobbyHelper] ❌ SteamEndPointMapper未初始化");
                    if (NetService.Instance != null)
                        NetService.Instance.status = "Steam P2P endpoint mapper not initialized";
                    return;
                }
                var port = NetService.Instance != null ? NetService.Instance.port : 9050;
                var virtualEndPoint = SteamEndPointMapper.Instance.RegisterSteamID(hostSteamID, port);
                Debug.Log($"[SteamLobbyHelper] ✓ 虚拟端点: {virtualEndPoint}");
                Debug.Log($"[SteamLobbyHelper] ⏳ 等待P2P会话建立...");
                SteamEndPointMapper.Instance.StartCoroutine(
                    SteamEndPointMapper.Instance.WaitForP2PSessionEstablished(hostSteamID, (success) =>
                    {
                        if (success)
                        {
                            Debug.Log($"[SteamLobbyHelper] ✓ P2P会话已就绪，开始连接");
                            if (NetService.Instance != null)
                                NetService.Instance.ConnectToHost(virtualEndPoint.Address.ToString(), virtualEndPoint.Port);
                        }
                        else
                        {
                            Debug.LogError($"[SteamLobbyHelper] ❌ P2P会话建立失败，无法连接");
                            if (NetService.Instance != null)
                            {
                                NetService.Instance.status = "Steam P2P session timeout";
                                NetService.Instance.isConnecting = false;
                            }
                            MModUI.ShowTip("[STEAM_JOIN] Steam P2P session timeout");
                        }
                    }, 10f)
                );
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamLobbyHelper] ❌❌❌ 触发连接失败: {ex}");
                Debug.LogError($"[SteamLobbyHelper] 堆栈: {ex.StackTrace}");
            }
        }

        public static void TriggerMultiplayerHost()
        {
            if (NetService.Instance == null)
            {
                MModUI.ShowTip("[STEAM_JOIN] network service not initialized");
                return;
            }

            if (!NetService.Instance.TryStartNetwork(true, keepSteamLobby: true))
                MModUI.ShowTip("[STEAM_JOIN] " + NetService.Instance.status);
        }
    }
}
