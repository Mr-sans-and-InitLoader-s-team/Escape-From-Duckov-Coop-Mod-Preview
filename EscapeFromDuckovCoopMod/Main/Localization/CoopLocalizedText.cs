using TMPro;

namespace EscapeFromDuckovCoopMod;

internal sealed class CoopLocalizedText : MonoBehaviour
{
    private TMP_Text _label;
    private string _key;
    private string _language;

    internal static TMP_Text Bind(TMP_Text label, string key)
    {
        var binding = label.gameObject.AddComponent<CoopLocalizedText>();
        binding._label = label;
        binding._key = key;
        binding.Refresh();
        return label;
    }

    private void OnEnable() => Refresh();
    private void LateUpdate()
    {
        if (_language != CoopLocalization.GetCurrentLanguage()) Refresh();
    }

    private void Refresh()
    {
        if (!_label) return;
        _label.text = CoopLocalization.Get(_key);
        _language = CoopLocalization.GetCurrentLanguage();
    }
}
