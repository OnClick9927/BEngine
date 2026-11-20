using System.Runtime.CompilerServices;
using BEngine.Hosting;

namespace BEngine;

/// <summary>Registers source asset references and scene/prefab document codecs.</summary>
public static class AssetPipelineRuntime
{
    [ModuleInitializer]
    public static void Initialize()
    {
        AssetReferenceResolver.Register();
        HostAssemblyRegistry.RegisterAnchor(typeof(AssetPipelineRuntime));
    }
}
