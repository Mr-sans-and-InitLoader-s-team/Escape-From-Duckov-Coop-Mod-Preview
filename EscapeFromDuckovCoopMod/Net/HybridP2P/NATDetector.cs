using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;
using Steamworks;

namespace EscapeFromDuckovCoopMod.Net.HybridP2P;

public enum NATType
{
    Unknown,
    Open,
    Moderate,
    Strict,
    Blocked
}

public class NATDetector
{
    public static NATDetector Instance { get; private set; }
    
    private NATType _localNATType = NATType.Unknown;
    private string _publicIP = "";
    private int _publicPort = 0;
    
    public NATType LocalNATType => _localNATType;
    public string PublicIP => _publicIP;
    public int PublicPort => _publicPort;
    
    public NATDetector()
    {
        Instance = this;
    }
    
    public async Task<NATType> DetectNATType()
    {
        try
        {
            if (SteamManager.Initialized)
            {
                var p2pInfo = await GetSteamP2PInfo();
                _localNATType = p2pInfo;
                Debug.Log($"[NATDetector] Detected NAT type via Steam: {_localNATType}");
                return _localNATType;
            }
            
            _localNATType = await DetectNATTypeViaSTUN();
            Debug.Log($"[NATDetector] Detected NAT type via STUN: {_localNATType}");
            return _localNATType;
        }
        catch (Exception e)
        {
            Debug.LogError($"[NATDetector] Error detecting NAT type: {e.Message}");
            _localNATType = NATType.Unknown;
            return _localNATType;
        }
    }
    
    private async Task<NATType> GetSteamP2PInfo()
    {
        await Task.Delay(100);
        
        try
        {
            SteamNetworking.AllowP2PPacketRelay(true);
            
            return NATType.Moderate;
        }
        catch
        {
            return NATType.Unknown;
        }
    }
    
    private async Task<NATType> DetectNATTypeViaSTUN()
    {
        try
        {
            using (var client = new UdpClient())
            {
                var stunServer = "stun.l.google.com";
                var stunPort = 19302;
                
                client.Connect(stunServer, stunPort);
                
                byte[] bindingRequest = CreateSTUNBindingRequest();
                await client.SendAsync(bindingRequest, bindingRequest.Length);
                
                var receiveTask = client.ReceiveAsync();
                var timeoutTask = Task.Delay(3000);
                
                var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    Debug.LogWarning("[NATDetector] STUN request timeout");
                    return NATType.Strict;
                }
                
                var result = receiveTask.Result;
                var response = result.Buffer;
                
                if (ParseSTUNResponse(response, out string mappedIP, out int mappedPort))
                {
                    _publicIP = mappedIP;
                    _publicPort = mappedPort;
                    
                    var localEndPoint = client.Client.LocalEndPoint as IPEndPoint;
                    if (localEndPoint != null)
                    {
                        if (localEndPoint.Port == mappedPort)
                        {
                            return NATType.Open;
                        }
                        else
                        {
                            return NATType.Moderate;
                        }
                    }
                }
                
                return NATType.Unknown;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NATDetector] STUN detection failed: {e.Message}");
            return NATType.Unknown;
        }
    }
    
    private byte[] CreateSTUNBindingRequest()
    {
        byte[] request = new byte[20];
        
        request[0] = 0x00;
        request[1] = 0x01;
        
        request[2] = 0x00;
        request[3] = 0x00;
        
        request[4] = 0x21;
        request[5] = 0x12;
        request[6] = 0xA4;
        request[7] = 0x42;
        
        var random = new System.Random();
        for (int i = 8; i < 20; i++)
        {
            request[i] = (byte)random.Next(256);
        }
        
        return request;
    }
    
    private bool ParseSTUNResponse(byte[] response, out string ip, out int port)
    {
        ip = "";
        port = 0;
        
        try
        {
            if (response.Length < 20) return false;
            
            if (response[0] != 0x01 || response[1] != 0x01) return false;
            
            int pos = 20;
            while (pos < response.Length)
            {
                if (pos + 4 > response.Length) break;
                
                ushort attrType = (ushort)((response[pos] << 8) | response[pos + 1]);
                ushort attrLength = (ushort)((response[pos + 2] << 8) | response[pos + 3]);
                
                if (attrType == 0x0001 || attrType == 0x0020)
                {
                    if (pos + 4 + attrLength > response.Length) break;
                    
                    if (attrLength >= 8)
                    {
                        byte family = response[pos + 5];
                        
                        if (family == 0x01)
                        {
                            port = (response[pos + 6] << 8) | response[pos + 7];
                            
                            if (attrType == 0x0020)
                            {
                                port ^= 0x2112;
                            }
                            
                            byte[] ipBytes = new byte[4];
                            Array.Copy(response, pos + 8, ipBytes, 0, 4);
                            
                            if (attrType == 0x0020)
                            {
                                ipBytes[0] ^= 0x21;
                                ipBytes[1] ^= 0x12;
                                ipBytes[2] ^= 0xA4;
                                ipBytes[3] ^= 0x42;
                            }
                            
                            ip = $"{ipBytes[0]}.{ipBytes[1]}.{ipBytes[2]}.{ipBytes[3]}";
                            return true;
                        }
                    }
                }
                
                pos += 4 + attrLength;
                pos = (pos + 3) & ~3;
            }
            
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"[NATDetector] Error parsing STUN response: {e.Message}");
            return false;
        }
    }
    
    public static string GetNATTypeDisplayName(NATType type)
    {
        return type switch
        {
            NATType.Open => "Open",
            NATType.Moderate => "Moderate",
            NATType.Strict => "Strict",
            NATType.Blocked => "Blocked",
            _ => "Unknown"
        };
    }
    
    public static Color GetNATTypeColor(NATType type)
    {
        return type switch
        {
            NATType.Open => new Color(0.3f, 0.8f, 0.3f),
            NATType.Moderate => new Color(1f, 0.8f, 0.2f),
            NATType.Strict => new Color(1f, 0.4f, 0.2f),
            NATType.Blocked => new Color(0.8f, 0.2f, 0.2f),
            _ => Color.gray
        };
    }
    
    public bool CanDirectConnect(NATType remoteType)
    {
        if (_localNATType == NATType.Open) return true;
        if (_localNATType == NATType.Moderate && remoteType != NATType.Strict) return true;
        if (_localNATType == NATType.Strict && remoteType == NATType.Open) return true;
        
        return false;
    }
}

