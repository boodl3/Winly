using System.Text;

namespace Winly.Core.Companion;

/// <summary>
/// Buffers streaming answer text and releases it one complete sentence at a time, so synthesis
/// of the first sentence can start before the model has finished writing the rest (SC-001).
/// </summary>
public sealed class SentenceAccumulator
{
    private readonly StringBuilder _pending = new();

    /// <summary>Adds streamed text and returns whichever sentences are now complete, in order.</summary>
    public IReadOnlyList<string> Append(string text)
    {
        _pending.Append(text);
        var buffer = _pending.ToString();
        List<string>? released = null;
        var sentenceStart = 0;

        for (var index = 0; index < buffer.Length; index++)
        {
            if (!IsTerminator(buffer[index]))
            {
                continue;
            }

            var sentenceEnd = index + 1;
            while (sentenceEnd < buffer.Length && IsClosingPunctuation(buffer[sentenceEnd]))
            {
                sentenceEnd++;
            }

            // Only trailing whitespace proves the sentence ended rather than the delta stopping
            // mid-token — deltas routinely split words, and "3." may yet become "3.5".
            if (sentenceEnd >= buffer.Length || !char.IsWhiteSpace(buffer[sentenceEnd]))
            {
                continue;
            }

            var sentence = buffer[sentenceStart..sentenceEnd].Trim();
            if (sentence.Length > 0)
            {
                released ??= [];
                released.Add(sentence);
            }

            sentenceStart = sentenceEnd;
            index = sentenceEnd - 1;
        }

        if (sentenceStart > 0)
        {
            _pending.Remove(0, sentenceStart);
        }

        return released ?? (IReadOnlyList<string>)[];
    }

    /// <summary>Returns whatever text never reached a sentence terminator, and empties the buffer.</summary>
    public string Flush()
    {
        var remainder = _pending.ToString().Trim();
        _pending.Clear();
        return remainder;
    }

    private static bool IsTerminator(char character) => character is '.' or '!' or '?';

    private static bool IsClosingPunctuation(char character) =>
        character is '"' or '\'' or ')' or ']' or '”' or '’';
}
