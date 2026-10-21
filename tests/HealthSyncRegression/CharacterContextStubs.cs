internal sealed class NewCharacterContext
{
    public string MainCharacterItemSaveKey => "MainCharacterItemData_Challenge";
}
internal sealed class PublicCharacterContext
{
    public const string MainCharacterItemSaveKey = "MainCharacterItemData";
}
internal sealed class OldCharacterContext { }
internal sealed class OldCharacterConfig { }
internal sealed class ChallengeCharacterConfig
{
    public static string OverridePlayerItemSaveDataKeySuffix { get; set; }
}
