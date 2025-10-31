// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team

using HarmonyLib;
using LiteNetLib;
using Steamworks;
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Patch.SteamP2P
{
    // Socket.Available - 检查是否有数据可读
    [HarmonyPatch(typeof(Socket), "get_Available")]
    public class Patch_Socket_Available
    {
        static void Postfix(Socket __instance, ref int __result)
        {
            if (!Net.Steam.SteamP2PLoader.Instance.UseSteamP2P || !SteamManager.Initialized)
                return;
                
            try
            {
                if (__result > 0)
                    return;
                    
                if (SteamManager.Initialized && SteamNetworking.IsP2PPacketAvailable(out uint packetSize, 0))
                {
                    __result = (int)packetSize;
                }
                else if (Net.Steam.SteamP2PManager.Instance != null)
                {
                    int queueSize = Net.Steam.SteamP2PManager.Instance.GetQueueSize();
                    if (queueSize > 0)
                    {
                        __result = queueSize * 200;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[Patch_Available] 异常: " + ex);
            }
        }
    }
    
    // Socket.Poll - 检查Socket状态
    [HarmonyPatch(typeof(Socket), nameof(Socket.Poll))]
    public class Patch_Socket_Poll
    {
        [ThreadStatic]
        private static bool _inPatch = false;
        
        static bool Prefix(Socket __instance, ref int microSeconds, SelectMode mode, ref bool __result)
        {
            if (_inPatch)
                return true;
                
            if (!Net.Steam.SteamP2PLoader.Instance.UseSteamP2P || !SteamManager.Initialized)
                return true;
                
            try
            {
                _inPatch = true;
                
                if (mode != SelectMode.SelectRead)
                    return true;
                    
                if (SteamManager.Initialized && SteamNetworking.IsP2PPacketAvailable(out uint packetSize, 0))
                {
                    __result = true;
                    return false;
                }
                else if (Net.Steam.SteamP2PManager.Instance != null)
                {
                    int queueSize = Net.Steam.SteamP2PManager.Instance.GetQueueSize();
                    if (queueSize > 0)
                    {
                        __result = true;
                        return false;
                    }
                }
                
                if (microSeconds > 100)
                {
                    microSeconds = 100;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[Patch_Poll] 异常: " + ex);
                return true;
            }
            finally
            {
                _inPatch = false;
            }
        }
    }
    
    // Socket.ReceiveFrom - 接收数据
    [HarmonyPatch]
    public class Patch_Socket_ReceiveFrom
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Socket), "ReceiveFrom", new Type[]
            {
                typeof(byte[]),
                typeof(int),
                typeof(int),
                typeof(SocketFlags),
                typeof(EndPoint).MakeByRefType()
            });
        }
        
        static bool Prefix(Socket __instance, byte[] buffer, int offset, int size, SocketFlags socketFlags, ref EndPoint remoteEP, ref int __result)
        {
            if (!Net.Steam.SteamP2PLoader.Instance.UseSteamP2P || !SteamManager.Initialized)
                return true;
                
            try
            {
                if (Net.Steam.SteamP2PManager.Instance != null)
                {
                    if (Net.Steam.SteamP2PManager.Instance.TryReceiveDirectFromSteam(buffer, offset, size, out int receivedLength, out CSteamID steamID, out IPEndPoint endPoint))
                    {
                        remoteEP = endPoint;
                        __result = receivedLength;
                        return false;
                    }
                    
                    if (Net.Steam.SteamP2PManager.Instance.TryGetReceivedPacket(out byte[] data, out int length, out CSteamID remoteSteamID))
                    {
                        if (length > size)
                        {
                            Debug.LogWarning("[Patch_ReceiveFrom] 接收的数据(" + length + " bytes)超过缓冲区大小(" + size + " bytes)");
                            length = size;
                        }
                        
                        Array.Copy(data, 0, buffer, offset, length);
                        
                        IPEndPoint virtualEndPoint = null;
                        if (Net.Steam.SteamEndPointMapper.Instance != null)
                        {
                            if (!Net.Steam.SteamEndPointMapper.Instance.TryGetEndPoint(remoteSteamID, out virtualEndPoint))
                            {
                                virtualEndPoint = Net.Steam.SteamEndPointMapper.Instance.RegisterSteamID(remoteSteamID);
                            }
                        }
                        
                        if (virtualEndPoint != null)
                        {
                            remoteEP = virtualEndPoint;
                            __result = length;
                            return false;
                        }
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[Patch_ReceiveFrom] 异常: " + ex);
                return true;
            }
        }
    }
    
    // Socket.Select - 选择就绪的Socket
    [HarmonyPatch(typeof(Socket), nameof(Socket.Select))]
    public static class Patch_Socket_Select
    {
        static bool Prefix(IList checkRead, IList checkWrite, IList checkError, int microSeconds)
        {
            if (!Net.Steam.SteamP2PLoader.Instance.UseSteamP2P || !SteamManager.Initialized)
            {
                return true;
            }
            
            try
            {
                if (SteamNetworking.IsP2PPacketAvailable(out _, 0))
                {
                    return false;
                }
                
                System.Threading.Thread.Sleep(1);
                checkRead?.Clear();
                checkWrite?.Clear();
                checkError?.Clear();
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError("[Patch_Socket_Select] 异常: " + ex);
                return true;
            }
        }
    }
    
    // Socket.SendTo - 发送数据
    [HarmonyPatch]
    public class Patch_Socket_SendTo
    {
        private static int _diagCount = 0;
        
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Socket), "SendTo", new Type[]
            {
                typeof(byte[]),
                typeof(int),
                typeof(int),
                typeof(SocketFlags),
                typeof(EndPoint)
            });
        }
        
        static bool Prefix(Socket __instance, byte[] buffer, int offset, int size, SocketFlags socketFlags, EndPoint remoteEP, ref int __result)
        {
            if (!Net.Steam.SteamP2PLoader.Instance.UseSteamP2P || !SteamManager.Initialized)
                return true;
                
            try
            {
                IPEndPoint ipEndPoint = remoteEP as IPEndPoint;
                if (ipEndPoint == null)
                {
                    return true;
                }
                
                if (Net.Steam.SteamEndPointMapper.Instance != null &&
                    Net.Steam.SteamEndPointMapper.Instance.IsVirtualEndPoint(ipEndPoint))
                {
                    if (Net.Steam.SteamEndPointMapper.Instance.TryGetSteamID(ipEndPoint, out CSteamID targetSteamID))
                    {
                        DeliveryMethod? deliveryMethod = PacketSignature.TryGetDeliveryMethod(buffer, offset, size);
                        _diagCount++;
                        
                        EP2PSend sendMode;
                        if (deliveryMethod == null && size > offset)
                        {
                            byte packetProperty = (byte)(buffer[offset] & 0x1F);
                            switch (packetProperty)
                            {
                                case 0:
                                    deliveryMethod = DeliveryMethod.Unreliable;
                                    break;
                                case 1:
                                    deliveryMethod = DeliveryMethod.ReliableOrdered;
                                    break;
                                case 2:
                                    deliveryMethod = DeliveryMethod.ReliableOrdered;
                                    break;
                                case 3:
                                case 4:
                                    deliveryMethod = DeliveryMethod.Unreliable;
                                    break;
                                case 5:
                                case 6:
                                case 7:
                                    deliveryMethod = DeliveryMethod.ReliableOrdered;
                                    break;
                                default:
                                    deliveryMethod = DeliveryMethod.ReliableOrdered;
                                    break;
                            }
                        }
                        
                        switch (deliveryMethod ?? DeliveryMethod.ReliableOrdered)
                        {
                            case DeliveryMethod.Unreliable:
                                sendMode = EP2PSend.k_EP2PSendUnreliableNoDelay;
                                break;
                            case DeliveryMethod.Sequenced:
                                sendMode = EP2PSend.k_EP2PSendUnreliableNoDelay;
                                break;
                            case DeliveryMethod.ReliableOrdered:
                                sendMode = EP2PSend.k_EP2PSendReliable;
                                break;
                            case DeliveryMethod.ReliableUnordered:
                                sendMode = EP2PSend.k_EP2PSendReliable;
                                break;
                            case DeliveryMethod.ReliableSequenced:
                                sendMode = EP2PSend.k_EP2PSendReliable;
                                break;
                            default:
                                sendMode = EP2PSend.k_EP2PSendReliable;
                                break;
                        }
                        
                        if (_diagCount % 1000 == 0)
                        {
                            if (SteamNetworking.GetP2PSessionState(targetSteamID, out P2PSessionState_t sessionState))
                            {
                                if (sessionState.m_nBytesQueuedForSend > 50000)
                                {
                                    Debug.LogWarning("[Patch_SendTo] 发送队列积压: " + sessionState.m_nBytesQueuedForSend + " bytes");
                                }
                            }
                        }
                        
                        bool success = Net.Steam.SteamP2PManager.Instance.SendPacket(
                            targetSteamID,
                            buffer,
                            offset,
                            size,
                            sendMode
                        );
                        
                        if (success)
                        {
                            __result = size;
                            return false;
                        }
                        else
                        {
                            Debug.LogError("[Patch_SendTo] Steam P2P发送失败！DeliveryMethod=" + deliveryMethod + ", Size=" + size);
                            return true;
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[Patch_SendTo] 虚拟端点 " + ipEndPoint + " 没有对应的Steam ID映射");
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[Patch_SendTo] 异常: " + ex);
                return true;
            }
        }
    }
}

