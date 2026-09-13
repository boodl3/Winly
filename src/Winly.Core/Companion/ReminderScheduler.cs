namespace Winly.Core.Companion;

/// <summary>
/// Timers the user asked for out loud. In memory only, like the conversation itself (FR-014):
/// a restart forgets them, which is the honest behaviour for something set by voice in a session.
/// </summary>
public sealed class ReminderScheduler : IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<Timer> _pending = [];

    /// <summary>Raised on a pool thread when a timer comes due. Must not throw.</summary>
    public event Action<string>? Elapsed;

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public void Schedule(TimeSpan delay, string label)
    {
        Timer? timer = null;
        timer = new Timer(
            _ =>
            {
                lock (_gate)
                {
                    _pending.Remove(timer!);
                }

                timer!.Dispose();
                Elapsed?.Invoke(label);
            },
            null,
            delay,
            Timeout.InfiniteTimeSpan);

        lock (_gate)
        {
            _pending.Add(timer);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var timer in _pending)
            {
                timer.Dispose();
            }

            _pending.Clear();
        }
    }
}
