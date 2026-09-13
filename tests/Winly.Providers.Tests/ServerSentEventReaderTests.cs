using System.Text;
using Winly.Providers.Chat;

namespace Winly.Providers.Tests;

public class ServerSentEventReaderTests
{
    private const string Stream = """
        : comment line the reader must ignore
        event: message
        data: {"delta":"Hello"}

        data: {"delta":" world"}

        data: {"delta":"multi"}
        data: {"delta":"line"}

        data: {"done":true,"pointingTarget":null}

        """;

    [Fact]
    public async Task ParsesDeltasAndDoneEvent()
    {
        var events = new List<string>();
        await foreach (var data in ServerSentEventReader.ReadDataEvents(new MemoryStream(Encoding.UTF8.GetBytes(Stream)), CancellationToken.None))
        {
            events.Add(data);
        }

        await Verify(events);
    }

    [Fact]
    public async Task FinalEventWithoutTrailingBlankLineIsStillDelivered()
    {
        var events = new List<string>();
        await foreach (var data in ServerSentEventReader.ReadDataEvents(new MemoryStream("data: {\"delta\":\"end\"}"u8.ToArray()), CancellationToken.None))
        {
            events.Add(data);
        }

        Assert.Equal(["{\"delta\":\"end\"}"], events);
    }
}
