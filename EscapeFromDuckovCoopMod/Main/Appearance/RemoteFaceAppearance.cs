namespace EscapeFromDuckovCoopMod;

internal sealed class RemoteFaceAppearance : MonoBehaviour
{
    private string _applied;

    internal void Apply(string json)
    {
        if (_applied == json) return;
        GetComponent<CustomFaceInstance>().LoadFromData(JsonUtility.FromJson<CustomFaceSettingData>(json));
        _applied = json;
    }
}
