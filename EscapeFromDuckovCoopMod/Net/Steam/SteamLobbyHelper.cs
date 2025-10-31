// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team

using Steamworks;
using System;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Net.Steam
{
    public static class SteamLobbyHelper
    {
        public static void TriggerMultiplayerConnect(CSteamID hostSteamID)
        {
            try
            {
                Debug.Log("[SteamLobbyHelper] ========== 开始连接流程 ==========");
                Debug.Log("[SteamLobbyHelper] 主机Steam ID: " + hostSteamID);
                
                if (SteamEndPointMapper.Instance == null)
                {
                    Debug.LogError("[SteamLobbyHelper] SteamEndPointMapper未初始化");
                    return;
                }
                
                var virtualEndPoint = SteamEndPointMapper.Instance.RegisterSteamID(hostSteamID, 27015);
                Debug.Log("[SteamLobbyHelper] 虚拟端点: " + virtualEndPoint);
                Debug.Log("[SteamLobbyHelper] 等待P2P会话建立...");
                
                SteamEndPointMapper.Instance.StartCoroutine(
                    SteamEndPointMapper.Instance.WaitForP2PSessionEstablished(hostSteamID, (success) =>
                    {
                        if (success)
                        {
                            Debug.Log("[SteamLobbyHelper] P2P会话已就绪，开始连接");
                            NetService.Instance.ConnectToHost(virtualEndPoint.Address.ToString(), virtualEndPoint.Port);
                        }
                        else
                        {
                            Debug.LogError("[SteamLobbyHelper] P2P会话建立失败，无法连接");
                        }
                    }, 10f)
                );
            }
            catch (Exception ex)
            {
                Debug.LogError("[SteamLobbyHelper] 触发连接失败: " + ex);
                Debug.LogError("[SteamLobbyHelper] 堆栈: " + ex.StackTrace);
            }
        }
        
        public static void TriggerMultiplayerHost()
        {
            Debug.Log("[SteamLobbyHelper] ========== 触发主机启动 ==========");
            
            if (NetService.Instance == null)
            {
                Debug.LogError("[SteamLobbyHelper] NetService未初始化");
                return;
            }
            
            if (NetService.Instance.networkStarted && NetService.Instance.IsServer)
            {
                Debug.Log("[SteamLobbyHelper] 服务器已启动");
                return;
            }
            
            Debug.Log("[SteamLobbyHelper] 启动服务器网络");
            NetService.Instance.StartNetwork(true);
        }
    }
}

