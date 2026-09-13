using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Winly.Providers;

namespace Winly.Providers.Tests;

public class HappyEyeballsConnectTests
{
    /// <summary>
    /// The bug this exists for: a reachable IPv4 address listed after unreachable IPv6 ones used to
    /// cost a full SYN timeout per IPv6 address — about 21 s each — before it was ever tried.
    /// </summary>
    [Fact]
    public async Task ReachesIPv4WithoutWaitingOutDeadIPv6()
    {
        using var listener = Listening();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        // 2001:db8::/32 is the documentation prefix: routed nowhere, so it either fails fast or hangs.
        IPAddress[] addresses = [IPAddress.Parse("2001:db8::1"), IPAddress.Parse("2001:db8::2"), IPAddress.Loopback];

        var started = Stopwatch.GetTimestamp();
        using var connected = await HappyEyeballsConnect.Race(addresses, port, CancellationToken.None);

        Assert.True(connected.CanWrite);
        Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5), "IPv4 should not wait on IPv6");
    }

    [Fact]
    public async Task ConnectsWhenOnlyOneFamilyIsListed()
    {
        using var listener = Listening();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var connected = await HappyEyeballsConnect.Race([IPAddress.Loopback], port, CancellationToken.None);

        Assert.True(connected.CanWrite);
    }

    [Fact]
    public async Task ReportsTheFailureWhenNothingAnswers()
    {
        // Port 1 on loopback: refused immediately rather than hanging.
        await Assert.ThrowsAsync<SocketException>(
            () => HappyEyeballsConnect.Race([IPAddress.Loopback], 1, CancellationToken.None));
    }

    private static TcpListener Listening()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return listener;
    }
}
