using System.Text.Json;
using System.Text.RegularExpressions;

namespace Winly.Core.Actions;

/// <summary>
/// Mirrors <see cref="Winly.Core.Pointing.PointingDesignationParser"/> for the <c>@@DO …@@</c>
/// designation: the Worker extracts it, so this only strips any residual in-band markup so it is
/// never read aloud as JSON.
///
/// Everything the model sends is validated here rather than at the point of use, so an executor
/// can assume an action it receives names something and carries a usable number.
/// </summary>
public static partial class DesktopActionParser
{
    /// <summary>A day. Long enough for any spoken timer, short enough to be an obvious typo guard.</summary>
    public const int MaxTimerSeconds = 86_400;

    /// <param name="structuredActions">
    /// What the Worker already extracted, carried through unchanged. Not re-derived from the text:
    /// the Worker withholds everything from the first <c>@@</c> onward while streaming and strips
    /// every tag before sending, so a designation the Worker failed to parse is not in the answer
    /// to recover from either.
    /// </param>
    /// <returns>
    /// The answer with every designation stripped, and the actions in the order the model wrote
    /// them. The list is never null and is not capped here — enforcing the per-request maximum is
    /// <see cref="DesktopActionSequenceRunner"/>'s job, so the cap lives in one place.
    /// </returns>
    public static (string SpokenText, IReadOnlyList<DesktopAction> Actions) Parse(
        string answerText,
        IReadOnlyList<DesktopAction> structuredActions) =>
        // Replace is global, so a designation that did survive into the text cannot be read aloud
        // as JSON — the strip is kept even though the recovery it used to feed is gone.
        (ActionTag().Replace(answerText, string.Empty).Trim(), structuredActions);

    /// <summary>Reads one action from its JSON body; returns null for anything Winly cannot act on.</summary>
    public static DesktopAction? TryParseTag(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("action", out var kindElement) || kindElement.ValueKind != JsonValueKind.String
                || !Enum.TryParse<DesktopActionKind>(kindElement.GetString(), ignoreCase: true, out var kind))
            {
                return null;
            }

            var target = Text(root, "target");
            var argument = Text(root, "argument");
            var relative = root.TryGetProperty("relative", out var relativeElement) && relativeElement.ValueKind == JsonValueKind.True;
            var amount = 0;
            var hasAmount = root.TryGetProperty("amount", out var amountElement) && amountElement.TryGetInt32(out amount);

            return kind switch
            {
                DesktopActionKind.Volume => hasAmount
                    ? new DesktopAction(kind, target, Amount: Clamp(amount, relative), Relative: relative)
                    : null,

                // Brightness is the one system command that carries a number; the rest are switches.
                DesktopActionKind.System when target.Equals("brightness", StringComparison.OrdinalIgnoreCase) => hasAmount
                    ? new DesktopAction(kind, target, Amount: Clamp(amount, relative), Relative: relative)
                    : null,

                DesktopActionKind.Timer => hasAmount && amount is > 0 and <= MaxTimerSeconds
                    ? new DesktopAction(kind, target, Amount: amount)
                    : null,

                // A window command needs both the app and what to do to it.
                DesktopActionKind.Window => target.Length > 0 && argument.Length > 0
                    ? new DesktopAction(kind, target, argument)
                    : null,

                // The app is optional — empty means whatever the user is looking at — so unlike a
                // window command only the label is required, but it still has to survive the
                // default branch's loss of Argument.
                DesktopActionKind.Click => target.Length > 0
                    ? new DesktopAction(kind, target, argument)
                    : null,

                // Everything else has to name what it acts on.
                _ => target.Length > 0 ? new DesktopAction(kind, target) : null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()!.Trim()
            : string.Empty;

    /// <summary>A relative step may be negative; an absolute percentage may not.</summary>
    private static int Clamp(int amount, bool relative) => Math.Clamp(amount, relative ? -100 : 0, 100);

    [GeneratedRegex(@"@@DO\s*(\{[\s\S]*?\})\s*@@")]
    private static partial Regex ActionTag();
}
