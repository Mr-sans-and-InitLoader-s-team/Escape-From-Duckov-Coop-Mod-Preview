using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace EscapeFromDuckovCoopMod
{
    [HarmonyPatch(typeof(Socket), "get_Available")]
    public class Patch_Socket_Available
    {
        static void Postfix(Socket __instance, ref int __result)
        {
            if (!SteamP2PLoader.Instance.UseSteamP2P || !SteamManager.Initialized)
                return;
            try
            {
                if (__result > 0)
                    return;

                // 🛡️ 修复：检查所有通道（0-3），支持 LiteNetLib 多通道系统
                if (SteamManager.Initialized)
                {
                    for (int channel = 0; channel < 4; channel++)
                    {
                        if (Steamworks.SteamNetworking.IsP2PPacketAvailable(out uint packetSize, channel))
                        {
                            __result = (int)packetSize;
                            return;
                        }
                    }
                }
                else if (SteamP2PManager.Instance != null)
                {
                    int queueSize = SteamP2PManager.Instance.GetQueueSize();
                    if (queueSize > 0)
                    {
                        __result = queueSize * 200;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Patch_Available] 异常: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Socket), nameof(Socket.Poll))]
    public class Patch_Socket_Poll
    {
        [ThreadStatic]
        private static bool _inPatch = false;
        static bool Prefix(Socket __instance, ref int microSeconds, SelectMode mode, ref bool __result)
        {
            if (_inPatch)
                return true;
            if (!SteamP2PLoader.Instance.UseSteamP2P || !SteamManager.Initialized)
                return true;
            try
            {
                _inPatch = true;
                if (mode != SelectMode.SelectRead)
                    return true;

                // 🛡️ 修复：检查所有通道（0-3），支持 LiteNetLib 多通道系统
                if (SteamManager.Initialized)
                {
                    for (int channel = 0; channel < 4; channel++)
                    {
                        if (Steamworks.SteamNetworking.IsP2PPacketAvailable(out uint packetSize, channel))
                        {
                            __result = true;
                            return false;
                        }
                    }
                }
                else if (SteamP2PManager.Instance != null)
                {
                    int queueSize = SteamP2PManager.Instance.GetQueueSize();
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
                Debug.LogError($"[Patch_Poll] 异常: {ex}");
                return true;
            }
            finally
            {
                _inPatch = false;
            }
        }
    }

    [HarmonyPatch]
    public class Patch_Socket_ReceiveFrom
    {
        private static int _oversizeWarningCount = 0;  // 🛡️ 限制缓冲区警告的频率
        private const int OVERSIZE_WARNING_INTERVAL = 100;  // 每100次只警告1次

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
            if (!SteamP2PLoader.Instance.UseSteamP2P || !SteamManager.Initialized)
                return true;
            try
            {
                if (SteamP2PManager.Instance != null)
                {
                    if (SteamP2PManager.Instance.TryReceiveDirectFromSteam(buffer, offset, size, out int receivedLength, out CSteamID steamID, out IPEndPoint endPoint))
                    {
                        remoteEP = endPoint;
                        __result = receivedLength;
                        return false;
                    }
                    if (SteamP2PManager.Instance.TryGetReceivedPacket(out byte[] data, out int length, out CSteamID remoteSteamID))
                    {
                        if (length > size)
                        {
                            // 🛡️ 限制日志频率：每100次只输出1次
                            _oversizeWarningCount++;
                            if (_oversizeWarningCount == 1 || _oversizeWarningCount % OVERSIZE_WARNING_INTERVAL == 0)
                            {
                                Debug.LogWarning($"[Patch_ReceiveFrom] 接收的数据({length} bytes)超过缓冲区大小({size} bytes) (已发生 {_oversizeWarningCount} 次)");
                            }
                            length = size;
                        }
                        Array.Copy(data, 0, buffer, offset, length);
                        IPEndPoint virtualEndPoint = null;
                        if (SteamEndPointMapper.Instance != null)
                        {
                            if (!SteamEndPointMapper.Instance.TryGetEndPoint(remoteSteamID, out virtualEndPoint))
                            {
                                virtualEndPoint = SteamEndPointMapper.Instance.RegisterSteamID(remoteSteamID);
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
                Debug.LogError($"[Patch_ReceiveFrom] 异常: {ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Socket), nameof(Socket.Select))]
    public static class Patch_Socket_Select
    {
        static bool Prefix(IList checkRead, IList checkWrite, IList checkError, int microSeconds)
        {
            if (!SteamP2PLoader.Instance.UseSteamP2P || !SteamManager.Initialized)
            {
                return true;
            }
            try
            {
                // 🛡️ 修复：检查所有通道（0-3），支持 LiteNetLib 多通道系统
                for (int channel = 0; channel < 4; channel++)
                {
                    if (Steamworks.SteamNetworking.IsP2PPacketAvailable(out _, channel))
                    {
                        return false;
                    }
                }
                System.Threading.Thread.Sleep(1);
                checkRead?.Clear();
                checkWrite?.Clear();
                checkError?.Clear();
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Patch_Socket_Select] 异常: {ex}");
                return true;
            }
        }
    }

    [HarmonyPatch]
    public class Patch_Socket_SendTo
    {
        private static int _diagCount = 0;
        private static int _unmappedWarningCount = 0;  // 🛡️ 限制未映射警告的频率
        private const int UNMAPPED_WARNING_INTERVAL = 300;  // 每300次只警告1次
        private static int _nonIpWarningCount = 0;  // 🛡️ 限制非IP警告的频率
        private const int NON_IP_WARNING_INTERVAL = 100;  // 每100次只警告1次
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
            if (!SteamP2PLoader.Instance.UseSteamP2P || !SteamManager.Initialized)
                return true;
            try
            {
                IPEndPoint ipEndPoint = remoteEP as IPEndPoint;
                if (ipEndPoint == null)
                {
                    // 🛡️ 限制日志频率：每100次只输出1次
                    _nonIpWarningCount++;
                    if (_nonIpWarningCount == 1 || _nonIpWarningCount % NON_IP_WARNING_INTERVAL == 0)
                    {
                        Debug.LogWarning($"[Patch_SendTo] remoteEP不是IPEndPoint类型，使用原始方法 (已发生 {_nonIpWarningCount} 次)");
                    }
                    return true;
                }
                if (SteamEndPointMapper.Instance != null &&
                    SteamEndPointMapper.Instance.IsVirtualEndPoint(ipEndPoint))
                {
                    if (SteamEndPointMapper.Instance.TryGetSteamID(ipEndPoint, out CSteamID targetSteamID))
                    {
                        // 🛡️ 修复：获取通道号
                        byte channel = 0;
                        DeliveryMethod deliveryMethod;
                        if (!PacketSignature.TryGetPacketInfo(buffer, offset, size, out deliveryMethod, out channel))
                        {
                            deliveryMethod = DeliveryMethod.ReliableOrdered;
                            channel = 0;
                        }

                        _diagCount++;
                        EP2PSend sendMode;
                        switch (deliveryMethod)
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
                            Steamworks.P2PSessionState_t sessionState;
                            if (Steamworks.SteamNetworking.GetP2PSessionState(targetSteamID, out sessionState))
                            {
                                if (sessionState.m_nBytesQueuedForSend > 50000)
                                {
                                    Debug.LogWarning($"[Patch_SendTo] ⚠️ 发送队列积压: {sessionState.m_nBytesQueuedForSend} bytes");
                                }
                            }
                        }
                        // 🛡️ 修复：传递通道号
                        bool success = SteamP2PManager.Instance.SendPacket(
                            targetSteamID,
                            buffer,
                            offset,
                            size,
                            sendMode,
                            channel
                        );
                        if (success)
                        {
                            __result = size;
                            return false;
                        }
                        else
                        {
                            Debug.LogError($"[Patch_SendTo] ❌ Steam P2P发送失败！DeliveryMethod={deliveryMethod}, Channel={channel}, Size={size}");
                            return true;
                        }
                    }
                    else
                    {
                        // 🛡️ 限制日志频率：每300次只输出1次，避免刷屏
                        _unmappedWarningCount++;
                        if (_unmappedWarningCount == 1 || _unmappedWarningCount % UNMAPPED_WARNING_INTERVAL == 0)
                        {
                            Debug.LogWarning($"[Patch_SendTo] ❌ 虚拟端点 {ipEndPoint} 没有对应的Steam ID映射 (已发生 {_unmappedWarningCount} 次)");
                            Debug.LogWarning($"[Patch_SendTo] 当前已映射的端点:");
                            var allEndPoints = SteamEndPointMapper.Instance.GetAllEndPoints();
                            foreach (var ep in allEndPoints)
                            {
                                Debug.LogWarning($"  - {ep}");
                            }
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Patch_SendTo] 异常: {ex}");
                return true;
            }
        }
    }











}
