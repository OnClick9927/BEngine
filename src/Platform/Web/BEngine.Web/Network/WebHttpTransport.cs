using BEngine.Platform.DotNet;

namespace BEngine.Platform;

/// <summary>The browser bridge is probed before submission; an accepted request is never submitted twice.</summary>
public sealed class WebHttpTransport : IPlatformHttpTransport
{
    public static WebHttpTransport Shared { get; } = new();
    public IPlatformHttpOperation Send(PlatformHttpRequest request, IPlatformHttpDownloadHandler handler) =>
        BrowserHttpTransport.TrySend(request, handler, out var operation)
            ? operation! : DotNetHttpTransport.Shared.Send(request, handler);
}
