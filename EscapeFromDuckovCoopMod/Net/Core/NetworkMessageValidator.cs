using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Net.Core
{
    public static class NetworkMessageValidator
    {
        public static bool ValidateHealthReport(NetDataReader reader, out float max, out float cur, out uint sequence)
        {
            max = 0f;
            cur = 0f;
            sequence = 0;

            if (reader.AvailableBytes < 12)
            {
                Debug.LogError($"[NetworkValidator] PLAYER_HEALTH_REPORT: 数据包不完整, 需要12字节, 实际{reader.AvailableBytes}字节");
                return false;
            }

            max = reader.GetFloat();
            cur = reader.GetFloat();
            sequence = reader.GetUInt();

            if (float.IsNaN(max) || float.IsNaN(cur) || max < 0 || cur < 0)
            {
                Debug.LogError($"[NetworkValidator] PLAYER_HEALTH_REPORT: 无效的健康值 max={max}, cur={cur}");
                return false;
            }

            return true;
        }

        public static bool ValidateAuthHealthRemote(NetDataReader reader, out string playerId, out float max, out float cur, out uint sequence)
        {
            playerId = null;
            max = 0f;
            cur = 0f;
            sequence = 0;

            if (reader.AvailableBytes < 4)
            {
                Debug.LogError($"[NetworkValidator] AUTH_HEALTH_REMOTE: 数据包不完整, 至少需要4字节读取playerId长度");
                return false;
            }

            playerId = reader.GetString();

            if (string.IsNullOrEmpty(playerId))
            {
                Debug.LogError($"[NetworkValidator] AUTH_HEALTH_REMOTE: playerId为空");
                return false;
            }

            if (reader.AvailableBytes < 12)
            {
                Debug.LogError($"[NetworkValidator] AUTH_HEALTH_REMOTE: playerId={playerId}, 需要12字节, 实际{reader.AvailableBytes}字节");
                return false;
            }

            max = reader.GetFloat();
            cur = reader.GetFloat();
            sequence = reader.GetUInt();

            if (float.IsNaN(max) || float.IsNaN(cur) || max < 0 || cur < 0)
            {
                Debug.LogError($"[NetworkValidator] AUTH_HEALTH_REMOTE: playerId={playerId}, 无效的健康值 max={max}, cur={cur}");
                return false;
            }

            return true;
        }

        public static bool ValidatePositionUpdate(NetDataReader reader, out string endPoint)
        {
            endPoint = null;

            if (reader.AvailableBytes < 4)
            {
                Debug.LogError($"[NetworkValidator] POSITION_UPDATE: 数据包不完整, 至少需要4字节读取endPoint长度");
                return false;
            }

            endPoint = reader.GetString();

            if (string.IsNullOrEmpty(endPoint))
            {
                Debug.LogError($"[NetworkValidator] POSITION_UPDATE: endPoint为空");
                return false;
            }

            if (reader.AvailableBytes < 12)
            {
                Debug.LogError($"[NetworkValidator] POSITION_UPDATE: endPoint={endPoint}, 需要至少12字节, 实际{reader.AvailableBytes}字节");
                return false;
            }

            return true;
        }

        public static bool ValidatePlayerStatusUpdateHeader(NetDataReader reader, out int playerCount)
        {
            playerCount = 0;

            if (reader.AvailableBytes < 4)
            {
                Debug.LogError($"[NetworkValidator] PLAYER_STATUS_UPDATE: 数据包不完整, 需要至少4字节读取playerCount");
                return false;
            }

            playerCount = reader.GetInt();

            if (playerCount < 0 || playerCount > 100)
            {
                Debug.LogError($"[NetworkValidator] PLAYER_STATUS_UPDATE: 无效的playerCount={playerCount}");
                return false;
            }

            return true;
        }

        public static bool IsValidString(string value, string fieldName)
        {
            if (string.IsNullOrEmpty(value))
            {
                Debug.LogWarning($"[NetworkValidator] {fieldName} 为空或null");
                return false;
            }
            return true;
        }

        public static bool IsValidVector(UnityEngine.Vector3 vec, string fieldName)
        {
            if (float.IsNaN(vec.x) || float.IsNaN(vec.y) || float.IsNaN(vec.z) ||
                float.IsInfinity(vec.x) || float.IsInfinity(vec.y) || float.IsInfinity(vec.z))
            {
                Debug.LogError($"[NetworkValidator] {fieldName} 包含无效数值: {vec}");
                return false;
            }
            return true;
        }

        public static bool IsValidQuaternion(UnityEngine.Quaternion quat, string fieldName)
        {
            if (float.IsNaN(quat.x) || float.IsNaN(quat.y) || float.IsNaN(quat.z) || float.IsNaN(quat.w) ||
                float.IsInfinity(quat.x) || float.IsInfinity(quat.y) || float.IsInfinity(quat.z) || float.IsInfinity(quat.w))
            {
                Debug.LogError($"[NetworkValidator] {fieldName} 包含无效数值: {quat}");
                return false;
            }
            return true;
        }
    }
}

