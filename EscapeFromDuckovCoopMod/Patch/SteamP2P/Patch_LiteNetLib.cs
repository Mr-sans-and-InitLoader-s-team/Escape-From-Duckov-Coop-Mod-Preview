using Steamworks;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace EscapeFromDuckovCoopMod
{
    [HarmonyPatch(typeof(NetManager), "Connect", new Type[] { typeof(string), typeof(int), typeof(LiteNetLib.Utils.NetDataWriter) })]
    public class Patch_NetManager_Connect
    {
        static bool Prefix(string address, int port, LiteNetLib.Utils.NetDataWriter connectionData, ref NetPeer __result)
        {
            if (!SteamP2PLoader.Instance.UseSteamP2P || !SteamManager.Initialized)
            {
                Debug.Log($"[Patch_Connect] 使用原生连接: UseSteamP2P={SteamP2PLoader.Instance.UseSteamP2P}, SteamInit={SteamManager.Initialized}");
                return true;
            }
            try
            {
                Debug.Log($"[Patch_Connect] ========== 开始P2P连接流程 ==========");
                Debug.Log($"[Patch_Connect] 目标: {address}:{port}");
                
                if (SteamLobbyManager.Instance != null && SteamLobbyManager.Instance.IsInLobby)
                {
                    CSteamID hostSteamID = SteamLobbyManager.Instance.GetLobbyOwner();
                    CSteamID localSteamID = SteamUser.GetSteamID();
                    
                    Debug.Log($"[Patch_Connect] Lobby信息:");
                    Debug.Log($"  - 主机Steam ID: {hostSteamID}");
                    Debug.Log($"  - 本地Steam ID: {localSteamID}");
                    Debug.Log($"  - Lobby成员数: {SteamMatchmaking.GetNumLobbyMembers(SteamLobbyManager.Instance.CurrentLobbyId)}");
                    
                    if (hostSteamID != CSteamID.Nil)
                    {
                        Debug.Log($"[Patch_Connect] 检测到有效的主机Steam ID");
                        
                        if (VirtualEndpointManager.Instance != null)
                        {
                            IPEndPoint virtualEndPoint = VirtualEndpointManager.Instance.RegisterOrUpdateSteamID(hostSteamID, port);
                            Debug.Log($"[Patch_Connect] 主机映射为虚拟IP: {virtualEndPoint}");
                            
                            Steamworks.P2PSessionState_t p2pState;
                            if (Steamworks.SteamNetworking.GetP2PSessionState(hostSteamID, out p2pState))
                            {
                                Debug.Log($"[Patch_Connect] P2P会话状态:");
                                Debug.Log($"  - Active: {p2pState.m_bConnectionActive == 1}");
                                Debug.Log($"  - UsingRelay: {p2pState.m_bUsingRelay == 1}");
                                Debug.Log($"  - QueuedBytes: {p2pState.m_nBytesQueuedForSend}");
                            }
                            else
                            {
                                Debug.LogWarning($"[Patch_Connect] 无法获取P2P会话状态");
                            }
                        }
                        else
                        {
                            Debug.LogError($"[Patch_Connect] VirtualEndpointManager未初始化");
                        }
                    }
                    else
                    {
                        Debug.LogError($"[Patch_Connect] 无效的主机Steam ID");
                    }
                }
                else
                {
                    Debug.LogWarning($"[Patch_Connect] 不在Lobby中，使用原生连接");
                }
                
                Debug.Log($"[Patch_Connect] ========== P2P连接流程结束 ==========");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Patch_Connect] 异常: {ex}");
                Debug.LogError($"[Patch_Connect] 堆栈: {ex.StackTrace}");
                return true;
            }
        }
    }


    [HarmonyPatch(typeof(NetPeer), "SendInternal", MethodType.Normal)]
    public class Patch_NetPeer_Send
    {
        private static int _patchedCount = 0;
        static void Prefix(byte[] data, int start, int length, DeliveryMethod deliveryMethod)
        {
            PacketSignature.Register(data, start, length, deliveryMethod);
            _patchedCount++;
        }
    }


















}
