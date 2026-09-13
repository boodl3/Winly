using Winly.Core.Actions;

namespace Winly.Core.Tests.Actions;

public class SpokenConfirmationTests
{
    [Theory]
    [InlineData("yes")]
    [InlineData("Yes!")]
    [InlineData("yeah")]
    [InlineData("yep")]
    [InlineData("sure")]
    [InlineData("okay")]
    [InlineData("OK")]
    [InlineData("go ahead")]
    [InlineData("do it")]
    [InlineData("yes, go ahead")]
    [InlineData("yeah that's fine")]
    public void AClearYesIsAgreement(string transcript) =>
        Assert.True(SpokenConfirmation.IsAgreement(transcript));

    [Theory]
    [InlineData("no")]
    [InlineData("No.")]
    [InlineData("nope")]
    [InlineData("cancel")]
    [InlineData("stop")]
    [InlineData("never mind")]
    [InlineData("wait")]
    public void AClearNoIsARefusal(string transcript) =>
        Assert.False(SpokenConfirmation.IsAgreement(transcript));

    /// <summary>
    /// Silence is the ordinary outcome here, not an edge case: the user walked away, or the room is
    /// too noisy to recognise anything (FR-012b).
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NothingHeardAtAllIsARefusal(string? transcript) =>
        Assert.False(SpokenConfirmation.IsAgreement(transcript));

    [Theory]
    [InlineData("what time is the meeting")]
    [InlineData("hang on I'm on the phone")]
    [InlineData("the yellow one")]
    [InlineData("Jess can you pass me that")]
    public void AnythingNotRecognisedAsAgreementIsARefusal(string transcript) =>
        Assert.False(SpokenConfirmation.IsAgreement(transcript));

    /// <summary>A refusal very often contains an agreement phrase inside it.</summary>
    [Theory]
    [InlineData("no, don't do it")]
    [InlineData("no go ahead and leave it")]
    [InlineData("cancel, do it later")]
    public void ARefusalWinsOverAnAgreementPhraseBuriedInsideIt(string transcript) =>
        Assert.False(SpokenConfirmation.IsAgreement(transcript));

    /// <summary>"now" must never be heard as "no".</summary>
    [Theory]
    [InlineData("yes do it now")]
    [InlineData("okay now")]
    public void AWordThatMerelyContainsNoIsNotARefusal(string transcript) =>
        Assert.True(SpokenConfirmation.IsAgreement(transcript));

    [Fact]
    public void TheWindowIsShortEnoughNotToHoldTheMicrophoneOpen() =>
        Assert.InRange(SpokenConfirmation.Window, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10));
}
