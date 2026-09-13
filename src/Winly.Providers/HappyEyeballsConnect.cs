using System.Net;
using System.Net.Sockets;

namespace Winly.Providers;

/// <summary>
/// Races IPv6 against IPv4 when opening a connection, the way curl and every browser already do
/// (RFC 8305), instead of walking the resolved addresses strictly in order.
///
/// .NET does not do this on its own, and the difference is not academic: a host that publishes AAAA
/// records, reached from a network with no working IPv6 path, costs the full Windows SYN timeout —
/// about 21 seconds — for *each* AAAA address before the IPv4 one is ever tried. The backend
/// publishes two, so a cold connect took 40 seconds and sometimes more. Because the connection is
/// then pooled, only the first request after launch or after an idle gap paid it, which is exactly
/// what "Winly buffers the first time I use it" was.
/// </summary>
public static class HappyEyeballsConnect
{
    /// <summary>How long IPv6 gets on its own before IPv4 starts alongside it.</summary>
    private static readonly TimeSpan AddressFamilyHeadStart = TimeSpan.FromMilliseconds(250);

    /// <summary>Assign to <see cref="SocketsHttpHandler.ConnectCallback"/>.</summary>
    public static async ValueTask<Stream> Connect(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        return await Race(addresses, context.DnsEndPoint.Port, cancellationToken);
    }

    /// <summary>
    /// Connects to the first address that answers, preferring IPv6 by a head start.
    ///
    /// ponytail: races the two families as two groups rather than interleaving every address the way
    /// RFC 8305 describes. Within a family the addresses are still tried in order, so a host whose
    /// *first* IPv4 address is blackholed still waits it out. Interleave per address if that ever
    /// shows up in the logs.
    /// </summary>
    public static async Task<Stream> Race(IReadOnlyList<IPAddress> addresses, int port, CancellationToken cancellationToken)
    {
        var sixes = addresses.Where(address => address.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
        var fours = addresses.Where(address => address.AddressFamily == AddressFamily.InterNetwork).ToArray();
        if (sixes.Length == 0 || fours.Length == 0)
        {
            // Nothing to race: one family is all there is.
            return await Attempt(sixes.Length == 0 ? fours : sixes, port, TimeSpan.Zero, cancellationToken);
        }

        using var race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var running = new List<Task<Stream>>
        {
            Attempt(sixes, port, TimeSpan.Zero, race.Token),
            Attempt(fours, port, AddressFamilyHeadStart, race.Token),
        };

        Exception? lastFailure = null;
        while (running.Count > 0)
        {
            var finished = await Task.WhenAny(running);
            running.Remove(finished);
            if (finished.IsCompletedSuccessfully)
            {
                // The loser is cancelled, but may still have connected in the meantime; dropping its
                // socket on the floor would leave a half-open connection behind.
                race.Cancel();
                foreach (var loser in running)
                {
                    _ = loser.ContinueWith(static task => task.Result.Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion);
                }

                return finished.Result;
            }

            lastFailure = finished.Exception?.GetBaseException() ?? lastFailure;
        }

        throw lastFailure ?? new SocketException((int)SocketError.HostUnreachable);
    }

    private static async Task<Stream> Attempt(IPAddress[] addresses, int port, TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }

        var socket = new Socket(addresses[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
