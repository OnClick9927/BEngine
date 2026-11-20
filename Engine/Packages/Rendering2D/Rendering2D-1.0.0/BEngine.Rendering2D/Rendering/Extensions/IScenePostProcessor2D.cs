using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public interface IScenePostProcessor2D : IDisposable
{
    string packageId { get; }
    bool IsActive(Scene scene, Camera2D camera);
    void Render(Scene scene, Camera2D camera, IGraphicsTexture2D source, GraphicsRect destination);
}
