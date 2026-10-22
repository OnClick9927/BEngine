using System.Net;

namespace BEngine.Networking;

public interface ITcpTransport : IDisposable
{
    bool IsConnected { get; }
    EndPoint? LocalEndPoint { get; }
    EndPoint? RemoteEndPoint { get; }
    bool NoDelay { get; set; }
    INetworkOperation Connect(string host, int port);
    INetworkOperation Send(ReadOnlyMemory<byte> data);
    INetworkOperation<int> Receive(Memory<byte> buffer);
    void Close();
}
