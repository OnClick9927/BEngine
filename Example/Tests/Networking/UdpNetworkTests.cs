using System.Net;
using System.Text;
using BEngine.Networking;

namespace BEngine.ExampleTests.Networking;

internal static class UdpNetworkTests
{
    public static async Task RunAsync()
    {
        await using var receiver = new UdpNetworkClient(new IPEndPoint(IPAddress.Loopback, 0))
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        await using var sender = new UdpNetworkClient(new IPEndPoint(IPAddress.Loopback, 0));
        var receiverEndpoint = receiver.LocalEndPoint ??
                               throw new InvalidOperationException("UDP receiver was not bound.");

        var payload = Encoding.UTF8.GetBytes("datagram");
        var sent = await sender.SendAsync(payload, receiverEndpoint).ConfigureAwait(false);
        var received = await receiver.ReceiveAsync().ConfigureAwait(false);
        TestAssert.Equal(payload.Length, sent, "UDP sent byte count was incorrect.");
        TestAssert.Equal("datagram", Encoding.UTF8.GetString(received.Data),
            "UDP loopback payload was incorrect.");
        TestAssert.Equal(sender.LocalEndPoint, received.RemoteEndPoint,
            "UDP loopback sender endpoint was not retained.");

        receiver.Timeout = TimeSpan.FromMilliseconds(50);
        await TestAssert.ThrowsAsync<TimeoutException>(
            () => receiver.ReceiveAsync().AsTask(),
            "UDP receive timeout was not reported.").ConfigureAwait(false);
        TestAssert.Equal(NetworkRequestResult.TimedOut, receiver.LastResult,
            "UDP timeout did not update the error status.");
    }
}
