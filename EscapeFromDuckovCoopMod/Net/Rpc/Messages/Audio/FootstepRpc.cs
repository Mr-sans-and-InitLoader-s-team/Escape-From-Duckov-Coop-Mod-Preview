namespace EscapeFromDuckovCoopMod;

[Rpc(Op.FOOTSTEP, DeliveryMethod.Unreliable, RpcDirection.Bidirectional)]
public struct FootstepRpc : IRpcMessage
{
    public string PlayerId;
    public string SceneId;
    public int AiId;
    public CharacterSoundMaker.FootStepTypes Type;
    public Duckov.AudioManager.FootStepMaterialType Material;
    public Vector3 Position;
    public float HearingRadius;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(PlayerId ?? string.Empty);
        writer.Put(SceneId ?? string.Empty);
        writer.Put(AiId);
        writer.Put((byte)Type);
        writer.Put((byte)Material);
        writer.Put(Position.x);
        writer.Put(Position.y);
        writer.Put(Position.z);
        writer.Put(HearingRadius);
    }

    public void Deserialize(NetPacketReader reader)
    {
        PlayerId = reader.GetString();
        SceneId = reader.GetString();
        AiId = reader.GetInt();
        Type = (CharacterSoundMaker.FootStepTypes)reader.GetByte();
        Material = (Duckov.AudioManager.FootStepMaterialType)reader.GetByte();
        Position = reader.GetVector3();
        HearingRadius = reader.GetFloat();
    }
}
