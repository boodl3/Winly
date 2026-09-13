using System.Runtime.CompilerServices;
using System.Text;

namespace Winly.Providers.Chat;

/// <summary>Yields the payload of each SSE <c>data:</c> event as it arrives (research.md §8).</summary>
public static class ServerSentEventReader
{
    public static async IAsyncEnumerable<string> ReadDataEvents(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    yield return data.ToString();
                    data.Clear();
                }

                continue;
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(line.AsSpan(5).TrimStart());
            }
            // event:, id: and retry: fields are not used by the Worker contract.
        }

        if (data.Length > 0)
        {
            yield return data.ToString();
        }
    }
}
