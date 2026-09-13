using System.Net.Http.Json;
using Winly.Core.Providers;

namespace Winly.Providers.TextToSpeech;

/// <summary>
/// Posts sanitized answer text to <c>/tts</c> and hands back the body as it arrives, so playback
/// starts on the first bytes instead of the last (SC-001). The backend picks the format:
/// <c>audio/L16</c> carries its sample rate as a content-type parameter, anything else is MP3.
/// </summary>
public sealed class ProxyTextToSpeechProvider(HttpClient httpClient, ProxyEndpointOptions endpoint) : ITextToSpeechProvider
{
    public async Task<SpokenAudio> Synthesize(string spokenText, string? previousText, CancellationToken cancellationToken)
    {
        var route = endpoint.Resolve("tts");
        using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(new { text = spokenText, previousText }) };
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            await ProxyErrors.ThrowIfFailed(response, cancellationToken);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        // Deliberately not disposing the response: the caller reads the body after this returns, and
        // disposing the body stream is what releases the pooled connection.
        var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return new SpokenAudio(body, PcmSampleRate(response));
    }

    private static int? PcmSampleRate(HttpResponseMessage response)
    {
        var contentType = response.Content.Headers.ContentType;
        if (!string.Equals(contentType?.MediaType, "audio/L16", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rate = contentType!.Parameters.FirstOrDefault(parameter => parameter.Name.Equals("rate", StringComparison.OrdinalIgnoreCase));
        return int.TryParse(rate?.Value, out var hertz) && hertz > 0 ? hertz : null;
    }
}
