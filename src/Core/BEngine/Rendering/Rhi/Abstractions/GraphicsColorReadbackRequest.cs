namespace BEngine.Rendering.Rhi;

/// <summary>
/// A one-shot readback ticket. Once ready, the returned image never changes. Dispose tickets
/// that are no longer needed so backend staging resources can be released promptly.
/// </summary>
public abstract class GraphicsColorReadbackRequest : IDisposable
{
    public abstract GraphicsRect Region { get; }
    public abstract GraphicsColorReadbackStatus Status { get; }
    public abstract string Error { get; }
    public abstract bool TryGetResult(out GraphicsColorReadbackImage? image);
    public abstract void Dispose();

    public static GraphicsColorReadbackRequest Unavailable(GraphicsRect region, string reason) =>
        new CompletedRequest(region, GraphicsColorReadbackStatus.Unavailable, null,
            string.IsNullOrWhiteSpace(reason) ? "Color readback is unavailable." : reason);

    public static GraphicsColorReadbackRequest Failed(GraphicsRect region, string error) =>
        new CompletedRequest(region, GraphicsColorReadbackStatus.Failed, null,
            string.IsNullOrWhiteSpace(error) ? "Color readback failed." : error);

    internal static GraphicsColorReadbackRequest Ready(
        GraphicsRect region,
        GraphicsColorReadbackImage image) =>
        new CompletedRequest(region, GraphicsColorReadbackStatus.Ready, image, string.Empty);

    private sealed class CompletedRequest(
        GraphicsRect region,
        GraphicsColorReadbackStatus status,
        GraphicsColorReadbackImage? image,
        string error) : GraphicsColorReadbackRequest
    {
        private bool _disposed;

        public override GraphicsRect Region => region;
        public override GraphicsColorReadbackStatus Status =>
            _disposed ? GraphicsColorReadbackStatus.Disposed : status;
        public override string Error => _disposed ? "The color readback request was disposed." : error;

        public override bool TryGetResult(out GraphicsColorReadbackImage? result)
        {
            result = !_disposed && status == GraphicsColorReadbackStatus.Ready ? image : null;
            return result is not null;
        }

        public override void Dispose() => _disposed = true;
    }
}
