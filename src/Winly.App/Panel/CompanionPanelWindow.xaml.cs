using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Winly.Core.Companion;
using Winly.Core.Settings;

namespace Winly.App.Panel;

/// <summary>Borderless status and settings panel; closing hides it — the tray owns the app's lifetime.</summary>
public partial class CompanionPanelWindow : Window
{
    private readonly CompanionOrchestrator _orchestrator;
    private readonly Func<UserSettings, Task> _applySettings;
    private readonly Func<Task> _connectMusic;

    public CompanionPanelWindow(CompanionOrchestrator orchestrator, Func<UserSettings, Task> applySettings, Func<Task> connectMusic)
    {
        _orchestrator = orchestrator;
        _applySettings = applySettings;
        _connectMusic = connectMusic;
        InitializeComponent();

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;

        orchestrator.TranscriptRecognized += transcript => Dispatcher.InvokeAsync(() =>
        {
            TranscriptText.Text = transcript.Length == 0 ? "(nothing heard)" : transcript;
            AnswerText.Clear();
        });
        orchestrator.AnswerDeltaReceived += delta => Dispatcher.InvokeAsync(() =>
        {
            AnswerText.AppendText(delta);
            AnswerText.ScrollToEnd();
        });
    }

    public void ShowPanel()
    {
        LoadSettingsIntoControls(_orchestrator.Settings);
        Show();
        Activate();
    }

    public void ShowState(CompanionState state)
    {
        StateText.Text = state switch
        {
            CompanionState.Listening => "Listening…",
            CompanionState.Working => "Thinking…",
            CompanionState.Speaking => "Speaking…",
            _ => "Idle — hold the activation key and ask.",
        };
        if (state == CompanionState.Listening)
        {
            MessageText.Visibility = Visibility.Collapsed;
        }
    }

    public void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;
    }

    public void ShowBackend(string description) => BackendText.Text = description;

    protected override void OnClosing(CancelEventArgs eventArgs)
    {
        eventArgs.Cancel = true;
        Hide();
    }

    private void LoadSettingsIntoControls(UserSettings settings)
    {
        ActivationKeyBox.Text = settings.ActivationKeyCombination;
        VisibilityBox.SelectedItem = VisibilityBox.Items.Cast<ComboBoxItem>()
            .First(item => (string)item.Tag == settings.CompanionVisibilityMode.ToString());
        ScreenCaptureBox.IsChecked = settings.ScreenCaptureEnabled;
        MicrophoneCaptureBox.IsChecked = settings.MicrophoneCaptureEnabled;
        DesktopActionsBox.IsChecked = settings.DesktopActionsEnabled;
        RunAtLoginBox.IsChecked = settings.RunAtLogin;
    }

    private async void OnSave(object sender, RoutedEventArgs eventArgs)
    {
        var updated = new UserSettings
        {
            ActivationKeyCombination = ActivationKeyBox.Text.Trim(),
            CompanionVisibilityMode = Enum.Parse<CompanionVisibilityMode>((string)((ComboBoxItem)VisibilityBox.SelectedItem).Tag),
            ScreenCaptureEnabled = ScreenCaptureBox.IsChecked == true,
            MicrophoneCaptureEnabled = MicrophoneCaptureBox.IsChecked == true,
            DesktopActionsEnabled = DesktopActionsBox.IsChecked == true,
            RunAtLogin = RunAtLoginBox.IsChecked == true,
        };
        await _applySettings(updated);
        LoadSettingsIntoControls(_orchestrator.Settings);
        ShowMessage("Settings saved.");
    }

    private async void OnConnectMusic(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            await _connectMusic();
        }
        catch (Exception exception)
        {
            // A settings panel must never take the app down with it (Principle VII).
            Serilog.Log.Error(exception, "Spotify connect could not be started");
            ShowMessage("I couldn't start the Spotify sign-in just now.");
        }
    }

    private void OnClose(object sender, RoutedEventArgs eventArgs) => Hide();

    private void OnHeaderDrag(object sender, MouseButtonEventArgs eventArgs) => DragMove();
}
