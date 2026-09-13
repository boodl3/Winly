using Microsoft.Win32;

namespace Winly.Platform.Settings;

/// <summary>Adds or removes the per-user <c>Run</c> registry value (research.md §9).</summary>
public static class RunAtLoginRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Winly";

    public static void Apply(bool runAtLogin, string executablePath)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("The Run registry key could not be opened.");
        if (runAtLogin)
        {
            runKey.SetValue(ValueName, $"\"{executablePath}\"");
        }
        else
        {
            runKey.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
