namespace Winly.Core.Companion;

/// <summary>
/// The current run's exchanges, in order. Lives in memory only: it is never serialized,
/// so a restart always begins empty (FR-013, FR-014).
/// </summary>
public sealed class ConversationSession
{
    private readonly List<Exchange> _exchanges = [];

    public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<Exchange> Exchanges
    {
        get
        {
            lock (_exchanges)
            {
                return _exchanges.ToArray();
            }
        }
    }

    /// <summary>
    /// How long a new question may follow the previous one and still count as part of the same
    /// conversation. Past it, the earlier turns are dropped rather than sent.
    ///
    /// History exists for "and the one next to it?", which arrives seconds later. Nothing aged it
    /// out before, so a question asked an hour after the last one arrived directly behind it and was
    /// indistinguishable from an immediate follow-up — which is how an unrelated answer ends up
    /// referring back to the previous chat.
    /// </summary>
    public static readonly TimeSpan FollowUpWindow = TimeSpan.FromMinutes(3);

    /// <summary>
    /// The last <paramref name="count"/> exchanges, stopping at the first pause longer than
    /// <see cref="FollowUpWindow"/>. Only these are sent with a new question: a long session would
    /// otherwise re-upload every earlier turn on every turn, for context a voice companion does not
    /// use (FR-013 keeps the whole session in memory either way).
    /// </summary>
    public IReadOnlyList<Exchange> Recent(int count) => Recent(count, DateTimeOffset.UtcNow);

    /// <summary>As <see cref="Recent(int)"/>, with the current moment supplied. For tests.</summary>
    public IReadOnlyList<Exchange> Recent(int count, DateTimeOffset asOf)
    {
        lock (_exchanges)
        {
            // Backwards from the newest, measuring each gap against the turn that followed it, so a
            // conversation that genuinely ran for ten minutes survives whole while a single pause
            // cuts everything before it. The newest exchange is measured against now, which is what
            // drops the whole history when the user simply comes back later.
            var next = asOf;
            var taken = 0;
            while (taken < count && taken < _exchanges.Count)
            {
                var candidate = _exchanges[^(taken + 1)];
                if (next - candidate.CreatedAtUtc > FollowUpWindow)
                {
                    break;
                }

                next = candidate.CreatedAtUtc;
                taken++;
            }

            return taken == 0 ? [] : _exchanges[^taken..].ToArray();
        }
    }

    public void Append(Exchange exchange)
    {
        lock (_exchanges)
        {
            _exchanges.Add(exchange);
        }
    }
}
