namespace EscapeFromDuckovCoopMod;

internal sealed class LocalPlayerDeathReport
{
    private object _health;
    private bool _wasDead;
    private bool _reported;
    public string LifeId { get; private set; }

    public void Observe(object health, bool isDead, float currentHealth)
    {
        if (!ReferenceEquals(_health, health) || (_wasDead && !isDead && currentHealth > 0f))
        {
            _health = health;
            LifeId = Guid.NewGuid().ToString("N");
            _reported = false;
        }
        _wasDead = isDead;
    }

    public bool TryReport(bool isDead, float currentHealth)
    {
        if (!isDead || currentHealth > 0f || _reported || string.IsNullOrEmpty(LifeId)) return false;
        _reported = true;
        return true;
    }

    public void Reset()
    {
        _health = null;
        LifeId = null;
        _wasDead = _reported = false;
    }
}

internal sealed class PlayerDeathReportGate<TPeer> where TPeer : class
{
    private sealed class Life
    {
        public string Id;
        public bool Dead;
        public bool Consumed;
    }
    private readonly Dictionary<TPeer, Life> _lives = new();

    public void Observe(TPeer peer, string lifeId, bool isDead)
    {
        if (peer == null || !Guid.TryParseExact(lifeId, "N", out _)) return;
        if (!_lives.TryGetValue(peer, out var life) || life.Id != lifeId)
            _lives[peer] = life = new Life { Id = lifeId };
        life.Dead = isDead;
        // A positive HP snapshot alone must never unlock an already consumed life.
    }

    public bool CanConsume(TPeer peer, string lifeId) => peer != null && _lives.TryGetValue(peer, out var life) &&
        life.Id == lifeId && life.Dead && !life.Consumed;

    public bool TryConsume(TPeer peer, string lifeId)
    {
        if (!CanConsume(peer, lifeId)) return false;
        _lives[peer].Consumed = true;
        return true;
    }
    public void Remove(TPeer peer) => _lives.Remove(peer);
    public void Clear() => _lives.Clear();
}
