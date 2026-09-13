using System.Text.Json;
using System.Text.RegularExpressions;

namespace Winly.Core.Pointing;

/// <summary>
/// The Worker already separates the structured <c>pointingTarget</c> from narration; this
/// strips any residual in-band <c>@@POINT …@@</c> / <c>@@NOPOINT@@</c> markup defensively and
/// recovers a designation from it when the structured one is missing (FR-016, FR-017).
/// </summary>
public static partial class PointingDesignationParser
{
    public static (string SpokenText, PointingTarget? Target) Parse(string rawAnswer, PointingTarget? structuredTarget)
    {
        var target = structuredTarget;
        if (target is null)
        {
            var match = PointTag().Match(rawAnswer);
            if (match.Success)
            {
                target = TryParseTag(match.Groups[1].Value);
            }
        }

        var spoken = PointTag().Replace(rawAnswer, string.Empty);
        spoken = NoPointTag().Replace(spoken, string.Empty);
        spoken = TrailingAtSigns().Replace(spoken, string.Empty);
        return (spoken.Trim(), target);
    }

    private static PointingTarget? TryParseTag(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("monitorId", out var monitorId) || monitorId.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("x", out var x) || !x.TryGetInt32(out var xValue)
                || !root.TryGetProperty("y", out var y) || !y.TryGetInt32(out var yValue))
            {
                return null;
            }

            var label = root.TryGetProperty("label", out var labelElement) && labelElement.ValueKind == JsonValueKind.String
                ? labelElement.GetString()!
                : string.Empty;
            return new PointingTarget(monitorId.GetString()!, xValue, yValue, label);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"@@POINT\s*(\{[\s\S]*?\})\s*@@")]
    private static partial Regex PointTag();

    [GeneratedRegex(@"@@NOPOINT@@")]
    private static partial Regex NoPointTag();

    [GeneratedRegex(@"@+\s*$")]
    private static partial Regex TrailingAtSigns();
}
