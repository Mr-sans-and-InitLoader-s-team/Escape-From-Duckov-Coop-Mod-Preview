using Steamworks;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace EscapeFromDuckovCoopMod
{
    public class VirtualEndpointManager : MonoBehaviour
    {
        public static VirtualEndpointManager Instance { get; private set; }

        private readonly ConcurrentDictionary<CSteamID, IPEndPoint> _steamToVirtual = new();
        private readonly ConcurrentDictionary<IPEndPoint, CSteamID> _virtualToSteam = new();
        
        private int _virtualIpCounter = 1;
        private const byte VirtualIpPrefix1 = 10;
        private const byte VirtualIpPrefix2 = 255;
        
        private readonly object _mappingLock = new object();
        private static volatile bool _steamInitialized = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.Log("[VirtualEndpoint] 实例已存在，销毁重复实例");
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            _steamInitialized = SteamManager.Initialized;
            Debug.Log($"[VirtualEndpoint] 虚拟端点管理器初始化完成，Steam状态: {_steamInitialized}");
        }

        public IPEndPoint RegisterOrUpdateSteamID(CSteamID steamID, int port = 27015)
        {
            if (!_steamInitialized)
            {
                _steamInitialized = SteamManager.Initialized;
                if (!_steamInitialized)
                {
                    Debug.LogError("[VirtualEndpoint] Steam未初始化，无法注册");
                    return null;
                }
            }

            lock (_mappingLock)
            {
                if (_steamToVirtual.TryGetValue(steamID, out var existing))
                {
                    Debug.Log($"[VirtualEndpoint] 复用映射 {steamID.m_SteamID} -> {existing}");
                    return existing;
                }

                var virtualEP = GenerateVirtualEndPoint(port);
                _steamToVirtual[steamID] = virtualEP;
                _virtualToSteam[virtualEP] = steamID;

                var localSteamId = SteamUser.GetSteamID();
                Debug.Log($"[VirtualEndpoint] 新映射: {steamID.m_SteamID} -> {virtualEP} (本地:{localSteamId.m_SteamID})");
                
                InitializeP2PSession(steamID);
                
                return virtualEP;
            }
        }

        private void InitializeP2PSession(CSteamID steamID)
        {
            try
            {
                Debug.Log($"[VirtualEndpoint] 初始化P2P会话: {steamID.m_SteamID}");
                
                bool accepted = SteamNetworking.AcceptP2PSessionWithUser(steamID);
                Debug.Log($"[VirtualEndpoint] AcceptP2PSession结果: {accepted}");
                
                byte[] handshake = System.Text.Encoding.UTF8.GetBytes("HANDSHAKE");
                
                for (int i = 0; i < 3; i++)
                {
                    bool sent = SteamNetworking.SendP2PPacket(steamID, handshake, (uint)handshake.Length, 
                        EP2PSend.k_EP2PSendUnreliableNoDelay, 0);
                    Debug.Log($"[VirtualEndpoint] 发送握手包#{i+1}/3 (不可靠): {sent}");
                }
                
                bool reliableSent = SteamNetworking.SendP2PPacket(steamID, handshake, (uint)handshake.Length, 
                    EP2PSend.k_EP2PSendReliable, 0);
                Debug.Log($"[VirtualEndpoint] 发送握手包 (可靠): {reliableSent}");

                if (SteamNetworking.GetP2PSessionState(steamID, out P2PSessionState_t state))
                {
                    Debug.Log($"[VirtualEndpoint] 初始会话状态: Active={state.m_bConnectionActive}, Relay={state.m_bUsingRelay}, Error={state.m_eP2PSessionError}, RemoteIP={state.m_nRemoteIP}:{state.m_nRemotePort}");
                }
                else
                {
                    Debug.LogWarning($"[VirtualEndpoint] 无法获取初始会话状态");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VirtualEndpoint] P2P会话初始化异常: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public bool TryGetSteamID(IPEndPoint virtualEP, out CSteamID steamID)
        {
            bool found = _virtualToSteam.TryGetValue(virtualEP, out steamID);
            if (!found)
            {
                Debug.LogWarning($"[VirtualEndpoint] 未找到虚拟端点映射: {virtualEP}");
            }
            return found;
        }

        public bool TryGetEndpoint(CSteamID steamID, out IPEndPoint endpoint)
        {
            bool found = _steamToVirtual.TryGetValue(steamID, out endpoint);
            if (!found)
            {
                Debug.LogWarning($"[VirtualEndpoint] 未找到Steam ID映射: {steamID.m_SteamID}");
            }
            return found;
        }

        public bool IsVirtualEndpoint(IPEndPoint endpoint)
        {
            if (endpoint == null) return false;
            
            var addressBytes = endpoint.Address.GetAddressBytes();
            if (addressBytes.Length != 4) return false;
            
            return addressBytes[0] == VirtualIpPrefix1 && addressBytes[1] == VirtualIpPrefix2;
        }

        public void UnregisterSteamID(CSteamID steamID)
        {
            lock (_mappingLock)
            {
                if (_steamToVirtual.TryRemove(steamID, out var vEndpoint))
                {
                    _virtualToSteam.TryRemove(vEndpoint, out _);
                    
                    Debug.Log($"[VirtualEndpoint] 移除映射: {steamID.m_SteamID} <-> {vEndpoint}");
                    
                    if (_steamInitialized)
                    {
                        try
                        {
                            SteamNetworking.CloseP2PSessionWithUser(steamID);
                            Debug.Log($"[VirtualEndpoint] 关闭P2P会话: {steamID.m_SteamID}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[VirtualEndpoint] 关闭P2P会话异常: {ex.Message}");
                        }
                    }
                }
            }
        }

        public System.Collections.IEnumerator WaitForSessionEstablished(CSteamID steamID, Action<bool> callback, float timeoutSeconds = 10f)
        {
            if (!_steamInitialized)
            {
                _steamInitialized = SteamManager.Initialized;
                if (!_steamInitialized)
                {
                    Debug.LogError("[VirtualEndpoint] Steam未初始化，无法等待会话");
                    callback?.Invoke(false);
                    yield break;
                }
            }

            Debug.Log($"[VirtualEndpoint] 开始等待P2P会话建立: {steamID.m_SteamID}, 超时: {timeoutSeconds}秒");
            
            float startTime = Time.time;
            int checkCount = 0;
            int handshakeSentCount = 0;
            bool sessionEstablished = false;
            byte[] handshake = System.Text.Encoding.UTF8.GetBytes("P2P_KEEPALIVE");

            while (Time.time - startTime < timeoutSeconds)
            {
                checkCount++;
                
                if (checkCount % 20 == 0)
                {
                    SteamNetworking.SendP2PPacket(steamID, handshake, (uint)handshake.Length, 
                        EP2PSend.k_EP2PSendUnreliableNoDelay, 0);
                    handshakeSentCount++;
                    if (handshakeSentCount % 5 == 1)
                    {
                        Debug.Log($"[VirtualEndpoint] 持续发送握手包 #{handshakeSentCount} 以触发NAT穿透");
                    }
                }
                
                if (SteamNetworking.GetP2PSessionState(steamID, out P2PSessionState_t state))
                {
                    bool isActive = state.m_bConnectionActive == 1;
                    bool usingRelay = state.m_bUsingRelay == 1;
                    
                    if (checkCount % 60 == 0 || checkCount == 1)
                    {
                        Debug.Log($"[VirtualEndpoint] 会话检查 #{checkCount}: Active={isActive}, Relay={usingRelay}, Error={state.m_eP2PSessionError}, QueuedBytes={state.m_nBytesQueuedForSend}, PacketsQueued={state.m_nPacketsQueuedForSend}");
                    }

                    if (isActive)
                    {
                        sessionEstablished = true;
                        float elapsed = Time.time - startTime;
                        Debug.Log($"[VirtualEndpoint] P2P会话建立成功: {steamID.m_SteamID}, 耗时: {elapsed:F2}秒, 检查次数: {checkCount}, 握手包: {handshakeSentCount}, 使用中继: {usingRelay}");
                        break;
                    }
                }
                else
                {
                    if (checkCount % 60 == 0)
                    {
                        Debug.LogWarning($"[VirtualEndpoint] 无法获取P2P会话状态 #{checkCount}");
                    }
                }

                yield return null;
            }

            if (!sessionEstablished)
            {
                float totalTime = Time.time - startTime;
                Debug.LogError($"[VirtualEndpoint] P2P会话建立超时: {steamID.m_SteamID}, 耗时: {totalTime:F2}秒, 检查次数: {checkCount}");
                
                if (SteamNetworking.GetP2PSessionState(steamID, out P2PSessionState_t failState))
                {
                    Debug.LogError($"[VirtualEndpoint] 最终会话状态: Active={failState.m_bConnectionActive}, Relay={failState.m_bUsingRelay}, Error={failState.m_eP2PSessionError}");
                }
                
                SteamNetworking.CloseP2PSessionWithUser(steamID);
                UnregisterSteamID(steamID);
            }
            
            callback?.Invoke(sessionEstablished);
        }

        private IPEndPoint GenerateVirtualEndPoint(int port)
        {
            lock (_mappingLock)
            {
                byte byte3 = (byte)(_virtualIpCounter / 256);
                byte byte4 = (byte)(_virtualIpCounter % 256);
                _virtualIpCounter++;
                
                if (_virtualIpCounter > 65535)
                {
                    Debug.LogWarning("[VirtualEndpoint] 虚拟IP计数器溢出，重置为1");
                    _virtualIpCounter = 1;
                }

                IPAddress virtualIP = new IPAddress(new byte[] { VirtualIpPrefix1, VirtualIpPrefix2, byte3, byte4 });
                IPEndPoint ep = new IPEndPoint(virtualIP, port);
                Debug.Log($"[VirtualEndpoint] 生成虚拟端点: {ep} (计数器: {_virtualIpCounter - 1})");
                return ep;
            }
        }

        public void ClearAll()
        {
            lock (_mappingLock)
            {
                Debug.Log($"[VirtualEndpoint] 清空所有映射，当前数量: {_steamToVirtual.Count}");
                
                if (_steamInitialized)
                {
                    foreach (var steamID in _steamToVirtual.Keys.ToArray())
                    {
                        try
                        {
                            SteamNetworking.CloseP2PSessionWithUser(steamID);
                            Debug.Log($"[VirtualEndpoint] 关闭P2P会话: {steamID.m_SteamID}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[VirtualEndpoint] 关闭P2P会话异常 ({steamID.m_SteamID}): {ex.Message}");
                        }
                    }
                }

                _steamToVirtual.Clear();
                _virtualToSteam.Clear();
                _virtualIpCounter = 1;
                
                Debug.Log("[VirtualEndpoint] 已清空所有映射");
            }
        }

        public string GetDiagnosticInfo()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[VirtualEndpoint] 诊断信息:");
            sb.AppendLine($"  - 总映射数: {_steamToVirtual.Count}");
            sb.AppendLine($"  - Steam初始化: {_steamInitialized}");
            sb.AppendLine($"  - 虚拟IP计数器: {_virtualIpCounter}");

            if (_steamInitialized)
            {
                foreach (var kvp in _steamToVirtual.Take(10))
                {
                    var steamID = kvp.Key;
                    var vEndpoint = kvp.Value;
                    
                    string sessionInfo = "未知";
                    try
                    {
                        if (SteamNetworking.GetP2PSessionState(steamID, out P2PSessionState_t state))
                        {
                            sessionInfo = $"Active={state.m_bConnectionActive}, Relay={state.m_bUsingRelay}, Error={state.m_eP2PSessionError}";
                        }
                    }
                    catch (Exception ex)
                    {
                        sessionInfo = $"异常: {ex.Message}";
                    }
                    
                    sb.AppendLine($"  - {steamID.m_SteamID} -> {vEndpoint} ({sessionInfo})");
                }
            }
            else
            {
                sb.AppendLine("  - Steam未初始化，无法获取会话信息");
            }

            return sb.ToString();
        }

        private void OnDestroy()
        {
            Debug.Log("[VirtualEndpoint] OnDestroy开始");
            Debug.Log(GetDiagnosticInfo());
            ClearAll();
            Debug.Log("[VirtualEndpoint] OnDestroy完成");
            
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
