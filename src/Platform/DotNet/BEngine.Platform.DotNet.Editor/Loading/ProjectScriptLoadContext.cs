using BEngine.Platform;
using System.Reflection;
using System.Runtime.Loader;

namespace BEngine.ProjectSystem.Editor;

internal sealed class ProjectScriptLoadContext(
    IReadOnlyDictionary<string, string> references,
    IReadOnlyDictionary<string, Assembly> sharedProjectAssemblies,
    FileUtility files, PathUtility paths, PlatformUtility platform)
    : AssemblyLoadContext($"BEngine.ProjectScripts:{Guid.NewGuid():N}", isCollectible: true)
{
    private readonly Dictionary<string, string> _references = references
        .Where(reference => files.FileExists(reference.Value))
        .ToDictionary(reference => reference.Key, reference => paths.GetFullPath(reference.Value),
            StringComparer.OrdinalIgnoreCase);
    private readonly string[] _probeDirectories = references.Values.Where(files.FileExists)
        .Select(path => paths.GetDirectoryName(paths.GetFullPath(path))!)
        .Append(platform.BaseDirectory)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    private readonly IReadOnlyDictionary<string, Assembly> _sharedProjectAssemblies =
        sharedProjectAssemblies;

    protected override Assembly? Load(AssemblyName requestedName)
    {
        var name = requestedName.Name;
        if (string.IsNullOrWhiteSpace(name)) return null;
        var own = Matches1317();
        [global::BEngine.SkipLoopGuard]
        global::System.Reflection.Assembly? Matches1317()
        {
            var matchSource1317 = Assemblies;
            ArgumentNullException.ThrowIfNull(matchSource1317, "source");
            global::System.ReadOnlySpan<global::System.Reflection.Assembly> matchValues1317;
            if (matchSource1317.GetType() == typeof(global::System.Reflection.Assembly[]))
                matchValues1317 = (global::System.Reflection.Assembly[])(object)matchSource1317;
            else if (matchSource1317.GetType() == typeof(global::System.Collections.Generic.List<global::System.Reflection.Assembly>))
                matchValues1317 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::System.Reflection.Assembly>)(object)matchSource1317);
            else
            {
                foreach (var assembly in (global::System.Collections.Generic.IEnumerable<global::System.Reflection.Assembly>)matchSource1317)
                {
                    if (assembly.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
                        return assembly;
                }
        
                return default!;
            }
        
            foreach (var assembly in matchValues1317)
            {
                if (assembly.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
                    return assembly;
            }
        
            return default!;
        }
        if (own is not null) return own;
        if (_sharedProjectAssemblies.TryGetValue(name, out var projectAssembly)) return projectAssembly;
        var shared = Matches1618();
        [global::BEngine.SkipLoopGuard]
        global::System.Reflection.Assembly? Matches1618()
        {
            var matchSource1618 = platform.GetLoadedAssemblies();
            ArgumentNullException.ThrowIfNull(matchSource1618, "source");
            global::System.ReadOnlySpan<global::System.Reflection.Assembly> matchValues1618;
            if (matchSource1618.GetType() == typeof(global::System.Reflection.Assembly[]))
                matchValues1618 = (global::System.Reflection.Assembly[])(object)matchSource1618;
            else if (matchSource1618.GetType() == typeof(global::System.Collections.Generic.List<global::System.Reflection.Assembly>))
                matchValues1618 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::System.Reflection.Assembly>)(object)matchSource1618);
            else
            {
                foreach (var assembly in (global::System.Collections.Generic.IEnumerable<global::System.Reflection.Assembly>)matchSource1618)
                {
                    if (assembly.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true && AssemblyLoadContext.GetLoadContext(assembly)is not ProjectScriptLoadContext)
                        return assembly;
                }
        
                return default!;
            }
        
            foreach (var assembly in matchValues1618)
            {
                if (assembly.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true && AssemblyLoadContext.GetLoadContext(assembly)is not ProjectScriptLoadContext)
                    return assembly;
            }
        
            return default!;
        }
        if (shared is not null) return shared;
        if (_references.TryGetValue(name, out var path)) return LoadFromAssemblyPath(path);
        var companion = _probeDirectories.Select(directory => paths.Combine(directory, $"{name}.dll"))
            .FirstOrDefault(files.FileExists);
        return companion is null ? null : LoadFromAssemblyPath(companion);
    }
}
