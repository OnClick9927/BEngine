using System.Reflection;
using System.Runtime.Loader;

namespace BEngine.ExampleTests.PackageSourceCompilation;

internal sealed class PackageAssemblyLoadContext(IReadOnlyDictionary<string, string> assemblyPaths)
    : AssemblyLoadContext("BEngine.PackageSourceCompilation.Test", isCollectible: true)
{
    private readonly IReadOnlyDictionary<string, string> _assemblyPaths = assemblyPaths;

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (string.IsNullOrWhiteSpace(name)) return null;
        var shared = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(candidate =>
            candidate.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
        if (shared is not null) return shared;
        return _assemblyPaths.TryGetValue(name, out var path) && File.Exists(path)
            ? LoadFromAssemblyPath(Path.GetFullPath(path))
            : null;
    }
}
