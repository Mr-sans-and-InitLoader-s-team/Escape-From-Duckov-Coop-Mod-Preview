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
