using BEngine.Instrumentation;
using System.Net;
using BEngine.DependencyInjection;
using BEngine.Diagnostics;
using System.Runtime.CompilerServices;
namespace BEngine.Platform.DotNet.Diagnostics;
internal static class RemoteTransportModule
{
    [ModuleInitializer] internal static void Register() => RuntimeServiceContributions.RegisterDefault<IRemoteDiagnosticsTransport>(new TcpRemoteDiagnosticsTransport());
}
public sealed class TcpRemoteDiagnosticsTransport : IRemoteDiagnosticsTransport
{
    public IRemoteDiagnosticsServer Start(string bindAddress, int port, string token)
    {
        if (!IPAddress.TryParse(bindAddress, out var address))
            throw new ArgumentException($"Invalid diagnostics bind address '{bindAddress}'.", nameof(bindAddress));
        return TcpRemoteDiagnosticsServer.Start(address, port, token);
    }

    public async Task<IRemoteDiagnosticsClient> ConnectAsync(string host, int port, string token,
        CancellationToken cancellationToken = default) =>
        await TcpRemoteDiagnosticsClient.ConnectAsync(host, port, token, cancellationToken).ConfigureAwait(false);
}
