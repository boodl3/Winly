using Winly.Providers.SpeechToText;

namespace Winly.Providers.Tests;

/// <summary>
/// The decoder takes at most 100 key terms, and what gets cut when a machine has a lot of windows
/// open decides whether "pin Spotify" survives. These pin the order and the junk filter.
/// </summary>
public class StreamingKeyTermsTests
{
    [Fact]
    public void RunningAppsComeFirstSoTheCapNeverCutsThem()
    {
        var terms = StreamingSpeechToTextProvider.KeyTerms(["Zen Browser", "Claude"]);

        Assert.Equal("Zen Browser", terms[0]);
        Assert.Equal("Claude", terms[1]);
        Assert.Contains("pin", terms);
    }

    [Fact]
    public void NothingUnsayableGetsBoosted()
    {
        var terms = StreamingSpeechToTextProvider.KeyTerms(
        [
            "",
            "X",
            "Inbox (4,312) - someone@example.com - Outlook - a whole sentence of window title",
            "Notepad",
        ]);

        Assert.Equal("Notepad", terms[0]);
    }

    [Fact]
    public void TheProviderCapIsRespectedWithAnAbsurdNumberOfWindows()
    {
        var apps = Enumerable.Range(0, 200).Select(index => $"App{index}").ToArray();

        var terms = StreamingSpeechToTextProvider.KeyTerms(apps);

        // 100 is the provider's own limit. This fails if the command vocabulary is ever grown past
        // the room left for it, which is the only way the two lists can collide.
        Assert.True(terms.Length <= 100, $"{terms.Length} key terms exceeds the provider's limit of 100");
        Assert.Equal(40, terms.Count(term => term.StartsWith("App", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnAppNamedAfterACommandIsNotSentTwice()
    {
        var terms = StreamingSpeechToTextProvider.KeyTerms(["Timer", "Notepad"]);

        Assert.Single(terms, term => term.Equals("timer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WithNothingRunningTheCommandVocabularyStillGoes()
    {
        var terms = StreamingSpeechToTextProvider.KeyTerms([]);

        Assert.Contains("to the left", terms);
        Assert.Contains("Wi-Fi", terms);
    }
}
