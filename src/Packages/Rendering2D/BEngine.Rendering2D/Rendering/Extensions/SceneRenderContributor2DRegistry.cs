using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

/// <summary>Registers 2D world extensions in the shared device-owned drawing pipeline.</summary>
public static class SceneRenderContributor2DRegistry
{
    public static void Register(string id, Func<IGraphicsDevice, ISceneRenderContributor2D> factory) =>
        RenderContributorRegistry.Register(id, factory);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Unregister(string id) => RenderContributorRegistry.UnregisterOwned(id, Assembly.GetCallingAssembly());

    internal static void UnregisterAssembly(Assembly assembly) => RenderContributorRegistry.UnregisterAssembly(assembly);
    internal static bool HasRegistrationsFrom(Assembly assembly) => RenderContributorRegistry.HasRegistrationsFrom(assembly);
    internal static ReadOnlySpan<IRenderContributor> GetContributors(IGraphicsDevice device) => RenderContributorRegistry.GetContributors(device);
    internal static void Release(IGraphicsDevice device) => RenderContributorRegistry.Release(device);
}
