// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team

using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using Steamworks;
using System;
using System.Net;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Patch.SteamP2P
{
    // NetManager.Connect - 拦截连接请求，在Steam Lobby中自动注册虚拟IP
    [HarmonyPatch(typeof(NetManager), "Connect", new Type[] { typeof(string), typeof(int), typeof(NetDataWriter) })]
    public class Patch_NetManager_Connect
    {
        static bool Prefix(string address, int port, NetDataWriter connectionData, ref NetPeer __result)
        {
            if (!Net.Steam.SteamP2PLoader.Instance.UseSteamP2P || !SteamManager.Initialized)
                return true;
                
            try
            {
                Debug.Log("[Patch_Connect] 尝试连接到: " + address + ":" + port);
                
                if (Net.Steam.SteamLobbyManager.Instance != null && Net.Steam.SteamLobbyManager.Instance.IsInLobby)
                {
                    CSteamID hostSteamID = Net.Steam.SteamLobbyManager.Instance.GetLobbyOwner();
                    if (hostSteamID != CSteamID.Nil)
                    {
                        Debug.Log("[Patch_Connect] 检测到Lobby连接，主机Steam ID: " + hostSteamID);
                        
                        if (Net.Steam.SteamEndPointMapper.Instance != null)
                        {
                            IPEndPoint virtualEndPoint = Net.Steam.SteamEndPointMapper.Instance.RegisterSteamID(hostSteamID, port);
                            Debug.Log("[Patch_Connect] 主机映射为虚拟IP: " + virtualEndPoint);
                        }
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[Patch_Connect] 异常: " + ex);
                return true;
            }
        }
    }
    
    // NetPeer.SendInternal - 记录数据包签名，用于后续识别传输模式
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

