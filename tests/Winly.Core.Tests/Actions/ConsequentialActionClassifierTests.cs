using Winly.Core.Actions;

namespace Winly.Core.Tests.Actions;

public class ConsequentialActionClassifierTests
{
    /// <summary>
    /// Every verb, deliberately spelled out rather than derived. Adding a verb without deciding
    /// whether it needs confirming must fail here rather than quietly default to "no".
    /// </summary>
    private static readonly Dictionary<DesktopActionKind, bool> ExpectedForATypicalUse = new()
    {
        [DesktopActionKind.Open] = false,
        [DesktopActionKind.Play] = false,
        [DesktopActionKind.Queue] = false,
        [DesktopActionKind.Media] = false,
        [DesktopActionKind.Volume] = false,
        [DesktopActionKind.Window] = false,   // focus/minimize/left/… ; close is covered below
        [DesktopActionKind.System] = false,   // darkmode/brightness/… ; lock and sleep below
        [DesktopActionKind.Type] = true,
        [DesktopActionKind.Clipboard] = false,
        [DesktopActionKind.OpenPath] = false,
        [DesktopActionKind.Click] = false,    // an ordinary link or button; destructive labels below
        [DesktopActionKind.Timer] = false,
    };

    [Fact]
    public void EveryVerbIsClassified()
    {
        var everyKind = Enum.GetValues<DesktopActionKind>().ToHashSet();

        Assert.Equal(everyKind, ExpectedForATypicalUse.Keys.ToHashSet());
    }

    [Fact]
    public void ATypicalUseOfEachVerbIsClassifiedAsExpected()
    {
        foreach (var (kind, expected) in ExpectedForATypicalUse)
        {
            var action = kind switch
            {
                DesktopActionKind.Window => new DesktopAction(kind, "Chrome", "focus"),
                DesktopActionKind.System => new DesktopAction(kind, "darkmode"),
                _ => new DesktopAction(kind, "something"),
            };

            Assert.Equal(expected, ConsequentialActionClassifier.IsConsequential(action));
        }
    }

    [Theory]
    [InlineData("Play Procreate Tutorial for Beginners", false)]
    [InlineData("Accept", false)]
    [InlineData("Next page", false)]
    [InlineData("Delete account", true)]
    [InlineData("delete", true)]
    [InlineData("Buy now", true)]
    [InlineData("Send", true)]
    [InlineData("Unsubscribe", true)]
    public void ClickingSomethingDestructiveNeedsConfirming(string label, bool expected) =>
        Assert.Equal(expected, ConsequentialActionClassifier.IsConsequential(
            new DesktopAction(DesktopActionKind.Click, label)));

    [Theory]
    [InlineData("close", true)]
    [InlineData("CLOSE", true)]
    [InlineData("focus", false)]
    [InlineData("minimize", false)]
    [InlineData("maximize", false)]
    [InlineData("restore", false)]
    [InlineData("left", false)]
    [InlineData("right", false)]
    public void OnlyClosingAWindowNeedsConfirming(string argument, bool expected) =>
        Assert.Equal(expected, ConsequentialActionClassifier.IsConsequential(
            new DesktopAction(DesktopActionKind.Window, "Word", argument)));

    [Theory]
    [InlineData("lock", true)]
    [InlineData("sleep", true)]
    [InlineData("darkmode", false)]
    [InlineData("lightmode", false)]
    [InlineData("brightness", false)]
    [InlineData("mute", false)]
    [InlineData("wifi", false)]
    [InlineData("bluetooth", false)]
    [InlineData("showdesktop", false)]
    public void OnlyEndingTheSessionNeedsConfirming(string target, bool expected) =>
        Assert.Equal(expected, ConsequentialActionClassifier.IsConsequential(
            new DesktopAction(DesktopActionKind.System, target)));

    [Fact]
    public void AnUnrecognisedVerbIsConfirmedRatherThanWavedThrough() =>
        Assert.True(ConsequentialActionClassifier.IsConsequential(new DesktopAction((DesktopActionKind)999, "who knows")));

    [Fact]
    public void TheQuestionNamesWhatIsAboutToHappenAndAsks()
    {
        var question = ConsequentialActionClassifier.Ask(new DesktopAction(DesktopActionKind.Window, "Word", "close"));

        Assert.Contains("Word", question, StringComparison.Ordinal);
        Assert.EndsWith("?", question, StringComparison.Ordinal);
    }

    [Fact]
    public void TypingIsDescribedWithoutRepeatingWhatWouldBeTyped()
    {
        var action = new DesktopAction(DesktopActionKind.Type, "my bank password is hunter2");

        Assert.DoesNotContain("hunter2", ConsequentialActionClassifier.Describe(action), StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", ConsequentialActionClassifier.Ask(action), StringComparison.Ordinal);
    }
}
