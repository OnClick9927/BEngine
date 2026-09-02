using System.Net;
using System.Net.Sockets;
using System.Text;
using BEngine.Networking;

namespace BEngine.ExampleTests.Networking;

internal static class TcpNetworkTests
{
    public static async Task RunAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var server = RunServerAsync(listener);

        await using var client = new TcpNetworkClient { Timeout = TimeSpan.FromSeconds(5) };
        await client.ConnectAsync(IPAddress.Loopback.ToString(), endpoint.Port).ConfigureAwait(false);
        TestAssert.Require(client.IsConnected, "TCP client did not enter the connected state.");

        await client.SendAsync(Encoding.UTF8.GetBytes("ping")).ConfigureAwait(false);
        var buffer = new byte[4];
        var count = await client.ReceiveAsync(buffer).ConfigureAwait(false);
        TestAssert.Equal("pong", Encoding.UTF8.GetString(buffer, 0, count),
            "TCP loopback response was incorrect.");
        TestAssert.Equal(NetworkRequestResult.Success, client.LastResult,
            "TCP operation status was not successful.");
        await server.ConfigureAwait(false);
        await client.CloseAsync().ConfigureAwait(false);
        TestAssert.Equal(NetworkClientState.Closed, client.State,
            "TCP close did not update the client state.");
    }

    private static async Task RunServerAsync(TcpListener listener)
    {
        using var socket = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
        var stream = socket.GetStream();
        var buffer = new byte[4];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(offset)).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException("TCP client disconnected before sending a request.");
            offset += count;
        }
        TestAssert.Equal("ping", Encoding.UTF8.GetString(buffer), "TCP loopback request was incorrect.");
        await stream.WriteAsync(Encoding.UTF8.GetBytes("pong")).ConfigureAwait(false);
    }
}
