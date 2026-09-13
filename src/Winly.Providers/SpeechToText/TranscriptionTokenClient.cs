using System.Net.Http.Json;
using System.Text.Json;
using Winly.Core.Providers;

namespace Winly.Providers.SpeechToText;

public sealed record TranscriptionToken(string Token, int ExpiresInSeconds, string Endpoint);

/// <summary>Calls <c>POST /transcribe-token</c> for a short-lived, single-session streaming token.</summary>
public sealed class TranscriptionTokenClient(HttpClient httpClient, ProxyEndpointOptions endpoint)
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    public async Task<TranscriptionToken> Mint(CancellationToken cancellationToken)
    {
        var route = endpoint.Resolve("transcribe-token");
        using var response = await httpClient.PostAsJsonAsync(route, new { }, WireOptions, cancellationToken);
        await ProxyErrors.ThrowIfFailed(response, cancellationToken);
        var token = await response.Content.ReadFromJsonAsync<TranscriptionToken>(WireOptions, cancellationToken);
        if (token is null || string.IsNullOrEmpty(token.Token) || string.IsNullOrEmpty(token.Endpoint))
        {
            throw new ProviderFailureException("provider_unavailable");
        }

        return token;
    }
}
