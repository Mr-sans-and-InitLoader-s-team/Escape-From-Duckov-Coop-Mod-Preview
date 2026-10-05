namespace EscapeFromDuckovCoopMod;

// Pure state admission: a stop is a tombstone, so a late start cannot revive it.
internal sealed class KazooPlaybackState
{
    public const double TimeoutSeconds = 1.0;
    private bool _hasSequence;
    private uint _sequence;
    private bool _playing;
    private string _sceneId;
    private double _expiresAt;
    public float Pitch { get; private set; }
    public float Intensity { get; private set; }

    public static bool IsNewer(uint value, uint previous) => unchecked((int)(value - previous)) > 0;

    public bool Apply(uint sequence, bool playing, string sceneId, float pitch, float intensity, double now)
    {
        if (float.IsNaN(pitch) || float.IsInfinity(pitch) || float.IsNaN(intensity) || float.IsInfinity(intensity)) return false;
        if (_hasSequence && !IsNewer(sequence, _sequence)) return false;
        _hasSequence = true;
        _sequence = sequence;
        _playing = playing;
        _sceneId = sceneId;
        Pitch = pitch;
        Intensity = Math.Clamp(intensity, 0f, 1f);
        _expiresAt = now + TimeoutSeconds;
        return true;
    }

    public bool IsAudible(double now, string sceneId) => _playing && now < _expiresAt &&
        !string.IsNullOrEmpty(sceneId) && string.Equals(_sceneId, sceneId, StringComparison.Ordinal);

    public void Expire() => _expiresAt = double.NegativeInfinity;
}
