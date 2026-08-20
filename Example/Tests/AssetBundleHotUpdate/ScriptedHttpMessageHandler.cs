using System.Collections.Concurrent;
using System.Net;

namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, byte[]> _content =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _remainingFailures =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _requests =
        new(StringComparer.OrdinalIgnoreCase);

    internal void Add(string path, byte[] bytes) => _content[Normalize(path)] = (byte[])bytes.Clone();

    internal void FailNext(string path, int count = 1) => _remainingFailures[Normalize(path)] = count;

    internal int RequestCount(string path) => _requests.GetValueOrDefault(Normalize(path));

    internal void Replace(string path, byte[] bytes) => Add(path, bytes);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Normalize(request.RequestUri?.AbsolutePath ?? string.Empty);
        _requests.AddOrUpdate(path, 1, static (_, count) => count + 1);
        if (_remainingFailures.TryGetValue(path, out var remaining) && remaining > 0)
        {
            _remainingFailures[path] = remaining - 1;
            return Task.FromException<HttpResponseMessage>(
                new HttpRequestException($"Injected transient failure for '{path}'."));
        }
        if (!_content.TryGetValue(path, out var bytes))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request
            });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new ByteArrayContent((byte[])bytes.Clone())
        });
    }

    private static string Normalize(string path) => "/" + path.Replace('\\', '/').Trim('/');
}
