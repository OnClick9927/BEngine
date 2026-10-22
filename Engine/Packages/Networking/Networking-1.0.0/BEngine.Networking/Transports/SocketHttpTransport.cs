using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BEngine.Networking;

/// <summary>
/// Minimal frame-polled HTTP/1.1 transport for local development servers.
/// TLS and platform-managed networking are supplied by the active player host.
/// </summary>
public sealed class SocketHttpTransport : IHttpTransport
{
    public static SocketHttpTransport Shared { get; } = new();

    public IHttpTransportOperation Send(
        HttpTransportRequest request,
        IHttpDownloadHandler downloadHandler)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(downloadHandler);
        if (request.Uri.Scheme != Uri.UriSchemeHttp)
            throw new PlatformNotSupportedException(
                "The Runtime socket transport supports HTTP only. " +
                "The active player host must register an HTTPS transport.");
        return new SocketHttpOperation(request, downloadHandler);
    }

    private sealed class SocketHttpOperation : IHttpTransportOperation
    {
        private const int MaximumHeaderBytes = 64 * 1024;
        private readonly HttpTransportRequest _request;
        private readonly IHttpDownloadHandler _downloadHandler;
        private readonly Socket _socket;
        private readonly IPEndPoint _remoteEndPoint;
        private readonly byte[] _requestBytes;
        private readonly byte[] _receiveBuffer = new byte[64 * 1024];
        private readonly MemoryStream _headerBuffer = new();
        private readonly MemoryStream _chunkBuffer = new();
        private int _requestOffset;
        private int _chunkOffset;
        private OperationPhase _phase;
        private bool _disposed;
        private bool _chunked;

        public SocketHttpOperation(
            HttpTransportRequest request,
            IHttpDownloadHandler downloadHandler)
        {
            _request = request;
            _downloadHandler = downloadHandler;
            var address = ResolveAddress(request.Uri.Host);
            var port = request.Uri.IsDefaultPort ? 80 : request.Uri.Port;
            _remoteEndPoint = new IPEndPoint(address, port);
            _socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                Blocking = false,
                NoDelay = true
            };
            _requestBytes = BuildRequest(request);
        }

        public NetworkOperationStatus Status { get; private set; } = NetworkOperationStatus.Running;
        public Exception? Exception { get; private set; }
        public HttpTransportResponse? Response { get; private set; }
        public long DownloadedBytes { get; private set; }
        public long? ContentLength { get; private set; }

        public void Poll()
        {
            if (Status != NetworkOperationStatus.Running) return;
            try
            {
                switch (_phase)
                {
                    case OperationPhase.Connect:
                    case OperationPhase.Connecting:
                        PollConnect();
                        break;
                    case OperationPhase.Send:
                        PollSend();
                        break;
                    case OperationPhase.Receive:
                        PollReceive();
                        break;
                }
            }
            catch (SocketException exception) when (IsPending(exception.SocketErrorCode))
            {
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        public void Abort()
        {
            if (Status != NetworkOperationStatus.Running) return;
            Fail(new IOException("The HTTP operation was aborted."));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _socket.Dispose();
            _headerBuffer.Dispose();
            _chunkBuffer.Dispose();
        }

        private void PollConnect()
        {
            if (_phase == OperationPhase.Connect)
            {
                try
                {
                    _socket.Connect(_remoteEndPoint);
                    _phase = OperationPhase.Send;
                    return;
                }
                catch (SocketException exception) when (IsPending(exception.SocketErrorCode))
                {
                    _phase = OperationPhase.Connecting;
                    return;
                }
            }

            if (!_socket.Poll(0, SelectMode.SelectWrite) &&
                !_socket.Poll(0, SelectMode.SelectError)) return;
            var error = (SocketError)(int)_socket.GetSocketOption(
                SocketOptionLevel.Socket, SocketOptionName.Error)!;
            if (error != SocketError.Success) throw new SocketException((int)error);
            _phase = OperationPhase.Send;
        }

        private void PollSend()
        {
            if (_requestOffset == _requestBytes.Length)
            {
                _phase = OperationPhase.Receive;
                return;
            }
            if (!_socket.Poll(0, SelectMode.SelectWrite)) return;
            var count = _socket.Send(
                _requestBytes.AsSpan(_requestOffset), SocketFlags.None);
            if (count == 0)
                throw new EndOfStreamException("The HTTP connection closed while sending the request.");
            _requestOffset += count;
            if (_requestOffset == _requestBytes.Length) _phase = OperationPhase.Receive;
        }

        private void PollReceive()
        {
            if (!_socket.Poll(0, SelectMode.SelectRead)) return;
            var count = _socket.Receive(_receiveBuffer, SocketFlags.None);
            if (count == 0)
            {
                if (Response is null)
                    throw new InvalidDataException("The HTTP connection closed before response headers arrived.");
                if (_chunked)
                    throw new InvalidDataException("The chunked HTTP response ended before its final chunk.");
                if (ContentLength is { } expected && DownloadedBytes != expected)
                    throw new InvalidDataException(
                        $"The HTTP response declared {expected} bytes but delivered {DownloadedBytes}.");
                Complete();
                return;
            }
            ProcessReceived(_receiveBuffer.AsSpan(0, count));
        }

        private void ProcessReceived(ReadOnlySpan<byte> bytes)
        {
            if (Response is not null)
            {
                ProcessBody(bytes);
                return;
            }

            _headerBuffer.Write(bytes);
            if (_headerBuffer.Length > MaximumHeaderBytes)
                throw new InvalidDataException(
                    $"The HTTP response headers exceed {MaximumHeaderBytes} bytes.");
            var buffered = _headerBuffer.GetBuffer().AsSpan(0, checked((int)_headerBuffer.Length));
            var delimiter = FindHeaderDelimiter(buffered);
            if (delimiter < 0) return;

            ParseResponseHeaders(buffered[..delimiter]);
            var bodyOffset = delimiter + 4;
            if (bodyOffset < buffered.Length) ProcessBody(buffered[bodyOffset..]);
            if (HasNoBody()) Complete();
        }

        private void ParseResponseHeaders(ReadOnlySpan<byte> bytes)
        {
            var text = Encoding.ASCII.GetString(bytes);
            var lines = text.Split("\r\n", StringSplitOptions.None);
            var statusParts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (statusParts.Length < 2 ||
                !int.TryParse(statusParts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                    out var statusCode) || statusCode is < 100 or > 999)
                throw new InvalidDataException("The HTTP response status line is invalid.");

            var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            for (var index = 1; index < lines.Length; index++)
            {
                var separator = lines[index].IndexOf(':');
                if (separator <= 0)
                    throw new InvalidDataException("The HTTP response contains an invalid header.");
                var name = lines[index][..separator].Trim();
                var value = lines[index][(separator + 1)..].Trim();
                if (!values.TryGetValue(name, out var entries))
                    values[name] = entries = [];
                entries.Add(value);
            }
            var headers = values.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);
            if (headers.TryGetValue("Content-Length", out var lengths))
            {
                if (lengths.Length != 1 ||
                    !long.TryParse(lengths[0], NumberStyles.None, CultureInfo.InvariantCulture,
                        out var length) || length < 0)
                    throw new InvalidDataException("The HTTP Content-Length header is invalid.");
                ContentLength = length;
            }
            if (headers.TryGetValue("Transfer-Encoding", out var encodings))
                _chunked = encodings.Any(value => value.Split(',').Any(
                    item => item.Trim().Equals("chunked", StringComparison.OrdinalIgnoreCase)));
            if (_chunked) ContentLength = null;

            Response = new HttpTransportResponse(
                (HttpStatusCode)statusCode,
                statusParts.Length == 3 ? statusParts[2] : null,
                headers);
            _downloadHandler.ReceiveResponse(Response);
        }

        private void ProcessBody(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty || Status != NetworkOperationStatus.Running) return;
            if (_chunked)
            {
                _chunkBuffer.Position = _chunkBuffer.Length;
                _chunkBuffer.Write(bytes);
                ParseChunks();
                return;
            }

            var count = bytes.Length;
            if (ContentLength is { } expected)
            {
                var remaining = expected - DownloadedBytes;
                if (remaining <= 0) return;
                count = (int)Math.Min(count, remaining);
            }
            Deliver(bytes[..count]);
            if (ContentLength is { } length && DownloadedBytes == length) Complete();
        }

        private void ParseChunks()
        {
            var bytes = _chunkBuffer.GetBuffer().AsSpan(0, checked((int)_chunkBuffer.Length));
            while (true)
            {
                var lineEnd = FindCrlf(bytes, _chunkOffset);
                if (lineEnd < 0) return;
                var sizeText = Encoding.ASCII.GetString(bytes[_chunkOffset..lineEnd]);
                var extension = sizeText.IndexOf(';');
                if (extension >= 0) sizeText = sizeText[..extension];
                if (!int.TryParse(sizeText.Trim(), NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture, out var size) || size < 0)
                    throw new InvalidDataException("The HTTP chunk size is invalid.");
                var dataOffset = lineEnd + 2;
                if (size == 0)
                {
                    if (bytes.Length < dataOffset + 2) return;
                    Complete();
                    return;
                }
                if (bytes.Length < dataOffset + size + 2) return;
                if (bytes[dataOffset + size] != (byte)'\r' ||
                    bytes[dataOffset + size + 1] != (byte)'\n')
                    throw new InvalidDataException("The HTTP chunk terminator is invalid.");
                Deliver(bytes.Slice(dataOffset, size));
                _chunkOffset = dataOffset + size + 2;
            }
        }

        private void Deliver(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty) return;
            var copy = bytes.ToArray();
            _downloadHandler.ReceiveData(copy);
            DownloadedBytes += copy.Length;
        }

        private bool HasNoBody() =>
            _request.Method == HttpRequestMethod.Head ||
            Response?.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotModified ||
            ContentLength == 0;

        private void Complete()
        {
            if (Status != NetworkOperationStatus.Running) return;
            Status = NetworkOperationStatus.Succeeded;
            _socket.Dispose();
        }

        private void Fail(Exception exception)
        {
            if (Status != NetworkOperationStatus.Running) return;
            Exception = exception;
            Status = NetworkOperationStatus.Failed;
            _socket.Dispose();
        }

        private static IPAddress ResolveAddress(string host)
        {
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return IPAddress.Loopback;
            if (IPAddress.TryParse(host, out var address)) return address;
            throw new PlatformNotSupportedException(
                "The Runtime HTTP transport accepts IP addresses and localhost only. " +
                "Register a platform HTTP transport for host-name resolution.");
        }

        private static byte[] BuildRequest(HttpTransportRequest request)
        {
            var builder = new StringBuilder();
            builder.Append(ToMethodText(request.Method)).Append(' ')
                .Append(string.IsNullOrEmpty(request.Uri.PathAndQuery) ? "/" : request.Uri.PathAndQuery)
                .Append(" HTTP/1.1\r\nHost: ").Append(request.Uri.IsDefaultPort
                    ? request.Uri.Host
                    : request.Uri.Authority)
                .Append("\r\nConnection: close\r\nAccept-Encoding: identity\r\n");
            foreach (var (name, value) in request.Headers)
            {
                ValidateHeader(name, value);
                if (name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                builder.Append(name).Append(": ").Append(value).Append("\r\n");
            }
            var body = request.UploadData ?? [];
            if (body.Length > 0)
            {
                builder.Append("Content-Length: ").Append(body.Length)
                    .Append("\r\nContent-Type: ")
                    .Append(string.IsNullOrWhiteSpace(request.UploadContentType)
                        ? "application/octet-stream"
                        : request.UploadContentType)
                    .Append("\r\n");
            }
            builder.Append("\r\n");
            var header = Encoding.ASCII.GetBytes(builder.ToString());
            if (body.Length == 0) return header;
            var result = new byte[header.Length + body.Length];
            header.CopyTo(result, 0);
            body.CopyTo(result, header.Length);
            return result;
        }

        private static string ToMethodText(HttpRequestMethod method) => method switch
        {
            HttpRequestMethod.Get => "GET",
            HttpRequestMethod.Post => "POST",
            HttpRequestMethod.Put => "PUT",
            HttpRequestMethod.Delete => "DELETE",
            HttpRequestMethod.Patch => "PATCH",
            HttpRequestMethod.Head => "HEAD",
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };

        private static void ValidateHeader(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['\r', '\n', ':']) >= 0)
                throw new ArgumentException("An HTTP header name is invalid.", nameof(name));
            if (value.IndexOfAny(['\r', '\n']) >= 0)
                throw new ArgumentException("An HTTP header value is invalid.", nameof(value));
        }

        private static int FindHeaderDelimiter(ReadOnlySpan<byte> bytes)
        {
            for (var index = 0; index <= bytes.Length - 4; index++)
                if (bytes[index] == (byte)'\r' && bytes[index + 1] == (byte)'\n' &&
                    bytes[index + 2] == (byte)'\r' && bytes[index + 3] == (byte)'\n')
                    return index;
            return -1;
        }

        private static int FindCrlf(ReadOnlySpan<byte> bytes, int start)
        {
            for (var index = start; index <= bytes.Length - 2; index++)
                if (bytes[index] == (byte)'\r' && bytes[index + 1] == (byte)'\n')
                    return index;
            return -1;
        }

        private static bool IsPending(SocketError error) =>
            error is SocketError.WouldBlock or SocketError.IOPending or
                SocketError.InProgress or SocketError.AlreadyInProgress or
                SocketError.NoBufferSpaceAvailable;

        private enum OperationPhase
        {
            Connect,
            Connecting,
            Send,
            Receive
        }
    }
}
