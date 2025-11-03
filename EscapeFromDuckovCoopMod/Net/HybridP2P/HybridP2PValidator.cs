using System;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod.Net.HybridP2P;

public class HybridP2PValidator
{
    public static HybridP2PValidator Instance { get; private set; }
    
    private class ClientValidationData
    {
        public Vector3 LastPosition;
        public float LastPositionTime;
        public int SuspiciousCount;
        public float LastFireTime;
        public int FireCount;
        public float FireWindowStart;
    }
    
    private readonly Dictionary<string, ClientValidationData> _clientData = new();
    private const float MAX_SPEED = 20f;
    private const float MAX_FIRE_RATE = 20f;
    private const float FIRE_RATE_WINDOW = 1f;
    private const int MAX_SUSPICIOUS_COUNT = 10;
    
    public HybridP2PValidator()
    {
        Instance = this;
    }
    
    public bool ValidatePositionUpdate(string endPoint, Vector3 position, Vector3 velocity)
    {
        if (!_clientData.TryGetValue(endPoint, out var data))
        {
            data = new ClientValidationData
            {
                LastPosition = position,
                LastPositionTime = Time.realtimeSinceStartup
            };
            _clientData[endPoint] = data;
            return true;
        }
        
        float deltaTime = Time.realtimeSinceStartup - data.LastPositionTime;
        if (deltaTime < 0.001f) return true;
        
        float distance = Vector3.Distance(position, data.LastPosition);
        float speed = distance / deltaTime;
        
        if (speed > MAX_SPEED)
        {
            data.SuspiciousCount++;
            Debug.LogWarning($"[HybridP2PValidator] Suspicious speed detected from {endPoint}: {speed:F2} m/s");
            
            if (data.SuspiciousCount >= MAX_SUSPICIOUS_COUNT)
            {
                Debug.LogError($"[HybridP2PValidator] Client {endPoint} exceeded suspicious behavior threshold, rejecting");
                return false;
            }
            
            return false;
        }
        
        data.LastPosition = position;
        data.LastPositionTime = Time.realtimeSinceStartup;
        return true;
    }
    
    public bool ValidateFireEvent(string endPoint)
    {
        if (!_clientData.TryGetValue(endPoint, out var data))
        {
            data = new ClientValidationData
            {
                FireWindowStart = Time.realtimeSinceStartup,
                FireCount = 1,
                LastFireTime = Time.realtimeSinceStartup
            };
            _clientData[endPoint] = data;
            return true;
        }
        
        float currentTime = Time.realtimeSinceStartup;
        
        if (currentTime - data.FireWindowStart > FIRE_RATE_WINDOW)
        {
            data.FireWindowStart = currentTime;
            data.FireCount = 1;
        }
        else
        {
            data.FireCount++;
        }
        
        if (data.FireCount > MAX_FIRE_RATE)
        {
            data.SuspiciousCount++;
            Debug.LogWarning($"[HybridP2PValidator] Suspicious fire rate from {endPoint}: {data.FireCount} shots in {FIRE_RATE_WINDOW}s");
            
            if (data.SuspiciousCount >= MAX_SUSPICIOUS_COUNT)
            {
                Debug.LogError($"[HybridP2PValidator] Client {endPoint} exceeded suspicious behavior threshold, rejecting");
                KickClient(endPoint);
                return false;
            }
            
            return false;
        }
        
        data.LastFireTime = currentTime;
        return true;
    }
    
    public bool ValidateDamageValue(string endPoint, float damage)
    {
        if (damage < 0 || damage > 1000f)
        {
            if (!_clientData.TryGetValue(endPoint, out var data))
            {
                data = new ClientValidationData();
                _clientData[endPoint] = data;
            }
            
            data.SuspiciousCount++;
            Debug.LogWarning($"[HybridP2PValidator] Invalid damage value from {endPoint}: {damage}");
            
            if (data.SuspiciousCount >= MAX_SUSPICIOUS_COUNT)
            {
                Debug.LogError($"[HybridP2PValidator] Client {endPoint} exceeded suspicious behavior threshold, rejecting");
                KickClient(endPoint);
                return false;
            }
            
            return false;
        }
        
        return true;
    }
    
    public void ResetClientData(string endPoint)
    {
        _clientData.Remove(endPoint);
    }
    
    private void KickClient(string endPoint)
    {
        try
        {
            var service = NetService.Instance;
            if (service != null && service.IsServer)
            {
                var peer = service.netManager?.GetPeerById(0);
                if (peer != null)
                {
                    service.netManager.DisconnectPeer(peer);
                    Debug.Log($"[HybridP2PValidator] Kicked client {endPoint}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[HybridP2PValidator] Error kicking client: {e.Message}");
        }
    }
}

