using Duckov;
using LiteNetLib;
using LiteNetLib.Utils;
using NodeCanvas.Tasks.Actions;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.AI_VOICE_SOUND, DeliveryMethod.ReliableUnordered, RpcDirection.ServerToClient)]
public struct AIPostSoundRpc : IRpcMessage
{
    public int Id;
    public PostSound.VoiceSounds VoiceSound;
    public AudioManager.VoiceType VoiceType;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Id);
        writer.Put((byte)VoiceSound);
        writer.Put((byte)VoiceType);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id = reader.GetInt();
        VoiceSound = (PostSound.VoiceSounds)reader.GetByte();
        VoiceType = (AudioManager.VoiceType)reader.GetByte();
    }
}
