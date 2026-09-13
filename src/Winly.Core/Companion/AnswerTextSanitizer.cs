using System.Text.RegularExpressions;

namespace Winly.Core.Companion;

/// <summary>Turns answer text into something that reads naturally aloud (FR-012).</summary>
public static partial class AnswerTextSanitizer
{
    public static string Sanitize(string text)
    {
        var result = text.Replace("\r\n", "\n");
        result = CodeFenceLine().Replace(result, string.Empty);
        result = HtmlTag().Replace(result, " ");
        result = MarkdownLink().Replace(result, "$1");
        result = LinePrefixMarkup().Replace(result, string.Empty);
        result = EmphasisWrapper().Replace(result, "$2");
        result = StrayMarkupSymbols().Replace(result, " ");
        return Whitespace().Replace(result, " ").Trim();
    }

    [GeneratedRegex(@"^[ \t]*```.*$", RegexOptions.Multiline)]
    private static partial Regex CodeFenceLine();

    [GeneratedRegex(@"<[^>\n]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    // Headings, bullets, numbered items, block quotes and table rows at the start of a line.
    [GeneratedRegex(@"^[ \t]*(?:#{1,6}[ \t]+|[-*+][ \t]+|\d+[.)][ \t]+|>[ \t]*|\|[ \t]*)", RegexOptions.Multiline)]
    private static partial Regex LinePrefixMarkup();

    [GeneratedRegex(@"(\*{1,3}|_{1,3}|`{1,3}|~~)(\S(?:.*?\S)?)\1")]
    private static partial Regex EmphasisWrapper();

    [GeneratedRegex(@"[*#`~|]+|^[-=]{3,}[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex StrayMarkupSymbols();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
