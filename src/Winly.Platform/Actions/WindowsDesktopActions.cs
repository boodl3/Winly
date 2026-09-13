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
    private static readonly TimeSpan ReadinessPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MessageProbeTimeout = TimeSpan.FromMilliseconds(100);

    private MusicLinkStatus? _linkStatus;
    private DateTimeOffset _linkStatusAt = DateTimeOffset.MinValue;
    private nint _typingTarget;

    public void RememberTypingTarget() => _typingTarget = NativeDesktopMethods.GetForegroundWindow();

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
                UserInputControl.Type(action.Target, _typingTarget);
                break;
            case DesktopActionKind.Clipboard:
                return Clipboard(action.Target);
            case DesktopActionKind.OpenPath:
                OpenPath(action.Target);
                break;
            case DesktopActionKind.Click:
                if (!await ScreenClickControl.TryClick(action.Target, action.Argument, _typingTarget, cancellationToken))
                {
                    throw new DesktopActionFailedException($"I couldn't find {action.Target} on your screen to click.");
                }

                break;
            default:
                throw new DesktopActionFailedException("I don't know how to do that yet.");
        }

        return null;
    }

    /// <summary>
    /// Waits until an application this request launched can actually be acted on.
    ///
    /// Deliberately matched against the live process list rather than against the <see cref="Process"/>
    /// that <see cref="Process.Start(ProcessStartInfo)"/> handed back: Spotify, Discord, Teams and
    /// every Electron application start a stub that spawns the real process and exits, so that
    /// handle is dead within a second and never owned a window. Waiting on it looks like it works
    /// and returns immediately for exactly the applications worth waiting for.
    /// </summary>
    public async Task<bool> WaitForApplicationReady(string appName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RespondsToMessages(AppMatcher.FindWindowed(appName)))
            {
                Log.Debug("{App} is ready to be acted on", appName);
                return true;
            }

            await Task.Delay(ReadinessPollInterval, cancellationToken);
        }

        Log.Information("{App} did not become ready within {Seconds}s", appName, timeout.TotalSeconds);
        return false;
    }

    /// <summary>
    /// A window that exists is not the same as a window that is listening. WM_NULL does nothing at
    /// the far end, so the only thing being tested is whether the reply comes back at all: a splash
    /// screen or an application stuck on a modal update prompt never answers.
    /// </summary>
    private static bool RespondsToMessages(Process? process)
    {
        if (process is null)
        {
            return false;
        }

        try
        {
            var window = process.MainWindowHandle;
            return window != nint.Zero
                && NativeDesktopMethods.SendMessageTimeout(
                    window,
                    NativeDesktopMethods.NullMessage,
                    0,
                    0,
                    NativeDesktopMethods.AbortIfHung,
                    (uint)MessageProbeTimeout.TotalMilliseconds,
                    out _) != nint.Zero;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            // The process exited between being matched and being probed.
            return false;
        }
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

        // Not linked. The desktop app is already signed in, so rather than asking the user to link
        // the same account a second time, drive the window they already have: open the search and
        // press the matching row's own play control.
        Log.Information("Spotify is not linked; opening a search for {Query}", spokenQuery);
        Launch($"spotify:search:{Uri.EscapeDataString(spokenQuery)}", spokenQuery);

        if (queueOnly)
        {
            // Queueing sits behind a row's context menu rather than a named control, so it is the
            // one music verb still waiting on a walk of that menu.
            throw new DesktopActionFailedException($"I've opened a search for {spokenQuery}, but I can't add things to the queue yet — only play them.");
        }

        if (!await SpotifyUiControl.TryPlaySearchResult(spokenQuery, cancellationToken))
        {
            throw new DesktopActionFailedException($"I opened a search for {spokenQuery} but couldn't tell which result you meant.");
        }
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
    /// whatever it sends. Three ways to reach it, best first: the account API when it is linked,
    /// then the slider in Spotify's own window through UI Automation, and only then the mixer.
    ///
    /// The mixer is last because it is not the thing the user asked for. It leaves the slider they
    /// are looking at exactly where it was, and it has no entry at all for an app that is not
    /// currently making sound.
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

        // The app's own slider, for the same reason Play drives the window rather than the API: no
        // account to link, and it moves the control the user is actually looking at. UI Automation
        // is a blocking COM client, so it stays off the sequence's thread.
        if (isSpotify && await Task.Run(() => SpotifyUiControl.TrySetVolume(amount, relative), cancellationToken))
        {
            return;
        }

        try
        {
            SetWindowsVolume(target, amount, relative);
        }
        catch (DesktopActionFailedException) when (isSpotify)
        {
            // Both of the ways that move Spotify's own volume are gone and the mixer has no entry
            // either, which only happens when its window is closed. Saying so without saying what
            // would fix it is the unhelpful half of the truth.
            throw new DesktopActionFailedException(
                "I couldn't reach Spotify's own volume, and it isn't playing anything for me to turn "
                + "down through Windows either. Open its window and I can move its slider.");
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
