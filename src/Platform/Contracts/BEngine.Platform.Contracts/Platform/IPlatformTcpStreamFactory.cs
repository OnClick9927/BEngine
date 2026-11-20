using System.Net;

namespace BEngine.Platform;

/// <summary>Host TCP streams for consumers that already own a stream protocol.
/// Unlike the polling socket API, pending I/O completes asynchronously and never calls engine code.</summary>
public interface IPlatformTcpStreamFactory
{
    IPlatformTcpStreamListener Listen(IPEndPoint endPoint, int backlog = 16);
    Task<IPlatformTcpStreamConnection> ConnectAsync(string host, int port, bool noDelay = true,
        CancellationToken cancellationToken = default);
}

/// <summary>Owns the listening socket. Disposing it does not close previously accepted connections.</summary>
public interface IPlatformTcpStreamListener : IDisposable
{
    IPEndPoint LocalEndPoint { get; }
    Task<IPlatformTcpStreamConnection> AcceptAsync(CancellationToken cancellationToken = default);
}

/// <summary>Owns the connection and its stream. The stream is borrowed until the connection is disposed.</summary>
public interface IPlatformTcpStreamConnection : IDisposable
{
    EndPoint? LocalEndPoint { get; }
    EndPoint? RemoteEndPoint { get; }
    Stream Stream { get; }
}
