using System.Net;
using System.Text;
using Winly.Core.Companion;
using Winly.Core.Pointing;
using Winly.Core.Providers;
using Winly.Providers;
using Winly.Providers.Chat;

namespace Winly.Providers.Tests;

public class ProxyChatProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static readonly ProxyEndpointOptions Endpoint = new(new Uri("https://winly.example.test/"));

    [Fact]
    public async Task StreamsDeltasAndReturnsTheStructuredPointingTarget()
    {
        const string body = "data: {\"delta\":\"It is \"}\n\ndata: {\"delta\":\"here.\"}\n\ndata: {\"done\":true,\"pointingTarget\":{\"monitorId\":\"m1\",\"x\":12,\"y\":34,\"label\":\"Save\"}}\n\n";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") });
        var provider = new ProxyChatProvider(new HttpClient(handler), Endpoint);
        var deltas = new List<string>();

        var answer = await provider.Ask(new ChatRequest("where is save", [], []), deltas.Add, CancellationToken.None);

        Assert.Equal(["It is ", "here."], deltas);
        Assert.Equal("It is here.", answer.Text);
        Assert.Equal(new PointingTarget("m1", 12, 34, "Save"), answer.PointingTarget);
        Assert.Equal("https://winly.example.test/chat", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task RequestCarriesTranscriptDisplaysAndHistoryWithoutAnyCredential()
    {
        string? sentBody = null;
        var handler = new StubHandler(request =>
        {
            sentBody = request.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("data: {\"done\":true,\"pointingTarget\":null}\n\n") };
        });
        var provider = new ProxyChatProvider(new HttpClient(handler), Endpoint);
        var display = new DisplayCapture("m1", [1, 2, 3], 10, 5, true, new MonitorGeometry(0, 0, 10, 5, 1));
        var history = new[] { new Exchange("earlier question", "earlier answer", null, DateTimeOffset.UtcNow) };

        await provider.Ask(new ChatRequest("now?", [display], history), _ => { }, CancellationToken.None);

        Assert.Contains("\"transcript\":\"now?\"", sentBody);
        Assert.Contains("\"imageBase64\":\"AQID\"", sentBody);
        Assert.Contains("\"isPrimary\":true", sentBody);
        Assert.Contains("{\"role\":\"user\",\"content\":\"earlier question\"},{\"role\":\"assistant\",\"content\":\"earlier answer\"}", sentBody);
        Assert.Null(handler.LastRequest!.Headers.Authorization);
        Assert.DoesNotContain("key", sentBody, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, "{\"error\":\"provider_unavailable\"}", "provider_unavailable")]
    [InlineData(HttpStatusCode.GatewayTimeout, "{\"error\":\"provider_timeout\"}", "provider_timeout")]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":\"invalid_request\"}", "invalid_request")]
    [InlineData(HttpStatusCode.InternalServerError, "<html>oops</html>", "provider_unavailable")]
    public async Task ErrorRepliesBecomeProviderFailures(HttpStatusCode status, string body, string expectedReason)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });
        var provider = new ProxyChatProvider(new HttpClient(handler), Endpoint);

        var failure = await Assert.ThrowsAsync<ProviderFailureException>(() => provider.Ask(new ChatRequest("q", [], []), _ => { }, CancellationToken.None));

        Assert.Equal(expectedReason, failure.Reason);
    }

    [Fact]
    public async Task MissingBaseAddressFailsBeforeAnyRequest()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not be called"));
        var provider = new ProxyChatProvider(new HttpClient(handler), new ProxyEndpointOptions(BaseAddress: null));

        await Assert.ThrowsAsync<ProxyNotConfiguredException>(() => provider.Ask(new ChatRequest("q", [], []), _ => { }, CancellationToken.None));
        Assert.Null(handler.LastRequest);
    }
}
