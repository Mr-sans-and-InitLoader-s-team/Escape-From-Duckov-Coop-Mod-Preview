namespace EscapeFromDuckovCoopMod;

internal enum TravelStage { Voting, Checking, Departure, Paying, Committed, Cancelled }

internal sealed class TravelBarrier
{
    private readonly HashSet<string> _participants;
    private readonly HashSet<string> _replies = new(StringComparer.Ordinal);
    public TravelStage Stage { get; private set; } = TravelStage.Voting;
    public TravelBarrier(IEnumerable<string> participants) => _participants = new HashSet<string>(participants, StringComparer.Ordinal);
    public bool Contains(string playerId) => _participants.Contains(playerId);
    public bool BeginCheck()
    {
        if (Stage != TravelStage.Voting || _participants.Count == 0) return false;
        Stage = TravelStage.Checking;
        _replies.Clear();
        return true;
    }
    public bool Reply(string playerId, TravelStage expected, bool success)
    {
        if (Stage != expected || !_participants.Contains(playerId)) return false;
        if (!success) { Stage = TravelStage.Cancelled; return false; }
        _replies.Add(playerId);
        return _replies.Count == _participants.Count;
    }
    public bool Advance(TravelStage expected, TravelStage next)
    {
        if (Stage != expected || _replies.Count != _participants.Count) return false;
        Stage = next;
        _replies.Clear();
        return true;
    }
    public void Cancel()
    {
        if (Stage != TravelStage.Committed) Stage = TravelStage.Cancelled;
    }
}
