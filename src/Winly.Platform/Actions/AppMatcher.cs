using System.Diagnostics;

namespace Winly.Platform.Actions;

/// <summary>
/// Works out which running process a spoken app name means.
///
/// A process's name is rarely what anyone calls it out loud: Edge runs as "msedge", VS Code as
/// "Code", Zen Browser as "zen". The file description is the name the vendor put on the app and
/// is almost always what the user says, so it is matched first-class alongside the process name
/// and the window title.
/// </summary>
internal static class AppMatcher
{
    /// <summary>Best windowed process for a spoken name, or null when nothing plausible is open.</summary>
    public static Process? FindWindowed(string spokenName)
    {
        Process[] running;
        try
        {
            running = Process.GetProcesses();
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        Process? best = null;
        var bestScore = 0;
        foreach (var process in running)
        {
            var score = HasWindow(process) ? Score(process, spokenName) : 0;
            if (score > bestScore)
            {
                best?.Dispose();
                (best, bestScore) = (process, score);
            }
            else
            {
                process.Dispose();
            }
        }

        return best;
    }

    /// <summary>Whether a process — identified by an audio session, say — is the named app.</summary>
    public static bool Matches(Process process, string spokenName) => Score(process, spokenName) > 0;

    /// <summary>Higher is a more confident match; 0 is no match at all.</summary>
    private static int Score(Process process, string spokenName)
    {
        var name = spokenName.Trim();
        if (name.Length == 0)
        {
            return 0;
        }

        var processName = Safe(() => process.ProcessName);
        if (processName.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        var description = Description(process);
        if (description.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        if (processName.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (description.Contains(name, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        // "VS Code" is neither the process name nor inside the description, but one of its words
        // names the process exactly. People shorten app names this way constantly.
        foreach (var word in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length >= 3 && word.Equals(processName, StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }
        }

        // Last resort: "Spotify Premium", "Inbox - Gmail" and the like. Weakest because a window
        // title is whatever document happens to be open.
        return Safe(() => process.MainWindowTitle).Contains(name, StringComparison.OrdinalIgnoreCase) ? 2 : 0;
    }

    private static bool HasWindow(Process process) => Safe(() => process.MainWindowHandle.ToString()) is not ("" or "0");

    /// <summary>The vendor's own name for the app, or empty for a process we may not inspect.</summary>
    private static string Description(Process process)
    {
        try
        {
            return process.MainModule?.FileVersionInfo.FileDescription ?? string.Empty;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // Protected, 32/64-bit mismatched, or exited between calls: no description to read.
            return string.Empty;
        }
    }

    private static string Safe(Func<string> read)
    {
        try
        {
            return read() ?? string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return string.Empty;
        }
    }
}
