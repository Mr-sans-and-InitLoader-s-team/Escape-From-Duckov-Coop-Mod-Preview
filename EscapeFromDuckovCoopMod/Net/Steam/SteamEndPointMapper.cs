using Steamworks;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace EscapeFromDuckovCoopMod
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
        public IPEndPoint RegisterSteamID(CSteamID steamID, int port = 27015)
        {
            if (_steamToEndPoint.TryGetValue(steamID, out IPEndPoint existingEndPoint))
            {
                Debug.Log($"[SteamEndPointMapper] Steam ID {steamID} 已注册为 {existingEndPoint}");
                return existingEndPoint;
            }
            IPEndPoint virtualEndPoint = GenerateVirtualEndPoint(port);
            _steamToEndPoint[steamID] = virtualEndPoint;
            _endPointToSteam[virtualEndPoint] = steamID;
            if (SteamManager.Initialized)
            {
                bool accepted = Steamworks.SteamNetworking.AcceptP2PSessionWithUser(steamID);
                byte[] handshake = System.Text.Encoding.UTF8.GetBytes("HANDSHAKE");
                for (int i = 0; i < 3; i++)
                {
                    Steamworks.SteamNetworking.SendP2PPacket(
                        steamID, handshake, (uint)handshake.Length,
                        Steamworks.EP2PSend.k_EP2PSendUnreliableNoDelay, 0
                    );
                }
                bool sent = Steamworks.SteamNetworking.SendP2PPacket(
                    steamID, handshake, (uint)handshake.Length,
                    Steamworks.EP2PSend.k_EP2PSendReliable, 0
                );
                Steamworks.P2PSessionState_t state;
                if (Steamworks.SteamNetworking.GetP2PSessionState(steamID, out state))
                {
                }
            }
            return virtualEndPoint;
        }
        public System.Collections.IEnumerator WaitForP2PSessionEstablished(CSteamID steamID, System.Action<bool> callback, float timeoutSeconds = 10f)
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError($"[SteamEndPointMapper] Steam未初始化");
                callback?.Invoke(false);
                yield break;
            }
            float startTime = Time.time;
            bool sessionEstablished = false;
            while (Time.time - startTime < timeoutSeconds)
            {
                Steamworks.P2PSessionState_t state;
                if (Steamworks.SteamNetworking.GetP2PSessionState(steamID, out state))
                {
                    if (state.m_bConnectionActive == 1)
                    {
                        sessionEstablished = true;
                        break;
                    }
                }
                yield return null;
            }
            if (!sessionEstablished)
            {
                Debug.LogError($"[SteamEndPointMapper] ❌ P2P会话建立超时（{timeoutSeconds}秒）");
                callback?.Invoke(false);
            }
            else
            {
                callback?.Invoke(true);
            }
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
                Debug.Log($"[SteamEndPointMapper] 移除映射: {steamID} <-> {endPoint}");
            }
        }
        public void UnregisterEndPoint(IPEndPoint endPoint)
        {
            if (_endPointToSteam.TryGetValue(endPoint, out CSteamID steamID))
            {
                _endPointToSteam.Remove(endPoint);
                _steamToEndPoint.Remove(steamID);
                Debug.Log($"[SteamEndPointMapper] 移除映射: {steamID} <-> {endPoint}");
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
        public CSteamID GetOrCreateSteamIDForRealEndPoint(IPEndPoint realEndPoint)
        {
            if (_endPointToSteam.TryGetValue(realEndPoint, out CSteamID existingSteamID))
            {
                return existingSteamID;
            }
            return CSteamID.Nil;
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
            return $"[SteamEndPointMapper] 当前映射数: {_steamToEndPoint.Count}";
        }
        private void OnDestroy()
        {
            Debug.Log(GetMappingStats());
            ClearAll();
        }
    }
}
