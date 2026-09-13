using Winly.Core.Companion;

namespace Winly.Core.Tests.Companion;

public class CompanionStateMachineTests
{
    private static readonly HashSet<(CompanionState From, CompanionState To)> LegalPerDataModel =
    [
        (CompanionState.Idle, CompanionState.Listening),
        (CompanionState.Listening, CompanionState.Working),
        (CompanionState.Listening, CompanionState.Idle),
        (CompanionState.Working, CompanionState.Speaking),
        (CompanionState.Working, CompanionState.Idle),
        (CompanionState.Working, CompanionState.Listening),
        (CompanionState.Speaking, CompanionState.Idle),
        (CompanionState.Speaking, CompanionState.Listening),
    ];

    public static TheoryData<CompanionState, CompanionState> EveryPair()
    {
        var data = new TheoryData<CompanionState, CompanionState>();
        foreach (var from in Enum.GetValues<CompanionState>())
        {
            foreach (var to in Enum.GetValues<CompanionState>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void OnlyDataModelTransitionsAreAccepted(CompanionState from, CompanionState to)
    {
        var machine = DriveTo(from);

        if (LegalPerDataModel.Contains((from, to)))
        {
            machine.TransitionTo(to);
            Assert.Equal(to, machine.State);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => machine.TransitionTo(to));
            Assert.Equal(from, machine.State);
        }
    }

    [Fact]
    public void NewActivationInterruptsWorkingAndSpeaking()
    {
        var fromWorking = DriveTo(CompanionState.Working);
        fromWorking.TransitionTo(CompanionState.Listening);
        Assert.Equal(CompanionState.Listening, fromWorking.State);

        var fromSpeaking = DriveTo(CompanionState.Speaking);
        fromSpeaking.TransitionTo(CompanionState.Listening);
        Assert.Equal(CompanionState.Listening, fromSpeaking.State);
    }

    [Fact]
    public void StateChangedFiresWithTheNewState()
    {
        var machine = new CompanionStateMachine();
        var observed = new List<CompanionState>();
        machine.StateChanged += observed.Add;

        machine.TransitionTo(CompanionState.Listening);
        machine.TransitionTo(CompanionState.Working);

        Assert.Equal([CompanionState.Listening, CompanionState.Working], observed);
    }

    private static CompanionStateMachine DriveTo(CompanionState target)
    {
        var machine = new CompanionStateMachine();
        foreach (var step in new[] { CompanionState.Listening, CompanionState.Working, CompanionState.Speaking })
        {
            if (machine.State == target)
            {
                break;
            }

            machine.TransitionTo(step);
        }

        return machine;
    }
}
