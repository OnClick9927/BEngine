using System.Net;
using System.Net.Sockets;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Diagnostics;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;
using static DiagnosticsProtocolProbe;

internal static class FactoryProbe
{
    internal static List<string> Run()
    {
        var checks = new List<string>();
        void Check(bool value, string name)
        { if (!value) throw new Exception(name); checks.Add(name); }
        var factory = RuntimeServices.GetRequiredService<IPlatformTcpStreamFactory>();
        Check(factory.GetType().Assembly == typeof(DotNetPlatformServices).Assembly,
            "The default stream factory is owned by the native host");
        Check(Fails<ArgumentOutOfRangeException>(() => factory.Listen(new(IPAddress.Loopback, 0), 0)),
            "Invalid listen backlogs are rejected");
        Check(Fails<ArgumentOutOfRangeException>(() => Wait(factory.ConnectAsync("localhost", 65536))),
            "Invalid connect ports are rejected");
        using (var listener = factory.Listen(new(IPAddress.Loopback, 0)))
        {
            using var cancellation = new CancellationTokenSource();
            var canceled = listener.AcceptAsync(cancellation.Token); cancellation.Cancel();
            Check(Fails<OperationCanceledException>(() => Wait(canceled)), "A pending accept is cancelable");
            using var alreadyCanceled = new CancellationTokenSource(); alreadyCanceled.Cancel();
            Check(Fails<OperationCanceledException>(() => Wait(factory.ConnectAsync("localhost", listener.LocalEndPoint.Port, cancellationToken: alreadyCanceled.Token))),
                "A canceled connect does not publish a connection");
        }
        foreach (var address in Socket.OSSupportsIPv6 ? new[] { IPAddress.Loopback, IPAddress.IPv6Loopback } : new[] { IPAddress.Loopback })
        {
            using var listener = factory.Listen(new(address, 0));
            var accept = listener.AcceptAsync();
            using var outgoing = Wait(factory.ConnectAsync(address.ToString(), listener.LocalEndPoint.Port));
            using var incoming = Wait(accept);
            Check(outgoing.RemoteEndPoint is IPEndPoint remote && remote.Port == listener.LocalEndPoint.Port,
                address + " preserves connection endpoint metadata");
            Check(ReferenceEquals(outgoing.Stream, outgoing.Stream), address + " returns one borrowed stream per connection");
            listener.Dispose();
            var data = new byte[] { 1, 7, 0, 255, 42 }; var received = new byte[data.Length];
            Wait(outgoing.Stream.WriteAsync(data).AsTask());
            Wait(incoming.Stream.ReadExactlyAsync(received).AsTask());
            Check(data.AsSpan().SequenceEqual(received), address + " accepted streams remain usable after listener disposal");
            outgoing.Dispose(); outgoing.Dispose();
            Check(Wait(incoming.Stream.ReadAsync(received).AsTask()) == 0,
                address + " connection disposal closes the borrowed stream and reaches peer EOF");
        }
        var tracking = new TrackingFactory(factory);
        using (var provider = new ServiceCollection().AddBEngineDotNetPlatform()
                   .AddSingleton<IPlatformTcpStreamFactory>(tracking).BuildServiceProvider())
        {
            PlatformRuntime.Initialize(provider);
            try
            {
                var protocol = DiagnosticsProtocolProbe.Run(); checks.AddRange(protocol);
                Check(tracking.Listens == 2 && tracking.Connects == 3 && tracking.NoDelay,
                    "Both diagnostic endpoints use the injected host capability and client NoDelay");
                Check(tracking.LiveConnections == 0 && tracking.LiveListeners == 0,
                    "Successful, rejected and restarted sessions release all native leases");
            }
            finally { DotNetPlatformServices.Initialize(); }
        }
        RuntimeServiceContributions.UnregisterAssembly(typeof(RemoteDiagnosticsProtocol).Assembly);
        Check(RuntimeServiceContributions.GetDefault<IRemoteDiagnosticsTransport>() is null,
            "Diagnostic protocol registration retires with its package");
        Check(PlatformRuntime.GetService<IPlatformTcpStreamFactory>() is not null,
            "Retiring diagnostic protocol leaves the native host capability available");
        return checks;
    }

    private sealed class TrackingFactory(IPlatformTcpStreamFactory inner) : IPlatformTcpStreamFactory
    {
        public int Listens, Connects, LiveConnections, LiveListeners;
        public bool NoDelay = true;
        public IPlatformTcpStreamListener Listen(IPEndPoint endpoint, int backlog = 16)
        { Listens++; var listener = inner.Listen(endpoint, backlog); LiveListeners++; return new Listener(listener, this); }
        public async Task<IPlatformTcpStreamConnection> ConnectAsync(string host, int port, bool noDelay = true, CancellationToken cancellationToken = default)
        { Connects++; NoDelay &= noDelay; return Wrap(await inner.ConnectAsync(host, port, noDelay, cancellationToken)); }
        private IPlatformTcpStreamConnection Wrap(IPlatformTcpStreamConnection connection)
        { Interlocked.Increment(ref LiveConnections); return new Connection(connection, this); }
        private sealed class Listener(IPlatformTcpStreamListener inner, TrackingFactory owner) : IPlatformTcpStreamListener
        {
            private int _disposed;
            public IPEndPoint LocalEndPoint => inner.LocalEndPoint;
            public async Task<IPlatformTcpStreamConnection> AcceptAsync(CancellationToken cancellationToken = default) =>
                owner.Wrap(await inner.AcceptAsync(cancellationToken));
            public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) { inner.Dispose(); owner.LiveListeners--; } }
        }
        private sealed class Connection(IPlatformTcpStreamConnection inner, TrackingFactory owner) : IPlatformTcpStreamConnection
        {
            private int _disposed;
            public EndPoint? LocalEndPoint => inner.LocalEndPoint;
            public EndPoint? RemoteEndPoint => inner.RemoteEndPoint;
            public Stream Stream => inner.Stream;
            public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) { inner.Dispose(); Interlocked.Decrement(ref owner.LiveConnections); } }
        }
    }
}
