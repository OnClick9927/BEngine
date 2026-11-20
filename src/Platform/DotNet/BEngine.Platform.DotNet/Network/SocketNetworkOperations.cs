using System.Net;
using System.Net.Sockets;

namespace BEngine.Platform.DotNet;

internal abstract class SocketNetworkOperation<TResult> : IPlatformNetworkOperation<TResult>
{
    private TResult? _result;

    public PlatformNetworkOperationStatus Status { get; private set; } = PlatformNetworkOperationStatus.Running;
    public Exception? Exception { get; private set; }
    public TResult Result => Status == PlatformNetworkOperationStatus.Succeeded
        ? _result!
        : throw new InvalidOperationException("The socket operation has not succeeded.");

    public void Poll()
    {
        if (Status != PlatformNetworkOperationStatus.Running) return;
        try
        {
            PollCore();
        }
        catch (SocketException exception) when (IsPending(exception.SocketErrorCode))
        {
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    public virtual void Abort()
    {
        if (Status == PlatformNetworkOperationStatus.Running)
            Fail(new IOException("The socket operation was aborted."));
    }

    public virtual void Dispose()
    {
    }

    protected abstract void PollCore();

    protected void Complete(TResult result)
    {
        if (Status != PlatformNetworkOperationStatus.Running) return;
        _result = result;
        Status = PlatformNetworkOperationStatus.Succeeded;
    }

    protected void Fail(Exception exception)
    {
        if (Status != PlatformNetworkOperationStatus.Running) return;
        Exception = exception;
        Status = PlatformNetworkOperationStatus.Failed;
    }

    protected static bool IsPending(SocketError error) =>
        error is SocketError.WouldBlock or SocketError.IOPending or
            SocketError.InProgress or SocketError.AlreadyInProgress or
            SocketError.NoBufferSpaceAvailable;
}

internal sealed class SocketConnectOperation(
    Socket socket,
    EndPoint remoteEndPoint) : SocketNetworkOperation<bool>
{
    private bool _started;

    protected override void PollCore()
    {
        if (!_started)
        {
            _started = true;
            try
            {
                socket.Connect(remoteEndPoint);
                Complete(true);
                return;
            }
            catch (SocketException exception) when (IsPending(exception.SocketErrorCode))
            {
            }
        }

        if (!socket.Poll(0, SelectMode.SelectWrite) &&
            !socket.Poll(0, SelectMode.SelectError)) return;

        var error = (SocketError)(int)socket.GetSocketOption(
            SocketOptionLevel.Socket, SocketOptionName.Error)!;
        if (error == SocketError.Success) Complete(true);
        else Fail(new SocketException((int)error));
    }
}

internal sealed class SocketSendOperation(
    Socket socket,
    ReadOnlyMemory<byte> data) : SocketNetworkOperation<int>
{
    private int _offset;

    protected override void PollCore()
    {
        if (_offset == data.Length)
        {
            Complete(_offset);
            return;
        }
        if (!socket.Poll(0, SelectMode.SelectWrite)) return;
        var count = socket.Send(data.Span[_offset..], SocketFlags.None);
        if (count == 0) throw new EndOfStreamException("The socket closed while sending data.");
        _offset += count;
        if (_offset == data.Length) Complete(_offset);
    }
}

internal sealed class SocketSendToOperation(
    Socket socket,
    ReadOnlyMemory<byte> data,
    EndPoint remoteEndPoint) : SocketNetworkOperation<int>
{
    protected override void PollCore()
    {
        if (!socket.Poll(0, SelectMode.SelectWrite)) return;
        var count = socket.SendTo(data.Span, SocketFlags.None, remoteEndPoint);
        if (count != data.Length)
            throw new IOException($"Only {count} of {data.Length} UDP bytes were sent.");
        Complete(count);
    }
}

internal sealed class SocketDatagramSendOperation(
    Socket socket,
    ReadOnlyMemory<byte> data) : SocketNetworkOperation<int>
{
    protected override void PollCore()
    {
        if (!socket.Poll(0, SelectMode.SelectWrite)) return;
        var count = socket.Send(data.Span, SocketFlags.None);
        if (count != data.Length)
            throw new IOException($"Only {count} of {data.Length} UDP bytes were sent.");
        Complete(count);
    }
}

internal sealed class SocketReceiveOperation(
    Socket socket,
    Memory<byte> buffer) : SocketNetworkOperation<int>
{
    protected override void PollCore()
    {
        if (!socket.Poll(0, SelectMode.SelectRead)) return;
        Complete(socket.Receive(buffer.Span, SocketFlags.None));
    }
}

internal sealed class SocketReceiveFromOperation : SocketNetworkOperation<PlatformNetworkDatagram>
{
    private readonly Socket _socket;
    private readonly byte[] _buffer = new byte[ushort.MaxValue];

    public SocketReceiveFromOperation(Socket socket) => _socket = socket;

    protected override void PollCore()
    {
        if (!_socket.Poll(0, SelectMode.SelectRead)) return;
        EndPoint remote = _socket.AddressFamily == AddressFamily.InterNetworkV6
            ? new IPEndPoint(IPAddress.IPv6Any, 0)
            : new IPEndPoint(IPAddress.Any, 0);
        var count = _socket.ReceiveFrom(_buffer, 0, _buffer.Length, SocketFlags.None, ref remote);
        if (remote is not IPEndPoint endPoint)
            throw new InvalidDataException("The UDP sender endpoint is not an IP endpoint.");
        Complete(new PlatformNetworkDatagram(_buffer.AsSpan(0, count).ToArray(), endPoint));
    }
}
