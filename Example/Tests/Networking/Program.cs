namespace BEngine.ExampleTests.Networking;

internal static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            await TcpNetworkTests.RunAsync().ConfigureAwait(false);
            await UdpNetworkTests.RunAsync().ConfigureAwait(false);
            await HttpNetworkTests.RunAsync().ConfigureAwait(false);
            await WebSocketNetworkTests.RunAsync().ConfigureAwait(false);
            Console.WriteLine(
                "NETWORKING_OK|tcp-loopback,udp-loopback,http-handler,http-protocol-error,http-cancellation," +
                "websocket-state,websocket-send,websocket-fragments,websocket-close,timeout,dispose");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"NETWORKING_FAILED|{exception}");
            return 1;
        }
    }
}
