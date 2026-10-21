namespace EscapeFromDuckovCoopMod;

internal sealed class LocalSceneLoadPermit
{
    private string _sceneId;
    private double _expiresAt;
    private bool _throughSettlement;

    public void Grant(string sceneId, double now, double duration, bool throughSettlement)
    {
        _sceneId = sceneId;
        _expiresAt = now + Math.Max(0.5, duration);
        _throughSettlement = throughSettlement;
    }

    public bool Allows(string sceneId, double now) => !string.IsNullOrEmpty(_sceneId) &&
        (_throughSettlement || now <= _expiresAt) &&
        (string.IsNullOrEmpty(sceneId) || string.Equals(_sceneId, sceneId, StringComparison.OrdinalIgnoreCase));

    public void Clear()
    {
        _sceneId = null;
        _throughSettlement = false;
        _expiresAt = 0;
    }
}
