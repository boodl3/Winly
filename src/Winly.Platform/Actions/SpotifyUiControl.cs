using System.Text.RegularExpressions;
using System.Windows.Automation;
using Serilog;
using Winly.Core.Actions;

namespace Winly.Platform.Actions;

/// <summary>
/// Plays a named track by driving Spotify's own window, the way a person sitting at the machine
/// would: open the search, then press play on the row that matches.
///
/// This exists because the alternative wants the user's Spotify account linked through OAuth while
/// the desktop app is already signed in — the same credential, asked for twice. It reads the
/// accessibility tree rather than screenshots: no model round trip, no pixels, nothing to re-derive
/// when a row moves, and it is the same interface a screen reader uses.
///
/// The model never names a UI element. It supplies a song title; which control that corresponds to
/// is decided here, against what is actually on screen (Constitution Principle VIII).
/// </summary>
internal static partial class SpotifyUiControl
{
    /// <summary>The results list, as Spotify names it. Everything is scoped inside this.</summary>
    private const string ResultsGridName = "Search results";

    private static readonly TimeSpan ResultsTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Each row carries a line like "Explicit Song • Dominic Fike", or "Album • …", "Playlist • …".
    /// The word before the bullet is what separates a track from a whole album.
    /// </summary>
    [GeneratedRegex(@"^(?:Explicit\s+)?(Song|Music video|Album|Single|EP|Playlist|Artist|Podcast|Episode)\s*[•·]\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RowSubtitle();

    /// <summary>
    /// Presses play on the result matching <paramref name="spokenQuery"/>, and reports whether it
    /// managed to. A false return means the search is open but no song row ever appeared — still
    /// better than nothing on screen.
    /// </summary>
    internal static async Task<bool> TryPlaySearchResult(string spokenQuery, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ResultsTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // UI Automation is a blocking COM client walking a tree of a thousand-odd elements, so
            // it stays off whatever thread the action sequence is running on.
            // Strict while waiting: the search field updates the moment the URI navigates, but the
            // rows under it do not, so a page that has not caught up is indistinguishable from a
            // request nothing matches. Accepting the top song here plays the last request's answer.
            if (await Task.Run(() => TryPlayFromResults(spokenQuery, allowTopSongFallback: false), cancellationToken))
            {
                return true;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        // The wait is spent. Whatever is on screen now is the best there is going to be, so take the
        // app's own top-ranked song rather than leaving a search sitting there doing nothing.
        if (await Task.Run(() => TryPlayFromResults(spokenQuery, allowTopSongFallback: true), cancellationToken))
        {
            return true;
        }

        Log.Information("No Spotify song row appeared for {Query} within {Seconds}s", spokenQuery, ResultsTimeout.TotalSeconds);
        return false;
    }

    /// <summary>
    /// Moves Spotify's own volume slider — the one in its window — and reports whether it managed
    /// to.
    ///
    /// This is a different thing from the Windows mixer's per-app fader, which is what the caller
    /// falls back to. The mixer only attenuates what Spotify already sends: it leaves the slider
    /// the user is looking at exactly where it was, and it has no entry at all while Spotify is
    /// paused. "Turn Spotify down" means the slider in the app, so reach into the app for it —
    /// the same reason <see cref="TryPlaySearchResult"/> drives the window instead of the API.
    /// </summary>
    /// <param name="amount">A step to add when <paramref name="relative"/>, otherwise an absolute 0-100.</param>
    internal static bool TrySetVolume(int amount, bool relative)
    {
        try
        {
            var process = AppMatcher.FindWindowed("Spotify");
            if (process is null || process.MainWindowHandle == nint.Zero)
            {
                return false;
            }

            var window = AutomationElement.FromHandle(process.MainWindowHandle);
            if (window is null)
            {
                return false;
            }

            var seen = new List<string>();
            foreach (AutomationElement slider in window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Slider)))
            {
                var name = slider.Current.Name;
                seen.Add(name);

                // Matched by name, never "the first slider": the playback progress bar is a slider
                // too, and setting that seeks the track instead of changing the volume — which is
                // both wrong and the kind of wrong the user notices immediately.
                if (!name.Contains("volume", StringComparison.OrdinalIgnoreCase)
                    || !slider.TryGetCurrentPattern(RangeValuePattern.Pattern, out var pattern))
                {
                    continue;
                }

                var range = (RangeValuePattern)pattern;
                var (minimum, maximum) = (range.Current.Minimum, range.Current.Maximum);
                if (range.Current.IsReadOnly || maximum <= minimum)
                {
                    continue;
                }

                // Mapped through whatever range the control reports rather than assumed: this
                // slider has been 0-1 in some Spotify builds and 0-100 in others, and treating a
                // 0-1 one as percent mutes the app on every request.
                var span = maximum - minimum;
                var current = (range.Current.Value - minimum) / span * 100;
                var wanted = Math.Clamp(relative ? current + amount : amount, 0, 100);
                range.SetValue(minimum + (wanted / 100 * span));
                Log.Information("Spotify's own volume slider moved to {Level}%", (int)wanted);
                return true;
            }

            // ponytail: matched on the label Spotify gives the control today. If it is ever renamed
            // this says so plainly and the caller still has the mixer; the alternative is guessing
            // at a slider and seeking the track.
            Log.Information("Spotify showed no volume slider; sliders on offer were {Sliders}", seen);
            return false;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            Log.Warning(failure, "Could not move Spotify's volume slider");
            return false;
        }
    }

    private static bool TryPlayFromResults(string spokenQuery, bool allowTopSongFallback)
    {
        try
        {
            var process = AppMatcher.FindWindowed("Spotify");
            if (process is null || process.MainWindowHandle == nint.Zero)
            {
                return false;
            }

            var window = AutomationElement.FromHandle(process.MainWindowHandle);
            if (window is null || !SearchBoxShows(window, spokenQuery))
            {
                // The previous page's results are still up. Acting now plays whatever the *last*
                // request searched for, which looks exactly like the matcher choosing wrongly.
                return false;
            }

            // Plural, deliberately. A search can render more than one grid under this name — the
            // music-video and artist strip comes first and holds no songs at all, so taking only
            // the first one finds nothing playable and gives up while the real list sits below it.
            var grids = window?.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, ResultsGridName));
            if (grids is null || grids.Count == 0)
            {
                // Still on whatever page was open: the search has not rendered yet.
                return false;
            }

            var rows = new List<AutomationElement>();
            var described = new List<SearchResultRow>();
            foreach (AutomationElement grid in grids)
            {
                foreach (AutomationElement row in grid.FindAll(TreeScope.Children, Condition.TrueCondition))
                {
                    rows.Add(row);
                    described.Add(Describe(row));
                }
            }

            var chosen = SearchResultMatcher.Choose(spokenQuery, described, allowTopSongFallback);
            if (chosen < 0)
            {
                return false;
            }

            // Named "Play" on some rows and "Play <title>" on others, so this matches the prefix
            // rather than an exact name — an exact match silently finds nothing on half the rows.
            AutomationElement? play = null;
            foreach (AutomationElement button in rows[chosen].FindAll(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.IsEnabledProperty, true))))
            {
                var name = button.Current.Name;
                if (name.Equals("Play", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Play ", StringComparison.OrdinalIgnoreCase))
                {
                    play = button;
                    break;
                }
            }

            if (play is null || !play.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            {
                return false;
            }

            // So the user sees the row it landed on, and because a virtualised list is more
            // reliable to invoke once the row has actually been realised.
            if (play.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll))
            {
                try
                {
                    ((ScrollItemPattern)scroll).ScrollIntoView();
                }
                catch (InvalidOperationException)
                {
                    // Nothing scrollable around it; invoking still works.
                }
            }

            ((InvokePattern)pattern).Invoke();
            Log.Information(
                "Played {Title} by {Artist} from Spotify's own search results",
                described[chosen].Title,
                described[chosen].Artist);
            return true;
        }
        catch (ElementNotAvailableException)
        {
            // The tree changed underneath the walk because Spotify was still rendering; the caller polls.
            return false;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            Log.Warning(failure, "Could not drive Spotify's window");
            return false;
        }
    }

    /// <summary>
    /// Whether the search field is showing the query we asked for. This is the only thing that
    /// distinguishes "the results are ready" from "the previous results have not gone away yet",
    /// and without it the very first poll acts on the last request's page.
    /// </summary>
    private static bool SearchBoxShows(AutomationElement window, string spokenQuery)
    {
        var wanted = Squash(spokenQuery);
        foreach (AutomationElement box in window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox)))
        {
            if (box.TryGetCurrentPattern(ValuePattern.Pattern, out var value)
                && Squash(((ValuePattern)value).Current.Value ?? string.Empty) == wanted)
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotAWord();

    /// <summary>Lowercased, with runs of anything that is not a letter or digit reduced to one space.</summary>
    private static string Squash(string text) =>
        NotAWord().Replace(text.ToLowerInvariant(), " ").Trim();

    /// <summary>
    /// Reduces one result row to a title, an artist and whether it is a single track.
    ///
    /// ponytail: rows are playable only when Spotify labels them "Song" or "Music video", which
    /// skips albums, playlists and the unlabelled "Top result" card. That card is sometimes the
    /// studio recording the user meant, so a request can still land on a live or remastered cut —
    /// asked for "karma police by radiohead", the best labelled row is the live version. Fixable by
    /// recognising the top-result card's own shape; not worth it until version-picking is the
    /// complaint.
    /// </summary>
    private static SearchResultRow Describe(AutomationElement row)
    {
        var title = row.Current.Name;
        foreach (AutomationElement text in row.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            var match = RowSubtitle().Match(text.Current.Name);
            if (!match.Success)
            {
                continue;
            }

            var kind = match.Groups[1].Value;
            var isAudio = kind.Equals("Song", StringComparison.OrdinalIgnoreCase);
            var isVideo = kind.Equals("Music video", StringComparison.OrdinalIgnoreCase);
            return new SearchResultRow(title, match.Groups[2].Value, isAudio || isVideo, isAudio);
        }

        // No subtitle found: treated as not a song, so it can never be chosen by accident.
        return new SearchResultRow(title, string.Empty, IsTrack: false);
    }
}
