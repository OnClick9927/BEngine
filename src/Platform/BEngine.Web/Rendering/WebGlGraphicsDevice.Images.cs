using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.Platform;

public sealed partial class WebGlGraphicsDevice
{
    private readonly HashSet<int> _preparedImageHandles = [];

    public async BTask<IGraphicsPreparedImage?> PrepareEncodedImageAsync(byte[] encoded, AsyncLoadContext context)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(encoded);
        ArgumentNullException.ThrowIfNull(context);
        context.cancellationToken.ThrowIfCancellationRequested();
        // Other formats retain the portable decoder's orientation/animation rules.
        if (encoded.Length < 33 || !encoded.AsSpan(0, 8).SequenceEqual<byte>([137, 80, 78, 71, 13, 10, 26, 10]) ||
            !ImageCodec.TryGetInfo(encoded, out var width, out var height)) return null;
        int handle;
        try
        {
            if (!WebImageInterop.Available()) return null;
            do
            {
                context.cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                handle = WebImageInterop.Begin(encoded, width, height);
                if (handle == 0) await BTask.Yield(); // Admission waits without allocating native image data.
            } while (handle == 0);
        }
        catch (JSException) { return null; } // An older host can still use the portable codec.
        if (handle <= 0) return null;
        _preparedImageHandles.Add(handle);
        var transferred = false;
        try
        {
            using var cancellation = context.cancellationToken.Register(() => ReleasePreparedImage(handle));
            while (true)
            {
                context.cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                var status = WebImageInterop.Status(handle);
                if (status < 0) return null;
                if (status > 0)
                {
                    transferred = true;
                    return new PreparedImage(this, handle, width, height);
                }
                await BTask.Yield();
            }
        }
        finally { if (!transferred) ReleasePreparedImage(handle); }
    }

    private void ReleasePreparedImage(int handle)
    {
        if (!_preparedImageHandles.Remove(handle)) return;
        try { WebImageInterop.Release(handle); } catch (JSException) { }
    }

    private void ReleasePreparedImages()
    {
        foreach (var handle in _preparedImageHandles.ToArray()) ReleasePreparedImage(handle);
    }

    private sealed class PreparedImage(WebGlGraphicsDevice owner, int handle, int width, int height) : IGraphicsPreparedImage
    {
        private bool _disposed, _uploaded;
        public int Width => width;
        public int Height => height;
        public long ByteSize => (long)width * height * 4;
        public IGraphicsTexture2D CreateTexture(string label, GraphicsTextureDescription description)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            owner.ThrowIfDisposed();
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
            description.Validate();
            if (_uploaded) throw new InvalidOperationException("The prepared image has already been uploaded.");
            if (description.Width != Width || description.Height != Height || description.MipLevels != 1 ||
                description.Format != GraphicsTextureFormat.Rgba8Unorm || description.Usage != GraphicsTextureUsage.Sampled)
                throw new ArgumentException("A prepared image requires a matching, sampled RGBA8 description.", nameof(description));
            try
            {
                var texture = WebImageInterop.Upload(handle, JsonSerializer.Serialize(description,
                    WebGlJsonContext.Default.GraphicsTextureDescription));
                if (texture <= 0) throw new InvalidDataException("The browser did not create the prepared texture.");
                _uploaded = true;
                return new WebGlTexture(owner, label, description, texture, ownsHandle: true);
            }
            finally { owner.InvalidateTextureBindings(); }
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.ReleasePreparedImage(handle);
        }
    }
}

internal static partial class WebImageInterop
{
    [JSImport("globalThis.BEngineWebImages.available")]
    internal static partial bool Available();
    [JSImport("globalThis.BEngineWebImages.begin")]
    internal static partial int Begin([JSMarshalAs<JSType.MemoryView>] Span<byte> encoded, int width, int height);
    [JSImport("globalThis.BEngineWebImages.status")]
    internal static partial int Status(int handle);
    [JSImport("globalThis.BEngineWebImages.upload")]
    internal static partial int Upload(int handle, string description);
    [JSImport("globalThis.BEngineWebImages.release")]
    internal static partial void Release(int handle);
}
