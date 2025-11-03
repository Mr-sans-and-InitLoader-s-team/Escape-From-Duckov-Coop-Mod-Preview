using System.Collections.Generic;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Net.HybridP2P
{
    public class PositionSnapshot
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public float Timestamp;
    }
    
    public class LatencyCompensator
    {
        private Dictionary<string, Queue<PositionSnapshot>> _historyBuffers = new Dictionary<string, Queue<PositionSnapshot>>();
        private const int MAX_HISTORY_SIZE = 60;
        private const float MAX_HISTORY_TIME = 2.0f;
        
        public void RecordPosition(string endPoint, Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            if (!_historyBuffers.ContainsKey(endPoint))
            {
                _historyBuffers[endPoint] = new Queue<PositionSnapshot>();
            }
            
            var queue = _historyBuffers[endPoint];
            var snapshot = new PositionSnapshot
            {
                Position = position,
                Rotation = rotation,
                Velocity = velocity,
                Timestamp = Time.realtimeSinceStartup
            };
            
            queue.Enqueue(snapshot);
            
            while (queue.Count > MAX_HISTORY_SIZE)
            {
                queue.Dequeue();
            }
            
            float currentTime = Time.realtimeSinceStartup;
            while (queue.Count > 0 && (currentTime - queue.Peek().Timestamp) > MAX_HISTORY_TIME)
            {
                queue.Dequeue();
            }
        }
        
        public Vector3 CompensatePosition(string endPoint, Vector3 receivedPosition, float latencyMs)
        {
            if (latencyMs <= 0 || latencyMs > 500f)
            {
                return receivedPosition;
            }
            
            if (!_historyBuffers.ContainsKey(endPoint))
            {
                return receivedPosition;
            }
            
            var queue = _historyBuffers[endPoint];
            if (queue.Count < 2)
            {
                return receivedPosition;
            }
            
            float latencySec = latencyMs / 1000f;
            float targetTime = Time.realtimeSinceStartup - latencySec;
            
            PositionSnapshot before = null;
            PositionSnapshot after = null;
            
            foreach (var snapshot in queue)
            {
                if (snapshot.Timestamp <= targetTime)
                {
                    before = snapshot;
                }
                else
                {
                    after = snapshot;
                    break;
                }
            }
            
            if (before == null || after == null)
            {
                var last = GetLastSnapshot(endPoint);
                if (last != null && last.Velocity.sqrMagnitude > 0.01f)
                {
                    return last.Position + last.Velocity * latencySec;
                }
                return receivedPosition;
            }
            
            float t = Mathf.InverseLerp(before.Timestamp, after.Timestamp, targetTime);
            Vector3 interpolatedPos = Vector3.Lerp(before.Position, after.Position, t);
            
            Vector3 velocity = (after.Position - before.Position) / (after.Timestamp - before.Timestamp);
            if (velocity.sqrMagnitude > 0.01f)
            {
                interpolatedPos += velocity * latencySec * 0.5f;
            }
            
            float maxCompensationDistance = 5f;
            Vector3 offset = interpolatedPos - receivedPosition;
            if (offset.sqrMagnitude > maxCompensationDistance * maxCompensationDistance)
            {
                offset = offset.normalized * maxCompensationDistance;
                interpolatedPos = receivedPosition + offset;
            }
            
            return interpolatedPos;
        }
        
        public PositionSnapshot GetLastSnapshot(string endPoint)
        {
            if (!_historyBuffers.ContainsKey(endPoint) || _historyBuffers[endPoint].Count == 0)
            {
                return null;
            }
            
            PositionSnapshot last = null;
            foreach (var snapshot in _historyBuffers[endPoint])
            {
                last = snapshot;
            }
            return last;
        }
        
        public void Clear(string endPoint)
        {
            if (_historyBuffers.ContainsKey(endPoint))
            {
                _historyBuffers.Remove(endPoint);
            }
        }
        
        public void ClearAll()
        {
            _historyBuffers.Clear();
        }
    }
}

