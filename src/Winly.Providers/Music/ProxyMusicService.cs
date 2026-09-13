using System.Net.Http.Json;
using System.Text.Json;
using Winly.Core.Actions;
using Winly.Core.Providers;

namespace Winly.Providers.Music;

/// <summary>
/// Control of the user's Spotify account through the backend's <c>/music/*</c> routes.
///
/// The client sends only its opaque install id: the account's refresh token lives in the Worker's
/// store and never reaches this machine (FR-034, Constitution Principle I).
/// </summary>
public sealed class ProxyMusicService(HttpClient httpClient, ProxyEndpointOptions endpoint, string installId) : IMusicService
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    private sealed record StatusWire(bool Configured, bool Linked, string? LinkUrl);

    private sealed record PlayingWire(string Title, string Artist, bool IsPlaying, string DeviceName, int VolumePercent);

    private sealed record StateWire(PlayingWire? Playing);

    private sealed record PlayedWire(string Title, string Artist);

    private sealed record VolumeWire(int VolumePercent);

    public async Task<MusicLinkStatus> Status(CancellationToken cancellationToken)
    {
        try
        {
            var status = await Post<StatusWire>("music/status", new { installId }, cancellationToken);
            return new MusicLinkStatus(
                status.Linked,
                Uri.TryCreate(status.LinkUrl, UriKind.Absolute, out var link) ? link : null,
                status.Configured);
        }
        catch (Exception exception) when (exception is HttpRequestException or MusicRequestFailedException)
        {
            // Not being able to ask is not the same as not being linked; report it as unusable.
            return new MusicLinkStatus(IsLinked: false, LinkUrl: null, IsConfigured: false);
        }
    }

    public async Task<NowPlaying?> Playing(CancellationToken cancellationToken)
    {
        var state = await Post<StateWire>("music/state", new { installId }, cancellationToken);
        return state.Playing is null or { Title: "" }
            ? null
            : new NowPlaying(
                "Spotify",
                state.Playing.Title,
                state.Playing.Artist,
                state.Playing.IsPlaying,
                state.Playing.VolumePercent,
                state.Playing.DeviceName);
    }

    public async Task<string> Play(string spokenQuery, bool queueOnly, CancellationToken cancellationToken)
    {
        var played = await Post<PlayedWire>("music/play", new { installId, query = spokenQuery, queueOnly }, cancellationToken);
        return played.Artist.Length > 0 ? $"{played.Title} by {played.Artist}" : played.Title;
    }

    public Task Transport(string command, CancellationToken cancellationToken) =>
        Post<JsonElement>("music/transport", new { installId, command }, cancellationToken);

    public async Task<int> SetVolume(int amount, bool relative, CancellationToken cancellationToken)
    {
        var applied = await Post<VolumeWire>("music/volume", new { installId, amount, relative }, cancellationToken);
        return applied.VolumePercent;
    }

    private async Task<T> Post<T>(string route, object payload, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(endpoint.Resolve(route), payload, WireOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await Failure(response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<T>(WireOptions, cancellationToken)
            ?? throw new MusicRequestFailedException("Spotify sent something I couldn't read.");
    }

    /// <summary>Every music failure becomes one plain sentence; the code itself never reaches the user (FR-031).</summary>
    private static async Task<Exception> Failure(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var code = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            code = document.RootElement.TryGetProperty("error", out var error) ? error.GetString() ?? string.Empty : string.Empty;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // An error body that is not the documented shape tells us nothing extra.
        }

        return code switch
        {
            "music_not_linked" => new MusicNotLinkedException(),
            "music_not_configured" => new MusicRequestFailedException(
                "Spotify isn't set up on my backend yet, so I can only control it through Windows."),
            "music_no_device" => new MusicRequestFailedException(
                "Spotify isn't open on any of your devices, so there's nothing for me to play to."),
            "music_needs_premium" => new MusicRequestFailedException(
                "Spotify only lets me control playback on a Premium account."),
            "music_no_match" => new MusicRequestFailedException("I couldn't find that on Spotify."),
            _ => new MusicRequestFailedException("I couldn't reach Spotify just now."),
        };
    }
}
