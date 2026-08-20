using System.Reflection;
using System.Runtime.Loader;

namespace BEngine.ProjectSystem.Editor;

internal sealed class ProjectScriptLoadContext(
    IReadOnlyDictionary<string, string> references,
    IReadOnlyDictionary<string, Assembly> sharedProjectAssemblies)
    : AssemblyLoadContext($"BEngine.ProjectScripts:{Guid.NewGuid():N}", isCollectible: true)
{
    private readonly Dictionary<string, string> _references = references
        .Where(reference => File.Exists(reference.Value))
        .ToDictionary(reference => reference.Key, reference => Path.GetFullPath(reference.Value),
            StringComparer.OrdinalIgnoreCase);
    private readonly string[] _probeDirectories = references.Values.Where(File.Exists)
        .Select(path => Path.GetDirectoryName(Path.GetFullPath(path))!)
        .Append(AppContext.BaseDirectory)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    private readonly IReadOnlyDictionary<string, Assembly> _sharedProjectAssemblies =
        sharedProjectAssemblies;

    protected override Assembly? Load(AssemblyName requestedName)
    {
        var name = requestedName.Name;
        if (string.IsNullOrWhiteSpace(name)) return null;
        var own = Assemblies.FirstOrDefault(assembly =>
            assembly.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
        if (own is not null) return own;
        if (_sharedProjectAssemblies.TryGetValue(name, out var projectAssembly)) return projectAssembly;
        var shared = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
            assembly.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true &&
            AssemblyLoadContext.GetLoadContext(assembly) is not ProjectScriptLoadContext);
        if (shared is not null) return shared;
        if (_references.TryGetValue(name, out var path)) return LoadFromAssemblyPath(path);
        var companion = _probeDirectories.Select(directory => Path.Combine(directory, $"{name}.dll"))
            .FirstOrDefault(File.Exists);
        return companion is null ? null : LoadFromAssemblyPath(companion);
    }
}
