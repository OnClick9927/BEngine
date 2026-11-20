using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using BEngine.Rendering.Rhi;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

public interface ISceneLightingShaderContext2D : IDisposable
{
    void BeginFrame();
    void BeginScene(Scene scene);
    void EndFrame();
    (IReadOnlyList<NVector4> Values, IReadOnlyList<string> CookieTextures) Prepare(
        GameObject target, SceneQuad2D quad, RenderCamera camera, int width, int height, bool useGraphNormal);
}

public static class SceneLightingShaderRegistry2D
{
    private static Func<IGraphicsDevice, ISceneLightingShaderContext2D>? _factory;
    private static Func<string>? _shaderFunctions;
    private static Assembly? _owner;
    private static string? _packageId;

    public static int Version { get; private set; }
    public static bool IsAvailable => _factory is not null && RuntimePackageState.IsEnabled(_packageId!);
    public static string? ShaderFunctions => IsAvailable ? _shaderFunctions?.Invoke() : null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register(string packageId, Func<IGraphicsDevice, ISceneLightingShaderContext2D> factory,
        Func<string> shaderFunctions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(shaderFunctions);
        _factory = factory;
        _shaderFunctions = shaderFunctions;
        _owner = Assembly.GetCallingAssembly();
        _packageId = packageId;
        Version++;
    }

    public static bool TryCreate(IGraphicsDevice device, [NotNullWhen(true)] out ISceneLightingShaderContext2D? context)
    {
        context = IsAvailable ? _factory!(device) : null;
        return context is not null;
    }

    internal static void UnregisterAssembly(Assembly assembly)
    {
        if (_owner != assembly) return;
        _factory = null;
        _shaderFunctions = null;
        _owner = null;
        _packageId = null;
        Version++;
    }
}
