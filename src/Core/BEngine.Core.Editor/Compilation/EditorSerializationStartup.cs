using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class EditorSerializationStartup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        YamlSerialization.Initialize();
        var assembly = typeof(YamlSerialization).Assembly;
        if (!assembly.IsCollectible &&
            (BEngine.Hosting.AssemblyDomains.Available is not { } loader || loader.IsDefaultAssembly(assembly)))
        {
            BEngine.Hosting.HostAssemblyRegistry.Register(assembly);
            BEngine.Hosting.HostAssemblyRegistry.Register(typeof(YamlDotNet.Serialization.IYamlTypeConverter).Assembly);
        }
    }
}
