using BEngine.Platform;
using System.Net;

namespace BEngine.Networking;

/// <summary>Adapts the active host's sockets without creating native handles in this assembly.</summary>
public sealed class SocketTransportFactory : ISocketTransportFactory
{
    public static SocketTransportFactory Shared { get; } = new();
    private static IPlatformSocketTransportFactory Native =>
        PlatformRuntime.GetService<IPlatformSocketTransportFactory>() ??
        throw new PlatformNotSupportedException("The host has no socket transport.");
    public ITcpTransport CreateTcpTransport() => new TcpTransport(Native.CreateTcpTransport());
    public IUdpTransport CreateUdpTransport(IPEndPoint? localEndPoint = null) =>
        new UdpTransport(Native.CreateUdpTransport(localEndPoint));

    private sealed class TcpTransport(IPlatformTcpTransport native) : ITcpTransport
    {
        public bool IsConnected => native.IsConnected;
        public EndPoint? LocalEndPoint => native.LocalEndPoint;
        public EndPoint? RemoteEndPoint => native.RemoteEndPoint;
        public bool NoDelay { get => native.NoDelay; set => native.NoDelay = value; }
        public INetworkOperation Connect(string host, int port) => new PlatformNetworkOperation(native.Connect(host, port));
        public INetworkOperation Send(ReadOnlyMemory<byte> data) => new PlatformNetworkOperation(native.Send(data));
        public INetworkOperation<int> Receive(Memory<byte> buffer) => new PlatformNetworkOperation<int>(native.Receive(buffer));
        public void Close() => native.Close();
        public void Dispose() => native.Dispose();
    }

    private sealed class UdpTransport(IPlatformUdpTransport native) : IUdpTransport
    {
        public IPEndPoint? LocalEndPoint => native.LocalEndPoint;
        public IPEndPoint? RemoteEndPoint => native.RemoteEndPoint;
        public void Connect(string host, int port) => native.Connect(host, port);
        public INetworkOperation<int> Send(ReadOnlyMemory<byte> data, IPEndPoint? remoteEndPoint = null) =>
            new PlatformNetworkOperation<int>(native.Send(data, remoteEndPoint));
        public INetworkOperation<NetworkDatagram> Receive() =>
            new PlatformMappedNetworkOperation<PlatformNetworkDatagram, NetworkDatagram>(native.Receive(),
                static packet => new NetworkDatagram(packet.Data, packet.RemoteEndPoint));
        public void Close() => native.Close();
        public void Dispose() => native.Dispose();
    }
}