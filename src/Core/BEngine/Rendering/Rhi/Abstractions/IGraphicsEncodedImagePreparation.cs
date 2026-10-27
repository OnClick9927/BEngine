namespace BEngine.Rendering.Rhi;

/// <summary>Optional device service that prepares encoded images without a graphics upload.
/// Unsupported images return null and use the portable decoder. The caller owns a successful
/// preparation and must dispose it after uploading, cancellation, or failure.</summary>
public interface IGraphicsEncodedImagePreparation
{
    BTask<IGraphicsPreparedImage?> PrepareEncodedImageAsync(byte[] encoded, AsyncLoadContext context);
}

/// <summary>A prepared, top-left-origin, straight-alpha RGBA8 image. Creation runs on the device's
/// owning thread, under the caller's upload budget. Disposing also releases an image never uploaded.</summary>
public interface IGraphicsPreparedImage : IDisposable
{
    int Width { get; }
    int Height { get; }
    long ByteSize { get; }
    IGraphicsTexture2D CreateTexture(string label, GraphicsTextureDescription description);
}
