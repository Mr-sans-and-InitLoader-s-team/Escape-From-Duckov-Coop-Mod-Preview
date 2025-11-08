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
                return true;
            try
            {
                Debug.Log($"[Patch_Connect] 尝试连接到: {address}:{port}");
                if (SteamLobbyManager.Instance != null && SteamLobbyManager.Instance.IsInLobby)
                {
                    CSteamID hostSteamID = SteamLobbyManager.Instance.GetLobbyOwner();
                    if (hostSteamID != CSteamID.Nil)
                    {
                        Debug.Log($"[Patch_Connect] 检测到Lobby连接，主机Steam ID: {hostSteamID}");
                        if (SteamEndPointMapper.Instance != null)
                        {
                            IPEndPoint virtualEndPoint = SteamEndPointMapper.Instance.RegisterSteamID(hostSteamID, port);
                            Debug.Log($"[Patch_Connect] 主机映射为虚拟IP: {virtualEndPoint}");
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Patch_Connect] 异常: {ex}");
                return true;
            }
        }
    }


    // 🛡️ 修复：Patch Send 方法以获取通道号
    // 注意：参数名必须与 LiteNetLib.NetPeer.Send 的实际签名完全匹配
    [HarmonyPatch(typeof(NetPeer), "Send", new Type[] { typeof(byte[]), typeof(int), typeof(int), typeof(byte), typeof(DeliveryMethod) })]
    public class Patch_NetPeer_Send_WithChannel
    {
        static void Prefix(byte[] data, int start, int length, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            PacketSignature.Register(data, start, length, deliveryMethod, channelNumber);
        }
    }

    // 保留旧 Patch 以兼容不带通道号的调用
    [HarmonyPatch(typeof(NetPeer), "SendInternal", MethodType.Normal)]
    public class Patch_NetPeer_Send
    {
        private static int _patchedCount = 0;
        static void Prefix(byte[] data, int start, int length, DeliveryMethod deliveryMethod)
        {
            PacketSignature.Register(data, start, length, deliveryMethod, 0);
            _patchedCount++;
        }
    }


















}
