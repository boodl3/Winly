using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Winly.Core.Actions;
using Winly.Core.Pointing;
using Winly.Core.Providers;

namespace Winly.Providers.Chat;

/// <summary>Posts to <c>/chat</c> and consumes the SSE answer incrementally. Sends no credential (FR-034).</summary>
public sealed class ProxyChatProvider(HttpClient httpClient, ProxyEndpointOptions endpoint) : IChatProvider
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    private sealed record DisplayWire(string MonitorId, string ImageBase64, int WidthPx, int HeightPx, bool IsPrimary);

    private sealed record HistoryWire(string Role, string Content);

    private sealed record ChatWire(string Transcript, DisplayWire[] Displays, HistoryWire[] History, string? NowPlaying, bool ScreenAvailable, bool WebSearchAllowed);

    public async Task<ChatAnswer> Ask(ChatRequest request, Action<string> onAnswerDelta, CancellationToken cancellationToken)
    {
        var route = endpoint.Resolve("chat");
        var payload = new ChatWire(
            request.Transcript,
            request.Displays
                .Select(display => new DisplayWire(display.MonitorId, Convert.ToBase64String(display.ImageBytes), display.WidthPx, display.HeightPx, display.IsPrimary))
                .ToArray(),
            request.History
                .SelectMany(exchange => new[] { new HistoryWire("user", exchange.Transcript), new HistoryWire("assistant", exchange.AnswerText) })
                .ToArray(),
            request.NowPlaying?.Describe() is { Length: > 0 } playing ? playing : null,
            request.ScreenAvailable,
            request.WebSearchAllowed);

        using var message = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(payload, options: WireOptions) };
        // ResponseHeadersRead is what makes the deltas arrive as they are produced (research.md §8).
        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await ProxyErrors.ThrowIfFailed(response, cancellationToken);

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await ReadAnswer(body, onAnswerDelta, cancellationToken);
    }

    public static async Task<ChatAnswer> ReadAnswer(Stream serverSentEvents, Action<string> onAnswerDelta, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        PointingTarget? target = null;
        IReadOnlyList<DesktopAction> actions = [];
        var needsScreen = false;
        var needsWeb = false;
        var awaitingReply = false;
        await foreach (var data in ServerSentEventReader.ReadDataEvents(serverSentEvents, cancellationToken))
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (root.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String)
            {
                var chunk = delta.GetString()!;
                text.Append(chunk);
                onAnswerDelta(chunk);
            }

            if (!root.TryGetProperty("done", out var done) || done.ValueKind != JsonValueKind.True)
            {
                continue;
            }

            if (root.TryGetProperty("pointingTarget", out var pointing) && pointing.ValueKind == JsonValueKind.Object)
            {
                target = new PointingTarget(
                    pointing.GetProperty("monitorId").GetString() ?? string.Empty,
                    pointing.GetProperty("x").GetInt32(),
                    pointing.GetProperty("y").GetInt32(),
                    pointing.TryGetProperty("label", out var label) ? label.GetString() ?? string.Empty : string.Empty);
            }

            if (root.TryGetProperty("actions", out var actionsElement) && actionsElement.ValueKind == JsonValueKind.Array)
            {
                var parsed = new List<DesktopAction>();
                foreach (var element in actionsElement.EnumerateArray())
                {
                    var one = DesktopActionParser.TryParseTag(element.GetRawText());
                    if (one is not null)
                    {
                        parsed.Add(one);
                    }
                }

                actions = parsed;
            }

            needsScreen = root.TryGetProperty("needsScreen", out var asked) && asked.ValueKind == JsonValueKind.True;
            needsWeb = root.TryGetProperty("needsWebSearch", out var wanted) && wanted.ValueKind == JsonValueKind.True;
            awaitingReply = root.TryGetProperty("awaitingReply", out var listening) && listening.ValueKind == JsonValueKind.True;
        }

        return new ChatAnswer(text.ToString(), target, actions, needsScreen, needsWeb, awaitingReply);
    }
}
