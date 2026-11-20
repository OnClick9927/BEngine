using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace BEngine.Launcher;

internal static class GitHostResolution
{
    // Only used after a failed GitHub connection. Repo paths and credentials never go to a resolver.
    internal static async Task<string?> TryResolveAsync(CancellationToken token, HttpClient? http = null)
    {
        using var owned = http is null ? new HttpClient { Timeout = Timeout.InfiniteTimeSpan } : null;
        var client = http ?? owned!;
        foreach (var endpoint in new[] { "https://dns.alidns.com/resolve?name=github.com&type=A", "https://cloudflare-dns.com/dns-query?name=github.com&type=A" })
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Accept.ParseAdd("application/dns-json");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var output = new MemoryStream(); var buffer = new byte[4096]; int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
                { if (output.Length + count > 65536) throw new InvalidDataException("DNS reply exceeds limit."); output.Write(buffer, 0, count); }
                using var document = JsonDocument.Parse(output.ToArray());
                var root = document.RootElement;
                if (!root.TryGetProperty("Status", out var status) || status.GetInt32() != 0 || !root.TryGetProperty("Answer", out var answer)) continue;
                var addresses = answer.EnumerateArray().Where(item => item.TryGetProperty("type", out var type) && type.GetInt32() == 1 &&
                        item.TryGetProperty("name", out var name) && name.GetString()?.TrimEnd('.').Equals("github.com", StringComparison.OrdinalIgnoreCase) == true)
                    .Select(item => item.TryGetProperty("data", out var data) ? data.GetString() : null)
                    .Where(value => IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                    .Distinct().ToArray();
                if (addresses.Length > 0) return "github.com:443:" + string.Join(',', addresses);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException or FormatException) { }
            token.ThrowIfCancellationRequested();
        }
        return null;
    }
}
