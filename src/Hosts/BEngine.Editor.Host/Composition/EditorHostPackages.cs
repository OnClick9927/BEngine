using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
namespace BEngine.Editor;
internal static class EditorHostPackages
{
    private static bool _initialized;
    internal static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Visit(typeof(EditorHostPackages).Assembly);
        void Visit(Assembly assembly)
        {
            if (!visited.Add(assembly.GetName().Name!)) return;
            foreach (var reference in assembly.GetReferencedAssemblies().Where(n => n.Name?.StartsWith("BEngine", StringComparison.Ordinal) == true))
                Visit(AssemblyLoadContext.Default.LoadFromAssemblyName(reference));
            BEngine.HotUpdate.ManagedCodeHostAssemblies.Register(assembly);
            RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        }
    }
}
