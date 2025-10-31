// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team

﻿using Steamworks;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Net.Steam
{
    public class SteamP2PManager : MonoBehaviour
    {
        public static SteamP2PManager Instance { get; private set; }
        
        private Callback<P2PSessionRequest_t> _p2pSessionRequestCallback;
        private Callback<P2PSessionConnectFail_t> _p2pSessionConnectFailCallback;
        
        private byte[] _receiveBuffer = new byte[1432];
        private byte[] _largeReceiveBuffer = new byte[8192];
        private ConcurrentQueue<ReceivedPacket> _receivedPackets = new ConcurrentQueue<ReceivedPacket>();
        
        private const int MAX_QUEUE_SIZE = 512;
        private const int BATCH_PROCESS_LIMIT = 512;
        
        private int _packetsSent;
        private int _packetsReceived;
        private long _bytesSent;
        private long _bytesReceived;
        
        public int PacketsSent => _packetsSent;
        public int PacketsReceived => _packetsReceived;
        public long BytesSent => _bytesSent;
        public long BytesReceived => _bytesReceived;
        public int PacketsDropped { get; private set; }
        public int SendFailures { get; private set; }
        public int MaxQueueDepth { get; private set; }
        
        private struct ReceivedPacket
        {
            public byte[] Data;
            public int Length;
            public CSteamID RemoteSteamID;
        }
        
        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeSteamCallbacks();
        }
        
        private void InitializeSteamCallbacks()
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamP2P] Steam未初始化，无法设置回调");
                return;
            }
            
            SteamNetworking.AllowP2PPacketRelay(true);
            Debug.Log("[SteamP2P] 已启用中继服务器（用于NAT穿透）");
            
            _p2pSessionRequestCallback = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
            _p2pSessionConnectFailCallback = Callback<P2PSessionConnectFail_t>.Create(OnP2PSessionConnectFail);
            
            Debug.Log("[SteamP2P] Steam回调已设置");
        }
        
        private int _updateCount = 0;
        
        private void Update()
        {
            if (!SteamManager.Initialized || !SteamP2PLoader.Instance.UseSteamP2P)
                return;
                
            _updateCount++;
            if (_updateCount % 1800 == 0)
            {
                if (PacketsReceived == 0 && PacketsSent > 10)
                {
                    Debug.LogWarning("[SteamP2P] 已发送 " + PacketsSent + " 包，但未收到任何回复！");
                }
            }
        }
        
        private HashSet<CSteamID> _acceptedSessions = new HashSet<CSteamID>();
        
        public bool SendPacket(CSteamID targetSteamID, byte[] data, int offset, int length, EP2PSend sendType = EP2PSend.k_EP2PSendUnreliableNoDelay)
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamP2P] Steam未初始化");
                return false;
            }
            
            if (!_acceptedSessions.Contains(targetSteamID))
            {
                SteamNetworking.AcceptP2PSessionWithUser(targetSteamID);
                _acceptedSessions.Add(targetSteamID);
            }
            
            bool success;
            if (offset == 0)
            {
                success = SteamNetworking.SendP2PPacket(targetSteamID, data, (uint)length, sendType, 0);
            }
            else
            {
                byte[] sendData = new byte[length];
                Array.Copy(data, offset, sendData, 0, length);
                success = SteamNetworking.SendP2PPacket(targetSteamID, sendData, (uint)length, sendType, 0);
            }
            
            if (success)
            {
                System.Threading.Interlocked.Increment(ref _packetsSent);
                System.Threading.Interlocked.Add(ref _bytesSent, length);
            }
            else
            {
                SendFailures++;
                if (SendFailures == 1 || SendFailures % 10 == 0)
                {
                    if (SteamNetworking.GetP2PSessionState(targetSteamID, out P2PSessionState_t sessionState))
                    {
                        Debug.LogWarning("[SteamP2P] 发送失败#" + SendFailures + " to " + targetSteamID + " | 连接:" + sessionState.m_bConnectionActive + " | 队列:" + sessionState.m_nBytesQueuedForSend + "B");
                    }
                }
            }
            
            return success;
        }
        
        public bool TryGetReceivedPacket(out byte[] data, out int length, out CSteamID remoteSteamID)
        {
            if (_receivedPackets.TryDequeue(out ReceivedPacket packet))
            {
                data = packet.Data;
                length = packet.Length;
                remoteSteamID = packet.RemoteSteamID;
                return true;
            }
            
            data = null;
            length = 0;
            remoteSteamID = CSteamID.Nil;
            return false;
        }
        
        public int GetQueueSize()
        {
            return _receivedPackets.Count;
        }
        
        public bool TryReceiveDirectFromSteam(byte[] buffer, int offset, int maxSize, out int length, out CSteamID remoteSteamID, out IPEndPoint endPoint)
        {
            length = 0;
            remoteSteamID = CSteamID.Nil;
            endPoint = null;
            
            if (!SteamManager.Initialized)
                return false;
                
            try
            {
                const int channel = 0;
                if (SteamNetworking.IsP2PPacketAvailable(out uint packetSize, channel))
                {
                    byte[] tempBuffer;
                    if (packetSize <= _receiveBuffer.Length)
                    {
                        tempBuffer = _receiveBuffer;
                    }
                    else if (packetSize <= _largeReceiveBuffer.Length)
                    {
                        tempBuffer = _largeReceiveBuffer;
                    }
                    else
                    {
                        tempBuffer = new byte[packetSize];
                    }
                    
                    if (SteamNetworking.ReadP2PPacket(tempBuffer, packetSize, out uint bytesRead, out remoteSteamID, channel))
                    {
                        if (bytesRead == 9 && System.Text.Encoding.UTF8.GetString(tempBuffer, 0, 9) == "HANDSHAKE")
                        {
                            return false;
                        }
                        
                        int copySize = (int)Math.Min(bytesRead, maxSize);
                        Array.Copy(tempBuffer, 0, buffer, offset, copySize);
                        length = copySize;
                        
                        if (SteamEndPointMapper.Instance != null)
                        {
                            if (!SteamEndPointMapper.Instance.TryGetEndPoint(remoteSteamID, out endPoint))
                            {
                                endPoint = SteamEndPointMapper.Instance.RegisterSteamID(remoteSteamID);
                            }
                        }
                        
                        System.Threading.Interlocked.Increment(ref _packetsReceived);
                        System.Threading.Interlocked.Add(ref _bytesReceived, copySize);
                        
                        return endPoint != null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[SteamP2P] TryReceiveDirectFromSteam 异常: " + ex);
            }
            
            return false;
        }
        
        public void ClearAcceptedSession(CSteamID remoteSteamID)
        {
            _acceptedSessions.Remove(remoteSteamID);
        }
        
        public bool AcceptP2PSession(CSteamID remoteSteamID)
        {
            if (!SteamManager.Initialized)
                return false;
            return SteamNetworking.AcceptP2PSessionWithUser(remoteSteamID);
        }
        
        public bool CloseP2PSession(CSteamID remoteSteamID)
        {
            if (!SteamManager.Initialized)
                return false;
            return SteamNetworking.CloseP2PSessionWithUser(remoteSteamID);
        }
        
        public bool GetP2PSessionState(CSteamID remoteSteamID, out P2PSessionState_t sessionState)
        {
            if (!SteamManager.Initialized)
            {
                sessionState = new P2PSessionState_t();
                return false;
            }
            return SteamNetworking.GetP2PSessionState(remoteSteamID, out sessionState);
        }
        
        #region Steam 回调处理
        
        private void OnP2PSessionRequest(P2PSessionRequest_t request)
        {
            Debug.Log("[SteamP2P] 收到P2P会话请求 from " + request.m_steamIDRemote);
            
            if (SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote))
            {
                Debug.Log("[SteamP2P] 已接受P2P会话 from " + request.m_steamIDRemote);
                
                byte[] handshake = System.Text.Encoding.UTF8.GetBytes("HANDSHAKE");
                for (int i = 0; i < 3; i++)
                {
                    SteamNetworking.SendP2PPacket(request.m_steamIDRemote, handshake, (uint)handshake.Length,
                        EP2PSend.k_EP2PSendUnreliableNoDelay, 0);
                }
                
                bool sent = SteamNetworking.SendP2PPacket(request.m_steamIDRemote, handshake, (uint)handshake.Length,
                    EP2PSend.k_EP2PSendReliable, 0);
                    
                Debug.Log("[SteamP2P] NAT穿透握手完成: " + (sent ? "成功" : "失败"));
                
                if (SteamEndPointMapper.Instance != null)
                {
                    SteamEndPointMapper.Instance.OnP2PSessionEstablished(request.m_steamIDRemote);
                }
            }
            else
            {
                Debug.LogError("[SteamP2P] 接受P2P会话失败 from " + request.m_steamIDRemote);
            }
        }
        
        private void OnP2PSessionConnectFail(P2PSessionConnectFail_t failure)
        {
            string errorMsg;
            switch (failure.m_eP2PSessionError)
            {
                case 0:
                    errorMsg = "None";
                    break;
                case 1:
                    errorMsg = "TargetNotRunning - 目标未运行Steam";
                    break;
                case 2:
                    errorMsg = "NoRightsToApp - 无权访问应用";
                    break;
                case 3:
                    errorMsg = "DestinationNotLoggedIn - 目标未登录";
                    break;
                case 4:
                    errorMsg = "Timeout - 连接超时（可能NAT穿透失败）";
                    break;
                default:
                    errorMsg = "Unknown(" + failure.m_eP2PSessionError + ")";
                    break;
            }
            
            Debug.LogError("[SteamP2P] P2P连接失败: " + failure.m_steamIDRemote);
            Debug.LogError("[SteamP2P] 错误原因: " + errorMsg);
            
            if (SteamEndPointMapper.Instance != null)
            {
                SteamEndPointMapper.Instance.OnP2PSessionFailed(failure.m_steamIDRemote);
            }
        }
        
        #endregion
        
        private void OnDestroy()
        {
            if (SteamManager.Initialized && SteamEndPointMapper.Instance != null)
            {
                var activeSessions = SteamEndPointMapper.Instance.GetAllSteamIDs();
                if (activeSessions != null)
                {
                    foreach (var steamID in activeSessions)
                    {
                        SteamNetworking.CloseP2PSessionWithUser(steamID);
                    }
                }
            }
            
            Debug.Log("[SteamP2P] ========== 会话统计 ==========");
            Debug.Log("[SteamP2P] 发送: " + PacketsSent + "包 (" + BytesSent + "字节) | 失败: " + SendFailures);
            Debug.Log("[SteamP2P] 接收: " + PacketsReceived + "包 (" + BytesReceived + "字节) | 丢包: " + PacketsDropped);
            Debug.Log("[SteamP2P] 队列峰值: " + MaxQueueDepth + "/" + MAX_QUEUE_SIZE);
            
            float lossRate = PacketsReceived > 0 ? (PacketsDropped * 100f / (PacketsReceived + PacketsDropped)) : 0;
            float failRate = PacketsSent > 0 ? (SendFailures * 100f / (PacketsSent + SendFailures)) : 0;
            
            Debug.Log("[SteamP2P] 接收丢包率: " + lossRate.ToString("F2") + "% | 发送失败率: " + failRate.ToString("F2") + "%");
            Debug.Log("[SteamP2P] ================================");
        }
    }
}
