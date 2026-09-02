using System.Net;

namespace BEngine.Networking;

public sealed record NetworkDatagram(byte[] Data, IPEndPoint RemoteEndPoint);
