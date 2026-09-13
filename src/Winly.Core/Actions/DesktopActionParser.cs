using System.Text.Json;
using System.Text.RegularExpressions;

namespace Winly.Core.Actions;

/// <summary>
/// Mirrors <see cref="Winly.Core.Pointing.PointingDesignationParser"/> for the <c>@@DO …@@</c>
/// designation: the Worker normally extracts it, so this strips any residual in-band markup and
/// recovers the action from it when the structured one is missing.
///
/// Everything the model sends is validated here rather than at the point of use, so an executor
/// can assume an action it receives names something and carries a usable number.
/// </summary>
public static partial class DesktopActionParser
{
    /// <summary>A day. Long enough for any spoken timer, short enough to be an obvious typo guard.</summary>
    public const int MaxTimerSeconds = 86_400;

    public static (string SpokenText, DesktopAction? Action) Parse(string answerText, DesktopAction? structuredAction)
    {
        var action = structuredAction;
        if (action is null)
        {
            var match = ActionTag().Match(answerText);
            if (match.Success)
            {
                action = TryParseTag(match.Groups[1].Value);
            }
        }

        return (ActionTag().Replace(answerText, string.Empty).Trim(), action);
    }

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
