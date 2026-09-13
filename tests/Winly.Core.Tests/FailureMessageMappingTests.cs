using System.Net.Http;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Winly.Core.Abstractions;
using Winly.Core.Companion;
using Winly.Core.Providers;

namespace Winly.Core.Tests;

public class FailureMessageMappingTests
{
    private static readonly string[] ProviderNames = ["Anthropic", "Claude", "OpenAI", "AssemblyAI", "Cloudflare", "Deepgram", "ElevenLabs"];

    public static TheoryData<Exception> EveryMappedFailure() =>
    [
        new MicrophoneUnavailableException("AUDCLNT_E_DEVICE_IN_USE 0x8889000A"),
        new DisplayCaptureUnavailableException("E_ACCESSDENIED"),
        new PlaybackDeviceUnavailableException("MMSYSERR_BADDEVICEID"),
        new ActivationKeyUnavailableException("SetWindowsHookEx failed 1428"),
        new ProxyNotConfiguredException(),
        new ProviderFailureException("provider_unavailable"),
        new ProviderFailureException("provider_timeout"),
        new ProviderFailureException("invalid_request"),
        new HttpRequestException("No such host is known (winly-backend.workers.dev:443)"),
        new WebSocketException("The remote party closed the WebSocket connection"),
        new TimeoutException(),
        new TaskCanceledException(),
        new InvalidOperationException("NullReferenceException in Anthropic client"),
    ];

    [Theory]
    [MemberData(nameof(EveryMappedFailure))]
    public void MessagesContainNoErrorCodesExceptionNamesOrProviderNames(Exception failure)
    {
        var message = FailureMessages.For(failure);

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain("Exception", message);
        Assert.DoesNotMatch(new Regex(@"\b\d{3,}\b|0x[0-9A-Fa-f]+|\bE_\w+|\b[A-Z]{3,}_[A-Z_]+"), message);
        Assert.DoesNotContain(failure.Message, message);
        foreach (var providerName in ProviderNames)
        {
            Assert.DoesNotContain(providerName, message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TimeoutReasonsGetTheTryAgainMessage() =>
        Assert.Equal(FailureMessages.For(new TimeoutException()), FailureMessages.For(new ProviderFailureException("provider_timeout")));
}
