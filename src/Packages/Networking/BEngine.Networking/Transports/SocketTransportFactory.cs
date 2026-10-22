using System.Net;
using System.Net.Sockets;

namespace BEngine.Networking;

public sealed class SocketTransportFactory : ISocketTransportFactory
{
    public static SocketTransportFactory Shared { get; } = new();
    public ITcpTransport CreateTcpTransport() => new TcpTransport();
    public IUdpTransport CreateUdpTransport(IPEndPoint? localEndPoint = null) => new UdpTransport(localEndPoint);

    private static IPAddress ResolveAddress(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? IPAddress.Loopback :
        IPAddress.TryParse(host, out var address) ? address :
        throw new PlatformNotSupportedException("The default socket adapter accepts IP addresses. Register a host transport for DNS.");

    private sealed class TcpTransport : ITcpTransport
    {
        private Socket? _socket;
        private bool _noDelay;
        public bool IsConnected => _socket?.Connected == true;
        public EndPoint? LocalEndPoint => _socket?.LocalEndPoint;
        public EndPoint? RemoteEndPoint => _socket?.RemoteEndPoint;
        public bool NoDelay
        {
            get => _socket?.NoDelay ?? _noDelay;
            set { _noDelay = value; if (_socket is not null) _socket.NoDelay = value; }
        }
        public INetworkOperation Connect(string host, int port)
        {
            if (_socket is not null) throw new InvalidOperationException("The transport is already connected.");
            var address = ResolveAddress(host);
            _socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                { Blocking = false, NoDelay = _noDelay };
            return new SocketConnectOperation(_socket, new IPEndPoint(address, port));
        }
        public INetworkOperation Send(ReadOnlyMemory<byte> data) => new SocketSendOperation(_socket!, data);
        public INetworkOperation<int> Receive(Memory<byte> buffer) => new SocketReceiveOperation(_socket!, buffer);
        public void Close() { _socket?.Dispose(); _socket = null; }
        public void Dispose() => Close();
    }

    private sealed class UdpTransport : IUdpTransport
    {
        private readonly Socket _socket;
        internal UdpTransport(IPEndPoint? local)
        {
            _socket = new Socket(local?.AddressFamily ?? AddressFamily.InterNetwork,
                SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
            try { if (local is not null) _socket.Bind(local); }
            catch { _socket.Dispose(); throw; }
        }
        public IPEndPoint? LocalEndPoint => _socket.LocalEndPoint as IPEndPoint;
        public IPEndPoint? RemoteEndPoint => _socket.RemoteEndPoint as IPEndPoint;
        public void Connect(string host, int port) => _socket.Connect(new IPEndPoint(ResolveAddress(host), port));
        public INetworkOperation<int> Send(ReadOnlyMemory<byte> data, IPEndPoint? remoteEndPoint = null) =>
            remoteEndPoint is null ? new SocketDatagramSendOperation(_socket, data) :
                new SocketSendToOperation(_socket, data, remoteEndPoint);
        public INetworkOperation<NetworkDatagram> Receive() => new SocketReceiveFromOperation(_socket);
        public void Close() => _socket.Close();
        public void Dispose() => _socket.Dispose();
    }
}
