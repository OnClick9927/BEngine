using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Hosting;
namespace BEngine.Workbench;
internal static class EditorHostPackages
{
    private static bool _initialized;
    internal static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Visit(typeof(EditorHostPackages).Assembly);
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "BEngine.*.dll"))
            Visit(AssemblyDomains.Current.LoadDefault(AssemblyName.GetAssemblyName(path)));
        void Visit(Assembly assembly)
        {
            if (!visited.Add(assembly.GetName().Name!)) return;
            {
                var __filteredSource765 = assembly.GetReferencedAssemblies();
                ArgumentNullException.ThrowIfNull(__filteredSource765, "source");
                foreach (var reference in __filteredSource765)
                {
                    if (!(reference.Name?.StartsWith("BEngine", StringComparison.Ordinal) == true))
                        continue;
                    Visit(AssemblyDomains.Current.LoadDefault(reference));
                }
            }
            BEngine.Hosting.HostAssemblyRegistry.Register(assembly);
            RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        }
    }
}
