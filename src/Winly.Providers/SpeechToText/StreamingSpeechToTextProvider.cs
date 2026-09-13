using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Winly.Core.Providers;

namespace Winly.Providers.SpeechToText;

/// <summary>
/// Streams PCM16 straight to the transcription provider over a websocket authorised by a
/// minted token (research.md §7). The client never holds a long-lived provider credential.
/// Wire format: AssemblyAI Streaming v3 — binary audio in, JSON <c>Turn</c>/<c>Termination</c> messages out.
/// </summary>
/// <param name="runningAppNames">
/// What the user could plausibly name out loud right now, boosted in the decoder. Optional
/// because only the Windows layer can answer it; without it the fixed command vocabulary is
/// still sent. Called once per activation and never allowed to fail a transcription.
/// </param>
public sealed class StreamingSpeechToTextProvider(
    TranscriptionTokenClient tokenClient,
    Func<IReadOnlyList<string>>? runningAppNames = null) : ISpeechToTextProvider
{
    private const int AudioChunkBytes = 3200; // 100 ms of 16 kHz mono PCM16
    private static readonly byte[] TerminateMessage = Encoding.UTF8.GetBytes("{\"type\":\"Terminate\"}");

    /// <summary>
    /// Words the decoder should lean towards. A one-word command has no sentence around it to
    /// disambiguate a phoneme — "pin Claude" came back as "pinch Claude" — so the verbs Winly
    /// acts on are declared up front. The app names are the other half and come from the machine,
    /// not from here, because "Zen Browser" is only worth boosting on a machine running it.
    /// </summary>
    private static readonly string[] CommandKeyTerms =
    [
        "pin", "snap", "dock", "minimize", "maximize", "restore", "focus", "switch to", "close",
        "open", "launch", "quit", "left half", "right half", "to the left", "to the right",
        "play", "pause", "resume", "skip", "next track", "previous track", "queue", "shuffle",
        "volume", "mute", "unmute", "louder", "quieter", "turn it down", "turn it up",
        "brightness", "dark mode", "light mode", "night light", "Wi-Fi", "Bluetooth",
        "lock", "sleep", "show desktop", "clipboard", "copy", "paste", "timer", "remind me",
        "screenshot", "this button", "this window", "read this",
        "click", "click on", "press", "tap", "video", "thumbnail", "link", "the first one",
    ];

    // App names go first: the fixed verbs are already spelled the way people say them, while an
    // app name is the thing a general transcriber has never heard. 40 plus the list above stays
    // under the provider's limit of 100 terms, which the test asserts rather than a Take() hiding
    // it — truncating silently would drop verbs off the end and nothing would say so.
    private const int MaxAppKeyTerms = 40;

    public async Task<string> Transcribe(Stream pcm16MonoAudio, CancellationToken cancellationToken)
    {
        var token = await tokenClient.Mint(cancellationToken);
        var separator = token.Endpoint.Contains('?') ? '&' : '?';
        var keyTerms = Uri.EscapeDataString(JsonSerializer.Serialize(KeyTerms(ReadAppNames())));
        var streamUri = new Uri($"{token.Endpoint}{separator}keyterms_prompt={keyTerms}&token={Uri.EscapeDataString(token.Token)}");

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(streamUri, cancellationToken);

        var turns = new SortedDictionary<int, string>();
        var sending = SendAudio(socket, pcm16MonoAudio, cancellationToken);
        var receiving = ReceiveTurns(socket, turns, sending, cancellationToken);
        await Task.WhenAll(sending, receiving);

        if (socket.State == WebSocketState.Open)
        {
            try
            {
                // CloseOutputAsync, not CloseAsync: the latter waits for the peer's close frame, which
                // is the round trip the early return above exists to skip. The transcript is in hand.
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken);
            }
            catch (WebSocketException)
            {
                // The provider may close first; the transcript is already complete.
            }
        }

        return string.Join(' ', turns.Values.Where(turn => turn.Length > 0));
    }

    private IReadOnlyList<string> ReadAppNames()
    {
        try
        {
            return runningAppNames?.Invoke() ?? [];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Enumerating processes is best-effort context, never a reason to lose the utterance.
            return [];
        }
    }

    /// <summary>Internal for the test: the cap, the ordering and the junk filter are the whole logic.</summary>
    internal static string[] KeyTerms(IReadOnlyList<string> appNames) =>
        appNames
            .Where(IsUsableTerm)
            .Take(MaxAppKeyTerms)
            .Concat(CommandKeyTerms)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    // A window title or a vendor string can be a sentence; boosting one of those is noise.
    private static bool IsUsableTerm(string term) =>
        term.Length is > 1 and <= 40 && term.Count(character => character == ' ') <= 5;

    private static async Task SendAudio(ClientWebSocket socket, Stream audio, CancellationToken cancellationToken)
    {
        var buffer = new byte[AudioChunkBytes];
        int read;
        while ((read = await audio.ReadAtLeastAsync(buffer, AudioChunkBytes, throwOnEndOfStream: false, cancellationToken)) > 0)
        {
            await socket.SendAsync(buffer.AsMemory(0, read), WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
        }

        await socket.SendAsync(TerminateMessage, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    private static async Task ReceiveTurns(ClientWebSocket socket, SortedDictionary<int, string> turns, Task sending, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            using var document = JsonDocument.Parse(message.ToArray());
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            if (type == "Termination")
            {
                return;
            }

            // Every Turn message for a turn_order overwrites the last: partials grow, then the
            // final formatted one lands. Partials are kept rather than filtered out so a turn the
            // provider never finalizes still yields its words instead of silence — with the hold
            // being one turn (min_turn_silence), dropping it would drop the whole request.
            if (type == "Turn" && root.TryGetProperty("turn_order", out var order))
            {
                turns[order.GetInt32()] = root.TryGetProperty("transcript", out var transcript) ? transcript.GetString() ?? string.Empty : string.Empty;

                // The Termination message trails the finished transcript by a round trip that buys
                // nothing: once Terminate has gone out and the hold's single turn has come back
                // formatted, there is no more text coming and this is dead time on the critical path.
                // Only the single-turn case takes the shortcut — a second turn (a pause longer than
                // min_turn_silence) may still be formatting, and truncating a request is not a trade.
                if (sending.IsCompletedSuccessfully && turns.Count == 1 && IsFormattedFinal(root))
                {
                    return;
                }
            }
        }
    }

    private static bool IsFormattedFinal(JsonElement turn) =>
        turn.TryGetProperty("end_of_turn", out var ended) && ended.ValueKind == JsonValueKind.True &&
        turn.TryGetProperty("turn_is_formatted", out var formatted) && formatted.ValueKind == JsonValueKind.True;
}
