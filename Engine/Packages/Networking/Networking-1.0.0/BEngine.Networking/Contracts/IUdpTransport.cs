using System.Net;

namespace BEngine.Networking;

public interface IUdpTransport : IDisposable
{
    IPEndPoint? LocalEndPoint { get; }
    IPEndPoint? RemoteEndPoint { get; }
    void Connect(string host, int port);
    INetworkOperation<int> Send(ReadOnlyMemory<byte> data, IPEndPoint? remoteEndPoint = null);
    INetworkOperation<NetworkDatagram> Receive();
    void Close();
}
