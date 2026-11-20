using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

/// <summary>Resource format and bootstrap requirements contributed by a shader implementation.</summary>
public interface IShaderResourceBuildPolicy
{
    bool IsSupported(string filePath, IReadOnlyCollection<GraphicsBackend> backends);
    bool IsRequiredBootstrapResource(string address);
}
