using System.Windows.Automation;
using Serilog;
using Winly.Core.Actions;

namespace Winly.Platform.Actions;

/// <summary>
/// Presses a control the user named out loud — a video on a results page, a button in a dialog —
/// by finding it in the window's accessibility tree and invoking it, the same interface a screen
/// reader drives.
///
/// Generalised from <see cref="SpotifyUiControl"/>, which proved the shape: no model round trip, no
/// pixels, and nothing to re-derive when the layout moves. It is also why this verb is not
/// a vision loop — a browser hands over its whole page as labelled, invokable elements for free.
///
/// The model never names a control it has not seen. It reads the label off the screenshot; which
/// element that corresponds to is decided here, against what the tree actually offers
/// (Constitution Principle VIII).
/// </summary>
internal static class ScreenClickControl
{
    /// <summary>
    /// Chromium builds its accessibility tree lazily, on the first UI Automation request, so the
    /// first look at a browser window can come back nearly empty. Polling costs nothing and is the
    /// difference between working and looking broken on the first click of a session.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// A results page is thousands of elements. Past this the answer is not in the list anyway, and
    /// the per-action time bound matters more than exhaustiveness.
    /// ponytail: flat cap. Narrow the scope to the focused pane if a real page ever overruns it.
    /// </summary>
    private const int MaxCandidates = 3000;

    /// <summary>Control types worth pressing. Everything else is layout, text or chrome.</summary>
    private static readonly Condition Clickable = new AndCondition(
        new PropertyCondition(AutomationElement.IsEnabledProperty, true),
        new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Image)));

    /// <param name="appName">The app to look in, or empty to use <paramref name="fallbackWindow"/>.</param>
    /// <param name="fallbackWindow">
    /// Where the user was looking when they started speaking, not wherever the foreground happens
    /// to be by the time the sequence runs — the same instant a typing action aims at.
    /// </param>
    internal static async Task<bool> TryClick(
        string label,
        string appName,
        nint fallbackWindow,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + Timeout;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            // UI Automation is a blocking COM client walking a large tree, so it stays off the
            // thread the action sequence is running on.
            if (await Task.Run(() => TryClickOnce(label, appName, fallbackWindow), cancellationToken))
            {
                return true;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
        while (DateTimeOffset.UtcNow < deadline);

        Log.Information("Nothing on screen matched {Label} within {Seconds}s", label, Timeout.TotalSeconds);
        return false;
    }

    private static bool TryClickOnce(string label, string appName, nint fallbackWindow)
    {
        try
        {
            var handle = WindowFor(appName, fallbackWindow);
            if (handle == nint.Zero)
            {
                return false;
            }

            var window = AutomationElement.FromHandle(handle);
            if (window is null)
            {
                return false;
            }

            var candidates = new List<AutomationElement>();
            var labels = new List<string>();
            foreach (AutomationElement element in window.FindAll(TreeScope.Descendants, Clickable))
            {
                var name = element.Current.Name;
                if (name.Length == 0)
                {
                    continue;
                }

                candidates.Add(element);
                labels.Add(name);
                if (candidates.Count == MaxCandidates)
                {
                    break;
                }
            }

            var chosen = ScreenElementMatcher.Choose(label, labels);
            if (chosen < 0)
            {
                return false;
            }

            // So the user sees what was pressed, and because a virtualised list is more reliable to
            // invoke once the element has actually been realised.
            if (candidates[chosen].TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll))
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

            if (candidates[chosen].TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
            {
                ((InvokePattern)invoke).Invoke();
            }
            else if (candidates[chosen].TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select))
            {
                // List rows and tabs are selected rather than invoked.
                ((SelectionItemPattern)select).Select();
            }
            else
            {
                // ponytail: no synthetic mouse fallback. Add one only if a real app turns up that
                // labels a control and then exposes no way to act on it.
                Log.Information("{Label} matched {Match} but offered no way to press it", label, labels[chosen]);
                return false;
            }

            Log.Information("Pressed {Match} for {Label}", labels[chosen], label);
            return true;
        }
        catch (ElementNotAvailableException)
        {
            // The tree changed underneath the walk because the page was still rendering; the caller polls.
            return false;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            Log.Warning(failure, "Could not press {Label}", label);
            return false;
        }
    }

    private static nint WindowFor(string appName, nint fallbackWindow)
    {
        if (appName.Length == 0)
        {
            return fallbackWindow;
        }

        // Falls back rather than giving up: the model names the app by reading the screenshot, and
        // it says "Edge" for a Zen window and "Chrome" for anything else blue often enough to
        // matter. The window the user was actually looking at when they spoke is a better answer
        // than no window at all, and it is the same instant a typing action aims at.
        using var process = AppMatcher.FindWindowed(appName);
        var handle = process?.MainWindowHandle ?? nint.Zero;
        if (handle == nint.Zero)
        {
            Log.Information("No window found for {App}; clicking in the window that was in front instead", appName);
        }

        return handle == nint.Zero ? fallbackWindow : handle;
    }
}
