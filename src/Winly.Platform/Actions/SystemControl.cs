using System.Management;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using Serilog;
using Windows.Devices.Radios;
using Winly.Core.Actions;
using static Winly.Platform.Actions.NativeDesktopMethods;

namespace Winly.Platform.Actions;

/// <summary>
/// The machine-wide switches: session, appearance, screen and radios.
///
/// Deliberately excludes anything irreversible. Emptying the recycle bin, deleting files and
/// changing security settings are not here, and a voice command is the wrong place to add them.
/// </summary>
internal static class SystemControl
{
    private const string PersonalizeKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static async Task Apply(string command, int amount, bool relative, CancellationToken cancellationToken)
    {
        switch (command.ToLowerInvariant())
        {
            case "lock":
                LockWorkStation();
                break;
            case "sleep" or "suspend":
                SetSuspendState(hibernate: false, force: false, wakeupEventsDisabled: false);
                break;
            case "darkmode" or "dark":
                SetTheme(light: false);
                break;
            case "lightmode" or "light":
                SetTheme(light: true);
                break;
            case "brightness":
                SetBrightness(amount, relative);
                break;
            case "mute":
                SetMute(muted: true);
                break;
            case "unmute":
                SetMute(muted: false);
                break;
            case "wifi" or "wi-fi":
                await ToggleRadio(RadioKind.WiFi, "Wi-Fi", cancellationToken);
                break;
            case "bluetooth":
                await ToggleRadio(RadioKind.Bluetooth, "Bluetooth", cancellationToken);
                break;
            case "showdesktop" or "minimizeall":
                PressWindowsKey(VirtualKeyD);
                break;
            default:
                throw new DesktopActionFailedException("I don't know that system setting.");
        }

        Log.Information("System command {Command}", command);
    }

    private static void SetTheme(bool light)
    {
        using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey)
            ?? throw new DesktopActionFailedException("I couldn't reach the appearance settings.");
        key.SetValue("AppsUseLightTheme", light ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("SystemUsesLightTheme", light ? 1 : 0, RegistryValueKind.DWord);

        // Apps repaint on this broadcast; without it the change only shows after a sign-out.
        SendNotifyMessage(BroadcastWindow, SettingChangeMessage, 0, "ImmersiveColorSet");
    }

    private static void SetMute(bool muted)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        device.AudioEndpointVolume.Mute = muted;
    }

    /// <summary>
    /// WMI is the only interface that reaches an internal panel's backlight. Desktop monitors do
    /// not implement it, so this reports that rather than failing silently.
    /// </summary>
    private static void SetBrightness(int amount, bool relative)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\WMI");
            using var monitors = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM WmiMonitorBrightness"));
            using var methods = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods"));

            var current = 50;
            foreach (ManagementBaseObject monitor in monitors.Get())
            {
                using (monitor)
                {
                    current = Convert.ToInt32(monitor["CurrentBrightness"]);
                }

                break;
            }

            var wanted = Math.Clamp(relative ? current + amount : amount, 0, 100);
            var applied = false;
            foreach (ManagementObject method in methods.Get().Cast<ManagementObject>())
            {
                using (method)
                {
                    // Timeout 0, then the level: WmiSetBrightness(uint32 Timeout, uint8 Brightness).
                    method.InvokeMethod("WmiSetBrightness", [(uint)0, (byte)wanted]);
                    applied = true;
                }
            }

            if (!applied)
            {
                throw new DesktopActionFailedException("This screen doesn't let me change its brightness.");
            }

            Log.Information("Brightness now {Level}%", wanted);
        }
        catch (ManagementException exception)
        {
            Log.Warning(exception, "Brightness could not be set");
            throw new DesktopActionFailedException("This screen doesn't let me change its brightness.");
        }
    }

    private static async Task ToggleRadio(RadioKind kind, string spokenName, CancellationToken cancellationToken)
    {
        var access = await Radio.RequestAccessAsync().AsTask(cancellationToken);
        if (access != RadioAccessStatus.Allowed)
        {
            throw new DesktopActionFailedException($"Windows won't let me change {spokenName} from here.");
        }

        var radios = await Radio.GetRadiosAsync().AsTask(cancellationToken);
        var radio = radios.FirstOrDefault(candidate => candidate.Kind == kind)
            ?? throw new DesktopActionFailedException($"This PC doesn't seem to have {spokenName}.");

        var wanted = radio.State == RadioState.On ? RadioState.Off : RadioState.On;
        var result = await radio.SetStateAsync(wanted).AsTask(cancellationToken);
        if (result != RadioAccessStatus.Allowed)
        {
            throw new DesktopActionFailedException($"I couldn't switch {spokenName} {(wanted == RadioState.On ? "on" : "off")}.");
        }

        Log.Information("{Radio} switched {State}", spokenName, wanted);
    }

    private static void PressWindowsKey(ushort virtualKey)
    {
        SyntheticInput[] sequence =
        [
            Key(VirtualKeyLeftWindows, up: false),
            Key(virtualKey, up: false),
            Key(virtualKey, up: true),
            Key(VirtualKeyLeftWindows, up: true),
        ];
        UserInputControl.Send(sequence, "pressing that");
    }

    internal static SyntheticInput Key(ushort virtualKey, bool up) => new()
    {
        Type = InputKeyboard,
        Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = up ? KeyEventKeyUp : 0 },
    };
}
