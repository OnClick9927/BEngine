using BEngine.ProjectSystem.Editor;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

/// <summary>Contributes generated shader sources through the editor's common build mechanism.</summary>
public interface IShaderSourceProvider
{
    IEnumerable<ShaderBuildSource> CollectSources(IReadOnlyList<AssetRecord> assets,
        IReadOnlyList<ShaderBuildResourceInput> resources, GraphicsBackend backend);
}
