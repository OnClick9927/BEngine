using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

internal sealed class GraphicsShaderResourceBuildPolicy : IShaderResourceBuildPolicy
{
    [ModuleInitializer]
    internal static void Register() => RuntimeServiceContributions.RegisterDefault<IShaderResourceBuildPolicy>(new GraphicsShaderResourceBuildPolicy());

    public bool IsRequiredBootstrapResource(string address) =>
        address.Contains("/Shaders/Common/", StringComparison.Ordinal);

    public bool IsSupported(string filePath, IReadOnlyCollection<GraphicsBackend> backends)
    {
        var name = Path.GetFileName(filePath.Replace('\\', '/'));
        var extension = Path.GetExtension(name);
        if (!extension.Equals(".wgsl", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".hlsl", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".glsl", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".spv", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".msl", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".metal", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains(".direct3d11.", StringComparison.OrdinalIgnoreCase)) return backends.Contains(GraphicsBackend.Direct3D11);
        if (name.Contains(".direct3d12.", StringComparison.OrdinalIgnoreCase)) return backends.Contains(GraphicsBackend.Direct3D12);
        if (name.Contains(".direct3d.", StringComparison.OrdinalIgnoreCase))
            return backends.Contains(GraphicsBackend.Direct3D11) || backends.Contains(GraphicsBackend.Direct3D12);
        if (name.Contains(".vulkan.", StringComparison.OrdinalIgnoreCase) || name.Contains(".spirv.", StringComparison.OrdinalIgnoreCase))
            return backends.Contains(GraphicsBackend.Vulkan);
        if (name.Contains(".opengles.", StringComparison.OrdinalIgnoreCase)) return backends.Contains(GraphicsBackend.OpenGLES);
        if (name.Contains(".opengl.", StringComparison.OrdinalIgnoreCase))
            return backends.Contains(GraphicsBackend.OpenGL) || backends.Contains(GraphicsBackend.OpenGLES) || backends.Contains(GraphicsBackend.WebGL);
        if (name.Contains(".webgpu.", StringComparison.OrdinalIgnoreCase)) return backends.Contains(GraphicsBackend.WebGPU);
        if (name.Contains(".webgl.", StringComparison.OrdinalIgnoreCase)) return backends.Contains(GraphicsBackend.WebGL);
        if (name.Contains(".metal.", StringComparison.OrdinalIgnoreCase)) return backends.Contains(GraphicsBackend.Metal);
        return true;
    }
}
