using System;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod.Net.HybridP2P;

public class LatencyCalculator
{
    public static LatencyCalculator Instance { get; private set; }
    
    private class PingData
    {
        public float LastPingTime;
        public float LastPongTime;
        public int PingSequence;
        public Queue<float> LatencySamples = new Queue<float>();
        public float AverageLatency;
        public float LastLatency;
    }
    
    private readonly Dictionary<string, PingData> _pingData = new();
    private const float PING_INTERVAL = 1.0f;
    private const int MAX_SAMPLES = 10;
    
    public LatencyCalculator()
    {
        Instance = this;
    }
    
    public void Update()
    {
        float currentTime = Time.realtimeSinceStartup;
        
        foreach (var kvp in _pingData)
        {
            if (currentTime - kvp.Value.LastPingTime >= PING_INTERVAL)
            {
                SendPing(kvp.Key);
            }
        }
        
        if (_updateLogTimer <= 0f)
        {
            if (_pingData.Count > 0)
            {
                Debug.Log($"[LATENCY-UPDATE] 延迟监控活跃: 已注册{_pingData.Count}个连接");
                foreach (var kvp in _pingData)
                {
                    Debug.Log($"  - {kvp.Key}: avg={kvp.Value.AverageLatency:F1}ms, samples={kvp.Value.LatencySamples.Count}");
                }
            }
            _updateLogTimer = 10f;
        }
        else
        {
            _updateLogTimer -= Time.deltaTime;
        }
    }
    
    private float _updateLogTimer = 10f;
    
    public void RegisterClient(string endPoint)
    {
        if (!_pingData.ContainsKey(endPoint))
        {
            _pingData[endPoint] = new PingData
            {
                LastPingTime = Time.realtimeSinceStartup - PING_INTERVAL,
                PingSequence = 0
            };
            Debug.Log($"[LATENCY-REG] 注册客户端延迟监控: endPoint={endPoint}");
        }
    }
    
    public void UnregisterClient(string endPoint)
    {
        _pingData.Remove(endPoint);
    }
    
    private void SendPing(string endPoint)
    {
        try
        {
            var service = NetService.Instance;
            if (service == null || service.netManager == null)
            {
                Debug.LogWarning($"[LATENCY-SEND] NetService不可用");
                return;
            }
            
            var data = _pingData[endPoint];
            data.PingSequence++;
            data.LastPingTime = Time.realtimeSinceStartup;
            
            var writer = new NetDataWriter();
            writer.Put((byte)200);
            writer.Put(data.PingSequence);
            writer.Put(data.LastPingTime);
            
            var peer = FindPeerByEndPoint(endPoint);
            if (peer != null)
            {
                peer.Send(writer, DeliveryMethod.Unreliable);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[LatencyCalculator] Error sending ping: {e.Message}");
        }
    }
    
    public void HandlePingPacket(string endPoint, NetDataReader reader)
    {
        try
        {
            int sequence = reader.GetInt();
            float sendTime = reader.GetFloat();
            
            var writer = new NetDataWriter();
            writer.Put((byte)201);
            writer.Put(sequence);
            writer.Put(sendTime);
            
            var peer = FindPeerByEndPoint(endPoint);
            if (peer != null)
            {
                peer.Send(writer, DeliveryMethod.Unreliable);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[LatencyCalculator] Error handling ping: {e.Message}");
        }
    }
    
    public void HandlePongPacket(string endPoint, NetDataReader reader)
    {
        try
        {
            int sequence = reader.GetInt();
            float sendTime = reader.GetFloat();
            
            if (!_pingData.TryGetValue(endPoint, out var data))
            {
                return;
            }
            
            if (sequence != data.PingSequence)
            {
                return;
            }
            
            float currentTime = Time.realtimeSinceStartup;
            float latency = (currentTime - sendTime) * 1000f;
            
            data.LastLatency = latency;
            data.LastPongTime = currentTime;
            
            data.LatencySamples.Enqueue(latency);
            if (data.LatencySamples.Count > MAX_SAMPLES)
            {
                data.LatencySamples.Dequeue();
            }
            
            float sum = 0;
            foreach (var sample in data.LatencySamples)
            {
                sum += sample;
            }
            data.AverageLatency = sum / data.LatencySamples.Count;
        }
        catch (Exception e)
        {
            Debug.LogError($"[LatencyCalculator] Error handling pong: {e.Message}");
        }
    }
    
    public float GetLatency(string endPoint)
    {
        if (_pingData.TryGetValue(endPoint, out var data))
        {
            return data.AverageLatency;
        }
        return 0f;
    }
    
    public float GetLastLatency(string endPoint)
    {
        if (_pingData.TryGetValue(endPoint, out var data))
        {
            return data.LastLatency;
        }
        return 0f;
    }
    
    private NetPeer FindPeerByEndPoint(string endPoint)
    {
        var service = NetService.Instance;
        if (service == null || service.netManager == null) return null;
        
        foreach (var peer in service.netManager.ConnectedPeerList)
        {
            if (peer.EndPoint.ToString() == endPoint)
            {
                return peer;
            }
        }
        
        return null;
    }
}

