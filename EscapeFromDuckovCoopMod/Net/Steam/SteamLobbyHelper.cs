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
                
                // 设置传输模式为 Steam P2P（客机通过 Lobby 加入）
                NetService.Instance.SetTransportMode(NetworkTransportMode.SteamP2P);
                Debug.Log($"[SteamLobbyHelper] ✓ 设置传输模式为 Steam P2P");
                
                if (SteamEndPointMapper.Instance == null)
                {
                    Debug.LogError("[SteamLobbyHelper] ❌ SteamEndPointMapper未初始化");
                    return;
                }
                
                // 先注册虚拟端点
                var virtualEndPoint = SteamEndPointMapper.Instance.RegisterSteamID(hostSteamID, 27015);
                Debug.Log($"[SteamLobbyHelper] ✓ 虚拟端点: {virtualEndPoint}");
                
                // 主动接受 P2P 会话
                if (SteamP2PManager.Instance != null)
                {
                    bool accepted = SteamP2PManager.Instance.AcceptP2PSession(hostSteamID);
                    Debug.Log($"[SteamLobbyHelper] 主动接受P2P会话: {(accepted ? "成功" : "失败")}");
                }
                
                Debug.Log($"[SteamLobbyHelper] ⏳ 等待P2P会话建立...");
                
                // 启动协程等待 P2P 会话建立
                SteamEndPointMapper.Instance.StartCoroutine(
                    WaitForP2PAndConnect(hostSteamID, virtualEndPoint)
                );
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamLobbyHelper] ❌❌❌ 触发连接失败: {ex}");
                Debug.LogError($"[SteamLobbyHelper] 堆栈: {ex.StackTrace}");
            }
        }
        
        private static System.Collections.IEnumerator WaitForP2PAndConnect(CSteamID hostSteamID, System.Net.IPEndPoint virtualEndPoint)
        {
            float startTime = UnityEngine.Time.time;
            float timeout = 15f; // 15秒超时
            bool sessionEstablished = false;
            
            Debug.Log($"[SteamLobbyHelper] 开始等待P2P会话建立，超时时间: {timeout}秒");
            
            // 持续检查 P2P 会话状态
            while (UnityEngine.Time.time - startTime < timeout)
            {
                if (Steamworks.SteamNetworking.GetP2PSessionState(hostSteamID, out Steamworks.P2PSessionState_t state))
                {
                    Debug.Log($"[SteamLobbyHelper] P2P会话状态检查 - 连接活跃: {state.m_bConnectionActive}, " +
                             $"正在连接: {state.m_bConnecting}, 使用中继: {state.m_bUsingRelay}, " +
                             $"发送队列: {state.m_nBytesQueuedForSend}");
                    
                    if (state.m_bConnectionActive == 1)
                    {
                        sessionEstablished = true;
                        Debug.Log($"[SteamLobbyHelper] ✓ P2P会话已建立！耗时: {UnityEngine.Time.time - startTime:F2}秒");
                        break;
                    }
                }
                
                // 每0.5秒检查一次
                yield return new UnityEngine.WaitForSeconds(0.5f);
            }
            
            if (!sessionEstablished)
            {
                Debug.LogError($"[SteamLobbyHelper] ❌ P2P会话建立超时（{timeout}秒）");
                Debug.LogError($"[SteamLobbyHelper] 可能原因：");
                Debug.LogError($"  1. 主机未正确初始化 Steam P2P");
                Debug.LogError($"  2. NAT 穿透失败（需要配置路由器或使用中继）");
                Debug.LogError($"  3. 防火墙阻止了 Steam P2P 连接");
                yield break;
            }
            
            // P2P 会话建立成功，等待额外1秒确保稳定
            Debug.Log($"[SteamLobbyHelper] 等待1秒确保P2P会话稳定...");
            yield return new UnityEngine.WaitForSeconds(1f);
            
            // 开始连接（主机 SteamID 已在 ChatTransportBridge.InitializeTransport 中注册）
            Debug.Log($"[SteamLobbyHelper] ✓ P2P会话已就绪，开始连接到 {virtualEndPoint}");
            NetService.Instance.ConnectToHost(virtualEndPoint.Address.ToString(), virtualEndPoint.Port);
        }

        public static void TriggerMultiplayerHost()
        {
            // 设置传输模式为 Steam P2P（主机创建 Lobby）
            NetService.Instance.SetTransportMode(NetworkTransportMode.SteamP2P);
            Debug.Log($"[SteamLobbyHelper] ✓ 设置传输模式为 Steam P2P");
            
            NetService.Instance.StartNetwork(true, keepSteamLobby: true);
        }
    }
}
