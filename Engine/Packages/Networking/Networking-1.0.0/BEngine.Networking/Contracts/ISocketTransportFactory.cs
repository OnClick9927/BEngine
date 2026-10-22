using System.Net;

namespace BEngine.Networking;

public interface ISocketTransportFactory
{
    ITcpTransport CreateTcpTransport();
    IUdpTransport CreateUdpTransport(IPEndPoint? localEndPoint = null);
}
