using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using Serilog;
using Windows.Media.Control;
using Winly.Core.Actions;
using Winly.Core.Providers;

namespace Winly.Platform.Actions;

/// <summary>
/// Carries out the <c>@@DO …@@</c> designations on this desktop.
///
/// The model names what to act on, never how: an app is resolved against what is actually
/// installed (App Paths, then the Start Menu) and launched with no arguments, a URL is opened
/// only when it is http or https, a path is searched for under the user's own profile, and a
/// player URI is built here from a spoken query. No model text ever reaches a command line.
/// </summary>
public sealed class WindowsDesktopActions(IMusicService music) : IDesktopActions
{
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\";
    private const string SpotifyProcessName = "Spotify";

    private static readonly TimeSpan LinkStatusFreshness = TimeSpan.FromMinutes(5);

    private MusicLinkStatus? _linkStatus;
    private DateTimeOffset _linkStatusAt = DateTimeOffset.MinValue;

    public async Task<string?> Run(DesktopAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (action.Kind)
        {
            case DesktopActionKind.Open:
                Open(action.Target);
                break;
            case DesktopActionKind.Play:
                await Play(action.Target, queueOnly: false, cancellationToken);
                break;
            case DesktopActionKind.Queue:
                await Play(action.Target, queueOnly: true, cancellationToken);
                break;
            case DesktopActionKind.Media:
                await ControlMedia(action.Target, cancellationToken);
                break;
            case DesktopActionKind.Volume:
                await SetVolume(action.Target, action.Amount, action.Relative, cancellationToken);
                break;
            case DesktopActionKind.Window:
                WindowControl.Apply(action.Target, action.Argument);
                break;
            case DesktopActionKind.System:
                await SystemControl.Apply(action.Target, action.Amount, action.Relative, cancellationToken);
                break;
            case DesktopActionKind.Type:
                UserInputControl.Type(action.Target);
                break;
            case DesktopActionKind.Clipboard:
                return Clipboard(action.Target);
            case DesktopActionKind.OpenPath:
                OpenPath(action.Target);
                break;
            default:
                throw new DesktopActionFailedException("I don't know how to do that yet.");
        }

        return null;
    }

    /// <summary>
    /// Local media transport first, because it costs nothing and covers every player. Spotify gets
    /// enriched from the account when it is linked, which is where the device and its own volume
    /// come from — the two things the Windows mixer cannot tell us.
    /// </summary>
    public async Task<NowPlaying?> CurrentlyPlaying(CancellationToken cancellationToken)
    {
        NowPlaying? local = null;
        try
        {
            var session = (await SessionManager(cancellationToken)).GetCurrentSession();
            if (session is not null)
            {
                var media = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
                local = new NowPlaying(
                    FriendlyAppName(session.SourceAppUserModelId),
                    media?.Title ?? string.Empty,
                    media?.Artist ?? string.Empty,
                    session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Debug(exception, "No media session could be read");
        }

        if (local is null || !local.AppId.Contains(SpotifyProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return local;
        }

        try
        {
            if ((await LinkStatus(cancellationToken)).IsLinked)
            {
                return await music.Playing(cancellationToken) ?? local;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Debug(exception, "Spotify state could not be read; keeping the local one");
        }

        return local;
    }

    // ---- open ----------------------------------------------------------------------------

    private static void Open(string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var url))
        {
            if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
            {
                throw new DesktopActionFailedException("I can only open web links, not that kind of address.");
            }

            Launch(url.AbsoluteUri, target);
            return;
        }

        // Anything that is not a plain name could steer the registry lookup somewhere else.
        if (target.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new DesktopActionFailedException($"I couldn't find an app called {target}.");
        }

        using (var alreadyOpen = AppMatcher.FindWindowed(target))
        {
            if (alreadyOpen is not null)
            {
                // Launching a second copy of something already on screen is never what was meant.
                WindowControl.Apply(target, "focus");
                return;
            }
        }

        var resolved = ResolveFromAppPaths(target) ?? ResolveFromStartMenu(target)
            ?? throw new DesktopActionFailedException($"I couldn't find an app called {target}.");
        Launch(resolved, target);
    }

    private static void Launch(string path, string spokenName)
    {
        try
        {
            using var started = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            Log.Information("Opened {Target}", spokenName);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            throw new DesktopActionFailedException($"I couldn't open {spokenName}.");
        }
    }

    private static string? ResolveFromAppPaths(string name)
    {
        foreach (var hive in (RegistryKey[])[Registry.CurrentUser, Registry.LocalMachine])
        {
            using var key = hive.OpenSubKey(AppPathsKey + name + ".exe");
            if (key?.GetValue(null) is string value && value.Trim('"') is var path && File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    // ponytail: walks the Start Menu on every miss. Cache it if opening apps ever feels slow.
    private static string? ResolveFromStartMenu(string name)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        string? prefixMatch = null;
        foreach (var folder in (Environment.SpecialFolder[])[Environment.SpecialFolder.CommonStartMenu, Environment.SpecialFolder.StartMenu])
        {
            var root = Environment.GetFolderPath(folder);
            if (root.Length == 0 || !Directory.Exists(root))
            {
                continue;
            }

            foreach (var shortcut in Directory.EnumerateFiles(root, "*.lnk", options))
            {
                var label = Path.GetFileNameWithoutExtension(shortcut);
                if (label.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return shortcut;
                }

                prefixMatch ??= label.StartsWith(name, StringComparison.OrdinalIgnoreCase) ? shortcut : null;
            }
        }

        return prefixMatch;
    }

    // ---- files ---------------------------------------------------------------------------

    /// <summary>
    /// Opens a file or folder the user named, searched for under their own profile only. Nothing
    /// outside it is reachable, and nothing is ever deleted or overwritten.
    /// </summary>
    private static void OpenPath(string name)
    {
        if (name.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new DesktopActionFailedException($"I couldn't find anything called {name}.");
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MaxRecursionDepth = 4, // ponytail: enough for the usual places; index it if this gets slow.
        };

        foreach (var folder in (Environment.SpecialFolder[])
                 [
                     Environment.SpecialFolder.Desktop,
                     Environment.SpecialFolder.MyDocuments,
                     Environment.SpecialFolder.UserProfile,
                 ])
        {
            var root = Environment.GetFolderPath(folder);
            if (root.Length == 0 || !Directory.Exists(root))
            {
                continue;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(root, $"*{name}*", options))
            {
                Launch(entry, Path.GetFileName(entry));
                return;
            }
        }

        throw new DesktopActionFailedException($"I couldn't find anything called {name}.");
    }

    private static string? Clipboard(string command)
    {
        switch (command.ToLowerInvariant())
        {
            case "copy":
                UserInputControl.Copy();
                return null;
            case "read" or "say":
                var text = UserInputControl.ReadText();
                // Capped because this is read aloud: a copied document should not become a monologue.
                return text.Length == 0
                    ? "There's no text on your clipboard right now."
                    : $"Your clipboard says: {text[..Math.Min(text.Length, 600)]}";
            default:
                throw new DesktopActionFailedException("I can copy the selection or read the clipboard out.");
        }
    }

    // ---- music ---------------------------------------------------------------------------

    private async Task Play(string spokenQuery, bool queueOnly, CancellationToken cancellationToken)
    {
        if ((await LinkStatus(cancellationToken)).IsLinked)
        {
            var started = await music.Play(spokenQuery, queueOnly, cancellationToken);
            Log.Information("{Verb} {Track}", queueOnly ? "Queued" : "Playing", started);
            return;
        }

        // Not linked: the search screen at least lands in the app rather than a browser tab, and
        // the spoken failure tells the user how to make the real thing work.
        Log.Information("Spotify is not linked; opening a search for {Query}", spokenQuery);
        Launch($"spotify:search:{Uri.EscapeDataString(spokenQuery)}", spokenQuery);
        throw new MusicNotLinkedException();
    }

    /// <summary>
    /// Spotify's own transport when it is the current player and the account is linked, because
    /// that survives the app not being focused; the system transport for everything else.
    /// </summary>
    private async Task ControlMedia(string command, CancellationToken cancellationToken)
    {
        var session = (await SessionManager(cancellationToken)).GetCurrentSession();
        var isSpotify = session is not null
            && session.SourceAppUserModelId.Contains(SpotifyProcessName, StringComparison.OrdinalIgnoreCase);

        if (isSpotify && (await LinkStatus(cancellationToken)).IsLinked)
        {
            await music.Transport(command, cancellationToken);
            Log.Information("Sent {Command} to Spotify", command);
            return;
        }

        if (session is null)
        {
            throw new DesktopActionFailedException("Nothing is playing right now.");
        }

        var accepted = command.ToLowerInvariant() switch
        {
            "playpause" or "toggle" => await session.TryTogglePlayPauseAsync().AsTask(cancellationToken),
            "play" or "resume" => await session.TryPlayAsync().AsTask(cancellationToken),
            "pause" => await session.TryPauseAsync().AsTask(cancellationToken),
            "next" or "skip" => await session.TrySkipNextAsync().AsTask(cancellationToken),
            "previous" or "back" => await session.TrySkipPreviousAsync().AsTask(cancellationToken),
            "stop" => await session.TryStopAsync().AsTask(cancellationToken),
            "shuffle" or "repeat" => throw new DesktopActionFailedException("I can only shuffle or repeat on Spotify."),
            _ => throw new DesktopActionFailedException("I don't know that playback control."),
        };

        var player = FriendlyAppName(session.SourceAppUserModelId);
        if (!accepted)
        {
            throw new DesktopActionFailedException($"{player} wouldn't take that just now.");
        }

        Log.Information("Sent {Command} to {Player}", command, player);
    }

    // ---- volume --------------------------------------------------------------------------

    /// <summary>
    /// "Turn Spotify down" means Spotify's own slider, not the Windows mixer entry that attenuates
    /// whatever it sends. When the account is linked the account API moves the real one; otherwise
    /// this falls back to the mixer and says so is not possible, since the mixer still works.
    /// </summary>
    private async Task SetVolume(string target, int amount, bool relative, CancellationToken cancellationToken)
    {
        if (target.Length == 0)
        {
            SetWindowsVolume(appName: string.Empty, amount, relative);
            return;
        }

        var isSpotify = target.Contains(SpotifyProcessName, StringComparison.OrdinalIgnoreCase);
        if (isSpotify && (await LinkStatus(cancellationToken)).IsLinked)
        {
            var applied = await music.SetVolume(amount, relative, cancellationToken);
            Log.Information("Spotify's own volume now {Level}%", applied);
            return;
        }

        try
        {
            SetWindowsVolume(target, amount, relative);
        }
        catch (DesktopActionFailedException) when (isSpotify)
        {
            // The mixer only has an entry for an app that is actually producing sound. Saying so
            // without saying what would fix it is the unhelpful half of the truth.
            throw new DesktopActionFailedException(
                "Spotify isn't playing anything for me to turn down through Windows, and I'm not "
                + "connected to your Spotify account yet. Connect it in the Winly panel and I can "
                + "set its own volume whether or not it's playing.");
        }
    }

    private static void SetWindowsVolume(string appName, int amount, bool relative)
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice device;
        try
        {
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (COMException exception)
        {
            Log.Warning(exception, "No render endpoint available for a volume change");
            throw new DesktopActionFailedException("I couldn't find a speaker to change the volume on.");
        }

        using (device)
        {
            if (appName.Length == 0)
            {
                var endpoint = device.AudioEndpointVolume;
                var wantedMaster = Adjust(endpoint.MasterVolumeLevelScalar, amount, relative);
                endpoint.MasterVolumeLevelScalar = wantedMaster;
                Log.Information("System volume now {Level:P0}", wantedMaster);
                return;
            }

            var matches = SessionsFor(device, appName);
            if (matches.Count == 0)
            {
                throw new DesktopActionFailedException($"{appName} isn't playing anything right now.");
            }

            // Every match moves to the same level, taken from whichever session is loudest now, so
            // a player split across several audio sessions does not end up at different volumes.
            var wanted = Adjust(matches.Max(session => session.SimpleAudioVolume.Volume), amount, relative);
            foreach (var session in matches)
            {
                session.SimpleAudioVolume.Volume = wanted;
            }

            Log.Information("Windows mixer volume for {App} now {Level:P0}", appName, wanted);
        }
    }

    /// <summary>Absolute percentage, or a signed step away from where the volume is right now.</summary>
    private static float Adjust(float current, int amount, bool relative) =>
        Math.Clamp(relative ? current + (amount / 100f) : amount / 100f, 0f, 1f);

    private static List<AudioSessionControl> SessionsFor(MMDevice device, string appName)
    {
        var sessions = device.AudioSessionManager.Sessions;
        var matches = new List<AudioSessionControl>();
        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            if (IsSessionFor(session, appName))
            {
                matches.Add(session);
            }
        }

        return matches;
    }

    private static bool IsSessionFor(AudioSessionControl session, string appName)
    {
        try
        {
            using var process = Process.GetProcessById((int)session.GetProcessID);
            return AppMatcher.Matches(process, appName);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or COMException)
        {
            // The session outlived its process, or belongs to one this user cannot inspect.
            return false;
        }
    }

    // ---- shared ---------------------------------------------------------------------------

    /// <summary>Cached briefly: every activation asks, and the answer changes only when the user links.</summary>
    private async Task<MusicLinkStatus> LinkStatus(CancellationToken cancellationToken)
    {
        if (_linkStatus is not null && DateTimeOffset.UtcNow - _linkStatusAt < LinkStatusFreshness)
        {
            return _linkStatus;
        }

        _linkStatus = await music.Status(cancellationToken);
        _linkStatusAt = DateTimeOffset.UtcNow;
        return _linkStatus;
    }

    private static Task<GlobalSystemMediaTransportControlsSessionManager> SessionManager(CancellationToken cancellationToken) =>
        GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken);

    /// <summary>Turns "Spotify.exe" or a packaged id into something worth saying out loud.</summary>
    private static string FriendlyAppName(string appUserModelId)
    {
        var name = appUserModelId.Contains('!') ? appUserModelId[(appUserModelId.IndexOf('!') + 1)..] : appUserModelId;
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
