namespace Winly.Core.Companion;

/// <summary>Enforces the legal transitions from data-model.md; anything else throws.</summary>
public sealed class CompanionStateMachine
{
    private static readonly Dictionary<CompanionState, CompanionState[]> LegalTransitions = new()
    {
        [CompanionState.Idle] = [CompanionState.Listening],
        [CompanionState.Listening] = [CompanionState.Working, CompanionState.Idle],
        [CompanionState.Working] = [CompanionState.Speaking, CompanionState.Idle, CompanionState.Listening],
        [CompanionState.Speaking] = [CompanionState.Idle, CompanionState.Listening],
    };

    private readonly object _gate = new();

    public CompanionState State { get; private set; } = CompanionState.Idle;

    public event Action<CompanionState>? StateChanged;

    public static bool IsLegal(CompanionState from, CompanionState to) => LegalTransitions[from].Contains(to);

    public void TransitionTo(CompanionState next)
    {
        lock (_gate)
        {
            if (!IsLegal(State, next))
            {
                throw new InvalidOperationException($"Illegal companion state transition {State} -> {next}.");
            }

            State = next;
        }

        StateChanged?.Invoke(next);
    }
}
