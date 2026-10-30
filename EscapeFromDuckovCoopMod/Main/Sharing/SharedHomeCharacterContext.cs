using System.Reflection;

namespace EscapeFromDuckovCoopMod;

internal sealed class SharedHomeCharacterContext
{
    private const string DefaultKey = "MainCharacterItemData";
    private const string TemporaryKey = DefaultKey + "/Temporary";
    private readonly PropertyInfo _key;
    private readonly FieldInfo _keyField;
    private readonly PropertyInfo _suffix;
    private readonly PropertyInfo _saveCharacter;

    internal SharedHomeCharacterContext(Type levelType, Type configType)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        _key = levelType.GetProperty("MainCharacterItemSaveKey", instance);
        _keyField = levelType.GetField("MainCharacterItemSaveKey", instance | BindingFlags.Static);
        _suffix = configType.GetProperty("OverridePlayerItemSaveDataKeySuffix",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        _saveCharacter = configType.GetProperty("SaveCharacter", BindingFlags.Static | BindingFlags.Public);
    }

    internal string Read(object level)
    {
        if (level == null) return DefaultKey;
        if (_saveCharacter?.GetValue(null) is false) return TemporaryKey;
        var key = _key?.GetValue(level) as string ?? _keyField?.GetValue(level) as string;
        if (!string.IsNullOrWhiteSpace(key)) return key;
        // Older game builds save the normal character under the fixed key.
        // Retain the native challenge suffix when that API is available.
        var suffix = _suffix?.GetValue(null) as string;
        return string.IsNullOrWhiteSpace(suffix) ? DefaultKey : DefaultKey + "_" + suffix;
    }
    internal static bool IsTemporary(string context) => context == TemporaryKey;
}
