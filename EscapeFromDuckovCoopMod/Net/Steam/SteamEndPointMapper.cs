// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team

using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Net.Steam
{
    public class SteamEndPointMapper : MonoBehaviour
    {
        public static SteamEndPointMapper Instance { get; private set; }
        
        private Dictionary<CSteamID, IPEndPoint> _steamToEndPoint = new Dictionary<CSteamID, IPEndPoint>();
        private Dictionary<IPEndPoint, CSteamID> _endPointToSteam = new Dictionary<IPEndPoint, CSteamID>();
        private int _virtualIpCounter = 1;
        
        private const byte VirtualIpPrefix1 = 10;
        private const byte VirtualIpPrefix2 = 255;
        
        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("[SteamEndPointMapper] 初始化完成");
        }
        
        public System.Collections.IEnumerator WaitForP2PSessionEstablished(CSteamID remoteSteamID, System.Action<bool> callback, float timeout = 10f)
        {
            float elapsed = 0f;
            
            Debug.Log("[SteamEndPointMapper] 等待P2P会话建立: " + remoteSteamID);
            
            while (elapsed < timeout)
            {
                if (SteamManager.Initialized && SteamNetworking.GetP2PSessionState(remoteSteamID, out P2PSessionState_t sessionState))
                {
                    if (sessionState.m_bConnectionActive == 1)
                    {
                        Debug.Log("[SteamEndPointMapper] P2P会话已建立: " + remoteSteamID);
                        callback?.Invoke(true);
                        yield break;
                    }
                }
                
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }
            
            Debug.LogWarning("[SteamEndPointMapper] P2P会话建立超时: " + remoteSteamID);
            callback?.Invoke(false);
        }
        
        public IPEndPoint RegisterSteamID(CSteamID steamID, int port = 27015)
        {
            CSteamID mySteamID = SteamUser.GetSteamID();
            if (steamID == mySteamID)
            {
                Debug.Log("[SteamEndPointMapper] 跳过注册本地玩家的SteamID: " + steamID);
                return null;
            }
            
            if (_steamToEndPoint.TryGetValue(steamID, out IPEndPoint existingEndPoint))
            {
                Debug.Log("[SteamEndPointMapper] Steam ID " + steamID + " 已注册为 " + existingEndPoint);
                return existingEndPoint;
            }
            
            IPEndPoint virtualEndPoint = GenerateVirtualEndPoint(port);
            _steamToEndPoint[steamID] = virtualEndPoint;
            _endPointToSteam[virtualEndPoint] = steamID;
            
            Debug.Log("[SteamEndPointMapper] ========== 虚拟IP映射 ==========");
            Debug.Log("[SteamEndPointMapper] SteamID: " + steamID);
            Debug.Log("[SteamEndPointMapper] 虚拟IP: " + virtualEndPoint.Address + ":" + virtualEndPoint.Port);
            Debug.Log("[SteamEndPointMapper] 当前映射数量: " + _steamToEndPoint.Count);
            
            if (SteamManager.Initialized)
            {
                bool accepted = SteamNetworking.AcceptP2PSessionWithUser(steamID);
                
                byte[] handshake = System.Text.Encoding.UTF8.GetBytes("HANDSHAKE");
                for (int i = 0; i < 3; i++)
                {
                    SteamNetworking.SendP2PPacket(
                        steamID, handshake, (uint)handshake.Length,
                        EP2PSend.k_EP2PSendUnreliableNoDelay, 0
                    );
                }
                
                bool sent = SteamNetworking.SendP2PPacket(
                    steamID, handshake, (uint)handshake.Length,
                    EP2PSend.k_EP2PSendReliable, 0
                );
                
                Debug.Log("[SteamEndPointMapper] NAT穿透握手: " + (sent ? "成功" : "失败"));
            }
            
            return virtualEndPoint;
        }
        
        public bool TryGetSteamID(IPEndPoint endPoint, out CSteamID steamID)
        {
            return _endPointToSteam.TryGetValue(endPoint, out steamID);
        }
        
        public bool TryGetEndPoint(CSteamID steamID, out IPEndPoint endPoint)
        {
            return _steamToEndPoint.TryGetValue(steamID, out endPoint);
        }
        
        public void UnregisterSteamID(CSteamID steamID)
        {
            if (_steamToEndPoint.TryGetValue(steamID, out IPEndPoint endPoint))
            {
                _steamToEndPoint.Remove(steamID);
                _endPointToSteam.Remove(endPoint);
                Debug.Log("[SteamEndPointMapper] 移除映射: " + steamID + " <-> " + endPoint);
            }
        }
        
        public void UnregisterEndPoint(IPEndPoint endPoint)
        {
            if (_endPointToSteam.TryGetValue(endPoint, out CSteamID steamID))
            {
                _endPointToSteam.Remove(endPoint);
                _steamToEndPoint.Remove(steamID);
                Debug.Log("[SteamEndPointMapper] 移除映射: " + steamID + " <-> " + endPoint);
            }
        }
        
        public bool IsVirtualEndPoint(IPEndPoint endPoint)
        {
            if (endPoint == null)
                return false;
                
            byte[] addressBytes = endPoint.Address.GetAddressBytes();
            if (addressBytes.Length == 4)
            {
                return addressBytes[0] == VirtualIpPrefix1 && addressBytes[1] == VirtualIpPrefix2;
            }
            return false;
        }
        
        public List<CSteamID> GetAllSteamIDs()
        {
            return _steamToEndPoint.Keys.ToList();
        }
        
        public List<IPEndPoint> GetAllEndPoints()
        {
            return _endPointToSteam.Keys.ToList();
        }
        
        public void ClearAll()
        {
            _steamToEndPoint.Clear();
            _endPointToSteam.Clear();
            _virtualIpCounter = 1;
            Debug.Log("[SteamEndPointMapper] 已清空所有映射");
        }
        
        public void OnP2PSessionEstablished(CSteamID remoteSteamID)
        {
            if (!_steamToEndPoint.ContainsKey(remoteSteamID))
            {
                RegisterSteamID(remoteSteamID);
            }
        }
        
        public void OnP2PSessionFailed(CSteamID remoteSteamID)
        {
            UnregisterSteamID(remoteSteamID);
        }
        
        private IPEndPoint GenerateVirtualEndPoint(int port)
        {
            byte byte3 = (byte)(_virtualIpCounter / 256);
            byte byte4 = (byte)(_virtualIpCounter % 256);
            _virtualIpCounter++;
            
            if (_virtualIpCounter > 65535)
            {
                _virtualIpCounter = 1;
            }
            
            IPAddress virtualIP = new IPAddress(new byte[] { VirtualIpPrefix1, VirtualIpPrefix2, byte3, byte4 });
            return new IPEndPoint(virtualIP, port);
        }
        
        public string GetMappingStats()
        {
            return "[SteamEndPointMapper] 当前映射数: " + _steamToEndPoint.Count;
        }
        
        private void OnDestroy()
        {
            Debug.Log(GetMappingStats());
            ClearAll();
        }
    }
}

