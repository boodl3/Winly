using Winly.Core.Companion;

namespace Winly.Core.Tests.Companion;

public class AnswerTextSanitizerTests
{
    [Theory]
    [InlineData("The Save button is in the top right corner, next to the search box.")]
    [InlineData("Don't worry, it's safe! Version 3.5 fixed that.")]
    [InlineData("Press Ctrl+S to save, or use File > Save As.")]
    public void PlainSentencesPassThroughUnchanged(string sentence) =>
        Assert.Equal(sentence, AnswerTextSanitizer.Sanitize(sentence));

    [Fact]
    public void MarkdownEmphasisAndCodeAreStripped() =>
        Assert.Equal("Click the Save button, then run npm install.",
            AnswerTextSanitizer.Sanitize("Click the **Save** button, then run `npm install`."));

    [Fact]
    public void ListSyntaxAndHeadingsAreStripped() =>
        Assert.Equal("Steps Open settings. Choose a key. Save.",
            AnswerTextSanitizer.Sanitize("## Steps\n- Open settings.\n* Choose a key.\n1. Save."));

    [Fact]
    public void LinksKeepTheirTextOnly() =>
        Assert.Equal("See the docs for details.",
            AnswerTextSanitizer.Sanitize("See [the docs](https://example.com/docs) for details."));

    [Fact]
    public void CodeFencesTablesAndHtmlAreRemoved() =>
        Assert.Equal("Run this: dotnet build Name Value",
            AnswerTextSanitizer.Sanitize("Run this:\n```bash\ndotnet build\n```\n| Name | Value |\n<br/>"));

    [Fact]
    public void WhitespaceIsCollapsed() =>
        Assert.Equal("One two three.", AnswerTextSanitizer.Sanitize("  One\n\n  two\t three.  "));
}
