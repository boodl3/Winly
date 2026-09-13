using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using Serilog;
using Winly.App.Overlay;
using Winly.App.Panel;
using Winly.App.Tray;
using Winly.Core.Abstractions;
using Winly.Core.Companion;
using Winly.Core.Providers;
using Winly.Core.Settings;
using Winly.Platform.Actions;
using Winly.Platform.Audio;
using Winly.Platform.Capture;
using Winly.Platform.Input;
using Winly.Platform.Settings;
using Winly.Providers;
using Winly.Providers.Chat;
using Winly.Providers.Music;
using Winly.Providers.SpeechToText;
using Winly.Providers.TextToSpeech;

namespace Winly.App;

/// <summary>Composition root: single-instance guard, logging, and hand-wired construction of the whole loop.</summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Global\WinlyApp.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private TrayIconHost? _tray;
    private CompanionOverlayHost? _overlay;
    private CompanionPanelWindow? _panel;
    private CompanionOrchestrator? _orchestrator;
    private JsonFileSettingsStore? _settingsStore;
    private ProxyMusicService? _music;
    private ReminderScheduler? _reminders;
    private JsonLinesActionRecord? _actionRecord;

    protected override async void OnStartup(StartupEventArgs startupEventArgs)
    {
        base.OnStartup(startupEventArgs);
        ConfigureLogging();
        RegisterLastResortHandlers();

        if (!TryClaimSingleInstance())
        {
            Log.Information("Another instance is already running; exiting");
            await TrayIconHost.ShowAlreadyRunningNotification();
            Shutdown();
            return;
        }

        _settingsStore = new JsonFileSettingsStore(JsonFileSettingsStore.DefaultFilePath);
        var isFirstRun = !_settingsStore.HasPersistedSettings;
        var settings = await _settingsStore.Load();
        if (string.IsNullOrWhiteSpace(settings.InstallId))
        {
            settings = settings with { InstallId = Guid.NewGuid().ToString() };
            await _settingsStore.Save(settings);
        }
        if (isFirstRun)
        {
            // Disclose what is captured, when, and where it goes before anything can be captured (FR-026).
            if (new FirstRunDisclosureWindow().ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            await _settingsStore.Save(settings);
        }

        var proxy = ProxyEndpointOptions.FromEnvironment();
        // A base address, never a credential (Constitution Principle I) - and the one fact worth
        // having in the log, since "not configured" is otherwise indistinguishable from an outage.
        // Whether the token is present, never its value: "not configured" and "the backend is down"
        // are otherwise indistinguishable in a log that says neither, and a missing token now looks
        // exactly like an outage from the client side.
        Log.Information(
            "Backend {Backend} token {Token}",
            proxy.IsConfigured ? proxy.BaseAddress!.AbsoluteUri : "not configured",
            proxy.HasToken ? "present" : "missing");
        // One pooled HTTP/2 client for every route: the token mint at key-down warms the connection
        // that /chat and /tts then multiplex over, so neither pays for a fresh handshake (SC-001).
        var httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
            // Without this a cold connect walks the backend's AAAA records first and waits out a
            // 21 s SYN timeout each, on any network whose IPv6 path is broken. See HappyEyeballsConnect.
            ConnectCallback = HappyEyeballsConnect.Connect,
        })
        {
            Timeout = TimeSpan.FromSeconds(60),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        // Set once on the shared client, so every route is gated and a route added later cannot
        // forget. The backend refuses an unauthenticated POST outright.
        if (proxy.HasToken)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", proxy.Token);
        }
        _music = new ProxyMusicService(httpClient, proxy, settings.InstallId);
        _reminders = new ReminderScheduler();
        _actionRecord = new JsonLinesActionRecord();
        _overlay = new CompanionOverlayHost(settings.CompanionVisibilityMode);
        _orchestrator = new CompanionOrchestrator(
            new LowLevelKeyboardHookActivationMonitor(),
            new WasapiMicrophoneCapture(),
            new GraphicsCaptureDisplayCapture(),
            // The transcriber is told what is open, because "pin Zen Browser" is only transcribable
            // by something that has heard of Zen Browser.
            new StreamingSpeechToTextProvider(new TranscriptionTokenClient(httpClient, proxy), AppMatcher.WindowedAppNames),
            new ProxyChatProvider(httpClient, proxy),
            new ProxyTextToSpeechProvider(httpClient, proxy),
            new NAudioPlayback(),
            _overlay,
            new WindowsDesktopActions(_music),
            _reminders,
            _actionRecord,
            new ConversationSession(),
            settings,
            Log.Logger);

        _panel = new CompanionPanelWindow(_orchestrator, ApplySettings, ConnectMusic, _actionRecord);
        _tray = new TrayIconHost(openPanel: () => _panel.ShowPanel(), exit: Shutdown);

        _orchestrator.StateMachine.StateChanged += state => Dispatcher.InvokeAsync(() =>
        {
            // Listening = microphone open; Working starts with the display snapshot (FR-027).
            _tray.SetCaptureActive(state is CompanionState.Listening or CompanionState.Working);
            _panel.ShowState(state);
        });
        _orchestrator.UserMessagePosted += message => Dispatcher.InvokeAsync(() =>
        {
            _panel.ShowMessage(message);
            _tray.Notify("Winly", message);
        });

        _panel.ShowBackend(
            !proxy.IsConfigured
                ? $"Backend: not configured — set {ProxyEndpointOptions.EnvironmentVariableName} and restart"
                : !proxy.HasToken
                    // The backend answers 401 to everything without this, which reads from the
                    // client exactly like an outage. Say which it is, here, once.
                    ? $"Backend: {proxy.BaseAddress} — no token; set {ProxyEndpointOptions.TokenVariableName} and restart"
                    : $"Backend: {proxy.BaseAddress}");
        if (!proxy.IsConfigured)
        {
            var message = FailureMessages.For(new Core.Providers.ProxyNotConfiguredException());
            _panel.ShowMessage(message);
            _tray.Notify("Winly", message);
        }

        try
        {
            await _orchestrator.Start(CancellationToken.None);
        }
        catch (ActivationKeyUnavailableException exception)
        {
            Log.Error(exception, "Activation key could not be registered");
            var message = FailureMessages.For(exception);
            _panel.ShowMessage(message);
            _tray.Notify("Winly", message);
        }

        TryApplyRunAtLogin(settings.RunAtLogin);
        Log.Information("Winly started");
    }

    /// <summary>Sends the user to the backend's sign-in page; the tokens it mints never come back here.</summary>
    private async Task ConnectMusic()
    {
        var status = await _music!.Status(CancellationToken.None);
        if (!status.IsConfigured)
        {
            _panel!.ShowMessage("This backend has no Spotify credentials set up, so there's nothing to connect to yet.");
            return;
        }

        if (status.LinkUrl is null)
        {
            _panel!.ShowMessage("I couldn't reach the backend to start the Spotify sign-in.");
            return;
        }

        _panel!.ShowMessage(status.IsLinked
            ? "Spotify is already connected. Opening the sign-in page again will re-link it."
            : "Opening Spotify sign-in in your browser.");
        using var browser = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(status.LinkUrl.AbsoluteUri) { UseShellExecute = true });
    }

    protected override void OnExit(ExitEventArgs exitEventArgs)
    {
        _reminders?.Dispose();
        _orchestrator?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _overlay?.Dispose();
        _tray?.Dispose();
        _singleInstanceMutex?.Dispose();
        Log.Information("Winly exited");
        Log.CloseAndFlush();
        base.OnExit(exitEventArgs);
    }

    private async Task ApplySettings(UserSettings updated)
    {
        var previous = _orchestrator!.Settings;
        _orchestrator.Settings = updated;
        _overlay!.VisibilityMode = updated.CompanionVisibilityMode;
        await _settingsStore!.Save(updated);
        TryApplyRunAtLogin(updated.RunAtLogin);

        if (previous.ActivationKeyCombination != updated.ActivationKeyCombination)
        {
            try
            {
                await _orchestrator.ApplyActivationKey(updated.ActivationKeyCombination);
            }
            catch (Exception exception) when (exception is ActivationKeyUnavailableException or ArgumentException)
            {
                Log.Error(exception, "New activation key rejected; restoring the previous one");
                _panel!.ShowMessage(FailureMessages.For(exception));
                _orchestrator.Settings = updated with { ActivationKeyCombination = previous.ActivationKeyCombination };
                await _settingsStore.Save(_orchestrator.Settings);
                await _orchestrator.ApplyActivationKey(previous.ActivationKeyCombination);
            }
        }
    }

    private static void TryApplyRunAtLogin(bool runAtLogin)
    {
        try
        {
            RunAtLoginRegistrar.Apply(runAtLogin, Environment.ProcessPath ?? throw new InvalidOperationException("Process path unknown"));
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Run-at-login setting could not be applied");
        }
    }

    private bool TryClaimSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
            return isFirstInstance;
        }
        catch (UnauthorizedAccessException)
        {
            // The mutex exists but belongs to another session's instance.
            return false;
        }
    }

    private static void ConfigureLogging()
    {
        var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winly", "logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(logDirectory, "winly-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();
    }

    private void RegisterLastResortHandlers()
    {
        // Constitution Principle VII: nothing may take the process, or the hotkey, down silently.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception; process is terminating");
    }
}
