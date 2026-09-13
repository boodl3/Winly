using System.Net.Http.Json;
using Winly.Core.Providers;

namespace Winly.Providers;

internal static class ProxyErrors
{
    private sealed record ErrorBody(string? Error);

    /// <summary>Turns a non-2xx <c>{ "error": … }</c> reply into a <see cref="ProviderFailureException"/>.</summary>
    public static async Task ThrowIfFailed(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string reason = "provider_unavailable";
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body?.Error))
            {
                reason = body.Error;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Non-JSON error body: keep the generic reason.
        }

        throw new ProviderFailureException(reason);
    }
}
