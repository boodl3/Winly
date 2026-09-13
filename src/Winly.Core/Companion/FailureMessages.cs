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
}
