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
public sealed class StreamingSpeechToTextProvider(TranscriptionTokenClient tokenClient) : ISpeechToTextProvider
{
    private const int AudioChunkBytes = 3200; // 100 ms of 16 kHz mono PCM16
    private static readonly byte[] TerminateMessage = Encoding.UTF8.GetBytes("{\"type\":\"Terminate\"}");

    public async Task<string> Transcribe(Stream pcm16MonoAudio, CancellationToken cancellationToken)
    {
        var token = await tokenClient.Mint(cancellationToken);
        var separator = token.Endpoint.Contains('?') ? '&' : '?';
        var streamUri = new Uri($"{token.Endpoint}{separator}token={Uri.EscapeDataString(token.Token)}");

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(streamUri, cancellationToken);

        var turns = new SortedDictionary<int, string>();
        var receiving = ReceiveTurns(socket, turns, cancellationToken);
        await SendAudio(socket, pcm16MonoAudio, cancellationToken);
        await receiving;

        if (socket.State == WebSocketState.Open)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken);
            }
            catch (WebSocketException)
            {
                // The provider may close first; the transcript is already complete.
            }
        }

        return string.Join(' ', turns.Values.Where(turn => turn.Length > 0));
    }

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

    private static async Task ReceiveTurns(ClientWebSocket socket, SortedDictionary<int, string> turns, CancellationToken cancellationToken)
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

            // A formatted turn re-sends the same turn_order, so later messages overwrite earlier ones.
            if (type == "Turn"
                && root.TryGetProperty("end_of_turn", out var endOfTurn) && endOfTurn.GetBoolean()
                && root.TryGetProperty("turn_order", out var order))
            {
                turns[order.GetInt32()] = root.TryGetProperty("transcript", out var transcript) ? transcript.GetString() ?? string.Empty : string.Empty;
            }
        }
    }
}
