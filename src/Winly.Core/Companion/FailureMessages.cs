using System.Net.Http;
using System.Net.WebSockets;
using Winly.Core.Abstractions;
using Winly.Core.Actions;
using Winly.Core.Providers;

namespace Winly.Core.Companion;

/// <summary>
/// Maps every failure to one plain sentence with no error codes, exception names, or
/// provider names (FR-031, SC-010). The exception itself goes to the log, not the user.
/// </summary>
public static class FailureMessages
{
    public static string For(Exception failure) => failure switch
    {
        // Already written as one plain sentence for the user by whatever raised them.
        DesktopActionFailedException or MusicNotLinkedException or MusicRequestFailedException => failure.Message,
        MicrophoneUnavailableException => "I couldn't hear you because the microphone isn't available right now.",
        DisplayCaptureUnavailableException => "I couldn't see the screen just now.",
        PlaybackDeviceUnavailableException => "I couldn't find a speaker to talk through.",
        ActivationKeyUnavailableException => "That key combination can't be used here. Please pick another one in settings.",
        ProxyNotConfiguredException => "The backend address isn't set up yet. Complete quickstart step 2, then restart Winly.",
        ProviderFailureException { Reason: "provider_timeout" } => "That took too long. Please try again.",
        ProviderFailureException { Reason: "invalid_request" } => "I couldn't make sense of that request. Please try again.",
        ProviderFailureException => "The answer service isn't available right now.",
        HttpRequestException or WebSocketException => "I can't reach the answer service. Check your connection and try again.",
        TimeoutException or TaskCanceledException => "That took too long. Please try again.",
        _ => "Something went wrong on my end. Please try again.",
    };

    /// <summary>
    /// What to say when a sequence did not finish everything it was asked to (FR-008): which parts
    /// completed and which did not, in the same plain language as every other failure here. Empty
    /// when everything completed, because the spoken answer already said what was happening.
    /// </summary>
    public static string ForSequence(ActionSequenceResult sequence)
    {
        if (sequence.EverythingCompleted)
        {
            return string.Empty;
        }

        var completed = sequence.Outcomes.Count(outcome => outcome.Status == ActionOutcomeStatus.Completed);
        var stopper = sequence.Stopper;
        if (stopper is null)
        {
            return sequence.StoppedAtActionLimit
                ? $"{DoneSoFar(completed)}, but that was more than I can do in one go, so the rest didn't happen."
                : string.Empty;
        }

        // Nothing happened at all, so there is no partial progress to describe and the reason says
        // everything. This is also what keeps a one-action request sounding exactly as it did
        // before sequences existed (FR-006).
        if (completed == 0)
        {
            return stopper.UserFacingReason;
        }

        // The reason is already a whole sentence from whatever produced it, so its own full stop
        // would land in the middle of this one.
        var reason = stopper.UserFacingReason.TrimEnd('.', ' ');
        return $"{DoneSoFar(completed)}, but {reason}.";
    }

    private static string DoneSoFar(int completed) => completed switch
    {
        1 => "I got the first part done",
        _ => $"I got the first {completed} parts done",
    };
}
