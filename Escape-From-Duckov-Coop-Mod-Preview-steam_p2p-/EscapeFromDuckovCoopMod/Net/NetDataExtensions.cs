// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspicuous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

using System.Runtime.CompilerServices;

namespace EscapeFromDuckovCoopMod;

public static class NetDataExtensions
{
    public static void PutVector3(this NetDataWriter writer, Vector3 vector)
    {
        writer.Put(vector.x);
        writer.Put(vector.y);
        writer.Put(vector.z);
    }

    public static Vector3 GetVector3(this NetPacketReader reader)
    {
        return new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Finite(float v)
    {
        return !float.IsNaN(v) && !float.IsInfinity(v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Quaternion NormalizeSafe(Quaternion q)
    {
        if (!Finite(q.x) || !Finite(q.y) || !Finite(q.z) || !Finite(q.w))
            return Quaternion.identity;

        // 防 0 四元数
        var mag2 = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
        if (mag2 < 1e-12f) return Quaternion.identity;

        var inv = 1.0f / Mathf.Sqrt(mag2);
        q.x *= inv;
        q.y *= inv;
        q.z *= inv;
        q.w *= inv;
        return q;
    }

    public static void PutQuaternion(this NetDataWriter writer, Quaternion q)
    {
        q = NormalizeSafe(q);
        writer.Put(q.x);
        writer.Put(q.y);
        writer.Put(q.z);
        writer.Put(q.w);
    }

    public static Quaternion GetQuaternion(this NetPacketReader reader)
    {
        var q = new Quaternion(reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        return NormalizeSafe(q);
    }
}