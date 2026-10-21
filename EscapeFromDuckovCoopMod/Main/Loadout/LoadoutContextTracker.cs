namespace EscapeFromDuckovCoopMod;

internal sealed class LoadoutContextTracker
{
    private string _sceneId;
    private object _character;

    public bool Update(bool inGame, string sceneId, object character)
    {
        if (!inGame || character == null)
        {
            _sceneId = null;
            _character = null;
            return false;
        }
        var changed = !ReferenceEquals(_character, character) || !string.Equals(_sceneId, sceneId, StringComparison.Ordinal);
        _character = character;
        _sceneId = sceneId;
        return changed;
    }
}
