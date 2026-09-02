using System.Net;
using System.Text;
using BEngine.Networking;

namespace BEngine.ExampleTests.Networking;

internal static class HttpNetworkTests
{
    public static async Task RunAsync()
    {
        var handler = new ScriptedHttpMessageHandler(HttpStatusCode.Created, "accepted");
        using (var request = new HttpNetworkRequest(
                   new Uri("https://loopback.invalid/items"), HttpMethod.Post, handler)
               {
                   UploadData = Encoding.UTF8.GetBytes("payload"),
                   UploadContentType = "text/plain"
               })
        {
            request.SetRequestHeader("X-Request-Id", "42");
            var completed = await request.SendAsync().ConfigureAwait(false);
            TestAssert.Require(ReferenceEquals(request, completed),
                "HTTP SendAsync did not return the request instance.");
            TestAssert.Equal(NetworkRequestResult.Success, request.Result,
                "Successful HTTP response was not reported as successful.");
            TestAssert.Equal(HttpStatusCode.Created, request.ResponseCode,
                "HTTP response code was not retained.");
            TestAssert.Equal("accepted", request.DownloadText,
                "HTTP response body was not retained.");
            TestAssert.Equal("yes", request.GetResponseHeader("X-Injected"),
                "HTTP response header was not retained.");
            TestAssert.Equal("payload", handler.RequestBody,
                "HTTP upload body did not reach the injected handler.");
            TestAssert.Equal("42", handler.RequestHeader,
                "HTTP request header did not reach the injected handler.");
        }

        using var protocolError = new HttpNetworkRequest(
            new Uri("https://loopback.invalid/missing"),
            HttpMethod.Get,
            new ScriptedHttpMessageHandler(HttpStatusCode.NotFound, "missing"));
        await protocolError.SendAsync().ConfigureAwait(false);
        TestAssert.Equal(NetworkRequestResult.ProtocolError, protocolError.Result,
            "HTTP error response did not produce a protocol error.");
        TestAssert.Require(protocolError.IsDone && protocolError.Error is not null,
            "HTTP protocol error details were not retained.");

        using var cancellation = new CancellationTokenSource();
        using var canceledRequest = new HttpNetworkRequest(
            new Uri("https://loopback.invalid/wait"),
            HttpMethod.Get,
            new WaitingHttpMessageHandler());
        cancellation.Cancel();
        await canceledRequest.SendAsync(cancellation.Token).ConfigureAwait(false);
        TestAssert.Equal(NetworkRequestResult.Canceled, canceledRequest.Result,
            "HTTP cancellation did not update the request result.");
    }

    private sealed class ScriptedHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public string? RequestHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestHeader = request.Headers.TryGetValues("X-Request-Id", out var values)
                ? values.Single()
                : null;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "text/plain"),
                RequestMessage = request
            };
            response.Headers.Add("X-Injected", "yes");
            return response;
        }
    }

    private sealed class WaitingHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidOperationException("The canceled HTTP handler unexpectedly resumed.");
        }
    }
}
