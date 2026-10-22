using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

/// <summary>Admission budget for textures owned by the scene texture caches, including retired GPU resources.</summary>
public static class GraphicsTextureMemory
{
    public static MemoryBudget Budget { get; } = new(256L * 1024 * 1024);
    static GraphicsTextureMemory() => Budget.LimitChanged += budget =>
    {
        if (budget.ReservedBytes > budget.LimitBytes) SceneTextureCache.TrimUnused();
    };
    public static long reservedBytes => Budget.ReservedBytes;
    internal static bool TryReserve(long bytes, out MemoryBudget.Lease? lease) => Budget.TryReserve(bytes, out lease);
    internal static void Retire(IGraphicsDevice device, IGraphicsTexture2D texture, MemoryBudget.Lease? lease)
    {
        if (lease is not null && device is IGraphicsResourceDisposal disposal)
        {
            disposal.DisposeResource(texture, lease); return;
        }
        var resource = new Retirement(texture, lease);
        if (device is IGraphicsResourceRetirement retirement) retirement.RetireResource(resource);
        else resource.Dispose();
    }
    private sealed class Retirement(IGraphicsTexture2D texture, MemoryBudget.Lease? lease) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            try { texture.Dispose(); } finally { lease?.Dispose(); }
        }
    }
}
