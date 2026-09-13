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
    /// The last <paramref name="count"/> exchanges. Only these are sent with a new question: a long
    /// session would otherwise re-upload every earlier turn on every turn, for context a voice
    /// companion does not use (FR-013 keeps the whole session in memory either way).
    /// </summary>
    public IReadOnlyList<Exchange> Recent(int count)
    {
        lock (_exchanges)
        {
            return _exchanges.Count <= count ? _exchanges.ToArray() : _exchanges[^count..].ToArray();
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
