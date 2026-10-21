public enum Teams : byte { player }
public enum SoundTypes : byte { unknowNoise, combatSound, grenadeDropSound }

namespace EscapeFromDuckovCoopMod
{
    public enum AIStatus : byte { Active }
    public struct ItemSnapshot { }
    public static class ItemTool
    {
        public static void WriteItemSnapshot(NetDataWriter writer, ItemSnapshot snapshot) { }
        public static ItemSnapshot ReadItemSnapshot(NetPacketReader reader) => default;
    }
    public static class SnapshotExtensions
    {
        public static void PutV3cm(this NetDataWriter writer, Vector3 value) { for (var i=0;i<3;i++) writer.Put(0); }
        public static Vector3 GetV3cm(this NetPacketReader reader) { for(var i=0;i<3;i++) reader.GetInt(); return default; }
        public static void PutDir(this NetDataWriter writer, Vector3 value) { writer.Put((ushort)0); writer.Put((ushort)0); }
        public static Vector3 GetDir(this NetPacketReader reader) { reader.GetUShort(); reader.GetUShort(); return default; }
        public static void PutVector3(this NetDataWriter writer, Vector3 value) { for (var i=0;i<3;i++) writer.Put(0f); }
        public static Vector3 GetVector3(this NetPacketReader reader) { for(var i=0;i<3;i++) reader.GetFloat(); return default; }
        public static void PutQuaternion(this NetDataWriter writer, Quaternion value) { for (var i=0;i<4;i++) writer.Put(0f); }
        public static Quaternion GetQuaternion(this NetPacketReader reader) { for(var i=0;i<4;i++) reader.GetFloat(); return default; }
    }
}
