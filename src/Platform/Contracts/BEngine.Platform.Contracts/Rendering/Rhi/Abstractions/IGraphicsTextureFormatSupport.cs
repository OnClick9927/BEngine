namespace BEngine.Rendering.Rhi;

/// <summary>Queries actual sampled texture support before selecting a GPU format.</summary>
public interface IGraphicsTextureFormatSupport
{
    bool SupportsTextureFormat(GraphicsTextureFormat format);
}
