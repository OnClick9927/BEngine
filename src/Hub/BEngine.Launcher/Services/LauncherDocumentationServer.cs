using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BEngine.Launcher;

// A Hub-owned, loopback-only document origin. Catalogs are discovered per request,
// so installing/removing packages never requires regenerating a documentation site.
internal sealed class LauncherDocumentationServer : IDisposable
{
    private const int MaximumHeaderBytes = 16 * 1024;
    private readonly LauncherEngineCatalog _engines;
    private readonly LauncherPackageCatalog _packages;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _connections = new(8);
    private readonly ConcurrentDictionary<int, TcpClient> _clients = new();
    private readonly string _prefix = "/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant() + "/";
    private readonly Uri _origin;
    private readonly Task _acceptLoop;
    private int _clientId;
    private int _disposed;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, string> AssetResources = typeof(LauncherDocumentationServer)
        .Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith("BEngine.Launcher.DocumentationAssets/", StringComparison.Ordinal))
        .ToDictionary(name => name["BEngine.Launcher.DocumentationAssets/".Length..].Replace('\\', '/'),
            name => name, StringComparer.Ordinal);

    internal LauncherDocumentationServer(LauncherEngineCatalog engines, LauncherPackageCatalog packages)
    {
        _engines = engines;
        _packages = packages;
        _listener.Start(16);
        _origin = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
        _acceptLoop = AcceptAsync();
    }

    internal Uri Open(LauncherEngineInfo engine, LauncherPackageInfo? package = null,
        string language = "zh-CN", string theme = "dark")
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var query = $"?engine={Uri.EscapeDataString(engine.Version)}" +
                    $"&lang={(language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en")}" +
                    $"&theme={(theme.Equals("light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark")}" +
                    (package is null ? string.Empty : "&package=" + Uri.EscapeDataString(package.Id));
        return new Uri(_origin, _prefix + query);
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                await _connections.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_lifetime.Token).ConfigureAwait(false); }
                catch { _connections.Release(); throw; }
                var id = Interlocked.Increment(ref _clientId);
                _clients[id] = client;
                _ = HandleAsync(id, client);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            if (!_lifetime.IsCancellationRequested) Trace.TraceError("Documentation listener: {0}", exception);
        }
    }

    private async Task HandleAsync(int id, TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, timeout.Token).ConfigureAwait(false);
                if (request is null) return;
                var (method, target, headers) = request.Value;
                if (method is not ("GET" or "HEAD"))
                {
                    await ErrorAsync(stream, 405, "Only GET and HEAD are supported.", false, timeout.Token);
                    return;
                }
                var head = method == "HEAD";
                if (!headers.TryGetValue("Host", out var host) ||
                    !host.Equals(_origin.Authority, StringComparison.OrdinalIgnoreCase) ||
                    (headers.TryGetValue("Origin", out var origin) && origin != _origin.GetLeftPart(UriPartial.Authority)))
                {
                    await ErrorAsync(stream, 403, "This document origin is local to BEngine Hub.", head, timeout.Token);
                    return;
                }
                var queryIndex = target.IndexOf('?');
                var rawPath = queryIndex < 0 ? target : target[..queryIndex];
                if (!rawPath.StartsWith(_prefix, StringComparison.Ordinal) || !TrySegments(rawPath[_prefix.Length..], out var parts))
                {
                    await ErrorAsync(stream, 404, "Documentation was not found.", head, timeout.Token);
                    return;
                }
                var query = ParseQuery(queryIndex < 0 ? string.Empty : target[(queryIndex + 1)..]);
                await RouteAsync(stream, parts, query, head, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
                // Closing a tab or closing the Hub can interrupt an ordinary read.
            }
            catch (Exception exception)
            {
                Trace.TraceError("Documentation request: {0}", exception);
                try { await ErrorAsync(client.GetStream(), 500, "The documentation catalog could not be read. Refresh to retry.", false, timeout.Token); }
                catch (Exception writeException) when (writeException is IOException or SocketException or ObjectDisposedException or OperationCanceledException) { }
            }
            finally
            {
                _clients.TryRemove(id, out _);
                _connections.Release();
            }
        }
    }

    private async Task RouteAsync(NetworkStream stream, string[] parts, Dictionary<string, string> query,
        bool head, CancellationToken token)
    {
        if (parts is ["api", "catalog"])
        {
            var installed = ReadCatalog(() => _engines.Discover());
            query.TryGetValue("engine", out var requested);
            var engine = requested is null ? installed.FirstOrDefault() :
                installed.FirstOrDefault(item => item.Version.Equals(requested, StringComparison.OrdinalIgnoreCase));
            if (engine is null) { await ErrorAsync(stream, 404, "The selected engine is no longer installed.", head, token); return; }
            var packages = ReadCatalog(() => _packages.Discover(engineVersion: engine.Version));
            var result = new
            {
                engine = Describe("engine", engine.DisplayName, engine.Version, engine.Description,
                    Path.Combine(engine.DirectoryPath, "Editor", "Doc"), ContentPath("engine", engine.Version)),
                engines = installed.Select(item => new { item.Version, name = item.DisplayName }),
                packages = packages.Select(package => Describe(package.Id, package.DisplayName, package.Version,
                    package.Description, Path.Combine(package.DirectoryPath, "Editor", "Doc"),
                    ContentPath("package", engine.Version, package.Id, package.Version), package.MinimumEngineVersion)).ToArray()
            };
            await SendAsync(stream, 200, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(result, JsonOptions), head, token);
            return;
        }
        if (parts.Length > 0 && parts[0] == "content")
        {
            string? root = null;
            string[] relative = [];
            if (parts.Length >= 4 && parts[1] == "engine")
            {
                var engine = ReadCatalog(() => _engines.Discover()).FirstOrDefault(item => item.Version.Equals(parts[2], StringComparison.OrdinalIgnoreCase));
                if (engine is not null) root = Path.Combine(engine.DirectoryPath, "Editor", "Doc");
                relative = parts[3..];
            }
            else if (parts.Length >= 6 && parts[1] == "package")
            {
                var engine = ReadCatalog(() => _engines.Discover()).FirstOrDefault(item => item.Version.Equals(parts[2], StringComparison.OrdinalIgnoreCase));
                var package = engine is null ? null : ReadCatalog(() => _packages.Discover(engineVersion: engine.Version)).FirstOrDefault(item =>
                    item.Id.Equals(parts[3], StringComparison.OrdinalIgnoreCase) && item.Version.Equals(parts[4], StringComparison.OrdinalIgnoreCase));
                if (package is not null) root = Path.Combine(package.DirectoryPath, "Editor", "Doc");
                relative = parts[5..];
            }
            if (root is null || !TryDocumentFile(root, relative, out var path))
            { await ErrorAsync(stream, 404, "The selected document is no longer available.", head, token); return; }
            try
            {
                await SendFileAsync(stream, path, head, token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
            { await ErrorAsync(stream, 404, "The selected document is no longer available.", head, token); }
            return;
        }
        var assetPath = parts.Length == 0 ? "index.html" : string.Join('/', parts);
        if (AssetResources.TryGetValue(assetPath, out var resource))
        {
            using var source = typeof(LauncherDocumentationServer).Assembly.GetManifestResourceStream(resource)!;
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, token).ConfigureAwait(false);
            await SendAsync(stream, 200, ContentType(assetPath)!, buffer.ToArray(), head, token);
            return;
        }
        await ErrorAsync(stream, 404, "Documentation was not found.", head, token);
    }

    private string ContentPath(params string[] parts) => _prefix + "content/" + string.Join('/', parts.Select(Uri.EscapeDataString)) + "/";

    private static object Describe(string id, string name, string version, string description,
        string root, string contentBaseUrl, string? minimumEngineVersion = null)
    {
        var pages = ReadCatalog(() => SafeRoot(root) ? Directory.EnumerateFiles(root, "*", new EnumerationOptions
            { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".html" or ".htm" or ".md")
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path.Equals("index.html", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new { title = Path.ChangeExtension(path, null), url = contentBaseUrl + string.Join('/', path.Split('/').Select(Uri.EscapeDataString)) })
            .ToArray() : []);
        return new { id, name, version, description, minimumEngineVersion, contentBaseUrl,
            documentationUrl = pages.FirstOrDefault()?.url, pages };
    }

    private static T ReadCatalog<T>(Func<T> read)
    {
        try { return read(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A changed/incomplete manifest is a retryable visible API error,
            // rather than an apparently successful empty package catalog.
            throw new DocumentationCatalogException(exception);
        }
    }

    private sealed class DocumentationCatalogException(Exception inner) : Exception("The documentation catalog could not be read.", inner);

    private static bool SafeRoot(string root)
    {
        if (!Directory.Exists(root)) return false;
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }

    private static bool TryDocumentFile(string root, string[] relative, out string path)
    {
        path = string.Empty;
        if (!SafeRoot(root) || relative.Length == 0 || ContentType(relative[^1]) is null) return false;
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine([root, .. relative]));
        if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate)) return false;
        var current = root;
        foreach (var segment in relative)
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        }
        path = candidate;
        return true;
    }

    private static bool TrySegments(string value, out string[] parts)
    {
        parts = value.Length == 0 ? [] : value.Split('/').Select(Uri.UnescapeDataString).ToArray();
        return parts.All(part => part.Length > 0 && part is not ("." or "..") &&
            part.IndexOfAny(['/', '\\', ':', '\0', '\r', '\n', '?', '#']) < 0 && !part.Any(char.IsControl));
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.IndexOf('=');
            if (split >= 0) result[Uri.UnescapeDataString(pair[..split])] = Uri.UnescapeDataString(pair[(split + 1)..]);
        }
        return result;
    }

    private static async Task<(string Method, string Target, Dictionary<string, string> Headers)?> ReadRequestAsync(
        NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[MaximumHeaderBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), token).ConfigureAwait(false);
            if (read == 0) return null;
            length += read;
            var text = Encoding.ASCII.GetString(buffer, 0, length);
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end < 0) continue;
            var lines = text[..end].Split("\r\n");
            var first = lines[0].Split(' ');
            if (first.Length != 3 || !first[1].StartsWith('/') || !first[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) return null;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var colon = line.IndexOf(':');
                if (colon <= 0 || !headers.TryAdd(line[..colon], line[(colon + 1)..].Trim())) return null;
            }
            return (first[0], first[1], headers);
        }
        await ErrorAsync(stream, 431, "Request headers are too large.", false, token);
        return null;
    }

    private static string? ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8", ".md" or ".txt" or ".cs" or ".h" => "text/plain; charset=utf-8",
        ".css" => "text/css; charset=utf-8", ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8", ".svg" => "image/svg+xml", ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".gif" => "image/gif", ".ico" => "image/x-icon",
        ".woff" => "font/woff", ".woff2" => "font/woff2", ".ttf" => "font/ttf",
        ".mp4" => "video/mp4", ".webm" => "video/webm", ".mp3" => "audio/mpeg", ".ogg" => "audio/ogg", ".wav" => "audio/wav", _ => null
    };

    private static Task ErrorAsync(NetworkStream stream, int status, string message, bool head, CancellationToken token) =>
        SendAsync(stream, status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(new { error = message }), head, token);

    private static async Task SendFileAsync(NetworkStream stream, string path, bool head, CancellationToken token)
    {
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await WriteHeadersAsync(stream, 200, ContentType(path)!, source.Length, token, document: true);
        if (!head) await source.CopyToAsync(stream, token).ConfigureAwait(false);
    }

    private static async Task SendAsync(NetworkStream stream, int status, string type, byte[] body, bool head, CancellationToken token)
    {
        await WriteHeadersAsync(stream, status, type, body.Length, token);
        if (!head) await stream.WriteAsync(body, token).ConfigureAwait(false);
    }

    private static Task WriteHeadersAsync(NetworkStream stream, int status, string type, long length,
        CancellationToken token, bool document = false)
    {
        var reason = status switch { 200 => "OK", 403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed", 431 => "Request Header Fields Too Large", _ => "Internal Server Error" };
        var policy = document ? "default-src 'none'; img-src 'self' data:; media-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'" :
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; media-src 'self'; connect-src 'self'; font-src 'self'; object-src 'none'; base-uri 'self'; form-action 'none'; frame-ancestors 'none'";
        var headers = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {length}\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: {policy}\r\n\r\n";
        return stream.WriteAsync(Encoding.ASCII.GetBytes(headers), token).AsTask();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _listener.Stop();
        foreach (var client in _clients.Values) client.Dispose();
        _ = _acceptLoop.ContinueWith(_ => _lifetime.Dispose(), TaskScheduler.Default);
    }
}
