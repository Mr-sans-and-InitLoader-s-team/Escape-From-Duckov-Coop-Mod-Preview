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
                if (VirtualEndpointManager.Instance == null)
                {
                    Debug.LogError("[SteamLobbyHelper]  VirtualEndpointManager未初始化");
                    return;
                }
                var virtualEndPoint = VirtualEndpointManager.Instance.RegisterOrUpdateSteamID(hostSteamID, 27015);
                Debug.Log($"[SteamLobbyHelper] ✓ 虚拟端点: {virtualEndPoint}");
                Debug.Log($"[SteamLobbyHelper] ⏳ 等待P2P会话建立...");
                VirtualEndpointManager.Instance.StartCoroutine(
                    VirtualEndpointManager.Instance.WaitForSessionEstablished(hostSteamID, (success) =>
                    {
                        if (success)
                        {
                            Debug.Log($"[SteamLobbyHelper] ✓ P2P会话已就绪，开始连接");
                            NetService.Instance.ConnectToHost(virtualEndPoint.Address.ToString(), virtualEndPoint.Port);
                        }
                        else
                        {
                            Debug.LogError($"[SteamLobbyHelper] ❌ P2P会话建立失败，无法连接");
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
            NetService.Instance.StartNetwork(true, keepSteamLobby: true);
        }
    }
}
