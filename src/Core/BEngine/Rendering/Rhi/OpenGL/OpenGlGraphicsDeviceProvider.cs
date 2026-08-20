using Silk.NET.OpenGL;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi.OpenGL;

public sealed class OpenGlGraphicsDeviceProvider : IGraphicsDeviceProvider
{
    private readonly GL _api;

    public GraphicsBackend Backend => GraphicsBackend.OpenGL;

    public OpenGlGraphicsDeviceProvider(GL api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public GraphicsBackendSupport QuerySupport() => new(
        Backend,
        GraphicsBackendAvailability.Available,
        "A host-owned OpenGL context and Silk.NET API were supplied.");

    public IGraphicsDevice CreateDevice() => new OpenGlGraphicsDevice(_api);
}
