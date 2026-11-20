using BEngine.Editor.Build.ProjectSystem;
using Mono.Cecil;
using BEngine.Editor.Build.ProjectSystem;

namespace BEngine.Editor.Build;

/// <summary>Freezes package-owned native requests without a package-name whitelist.</summary>
internal static class NativeRuntimeImages
{
    internal static RuntimeManagedCodeReleaseInputSet RestoreCanonicalImages(
        RuntimeManagedCodeReleaseInputSet release, RuntimeManagedCodeReleaseInputSet originals)
    {
        var available = originals.Assemblies.ToDictionary(static image => image.Name, StringComparer.OrdinalIgnoreCase);
        var admitted = release.Assemblies.ToDictionary(static image => image.Name, StringComparer.OrdinalIgnoreCase);
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var image in release.Assemblies)
            if (available.TryGetValue(image.Name, out var original) && !DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(image.Name) &&
                RequiresNative(original.AssemblyPath)) Visit(image.Name);
        if (required.Count == 0) return release;
        foreach (var name in required) admitted[name] = available[name];
        return new(admitted.Values.OrderBy(static image => image.Name, StringComparer.Ordinal).ToArray(),
            release.PackageResources, release.PackageResourceRoots);

        void Visit(string name)
        {
            if (DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(name) ||
                !available.TryGetValue(name, out var image) || !required.Add(name)) return;
            foreach (var dependency in image.Dependencies) Visit(dependency);
        }
    }

    internal static bool RequiresNative(string path)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        bool __queryResult1733;
        {
            var __querySource1733 = assembly.CustomAttributes;
            ArgumentNullException.ThrowIfNull(__querySource1733, "source");
            __queryResult1733 = false;
            foreach (var __queryItem1733 in __querySource1733)
            {
                if ((__queryItem1733.AttributeType.FullName == "BEngine.Hosting.RuntimeNativeExecutionAttribute"))
                {
                    __queryResult1733 = true;
                    break;
                }
            }
        }
        
        return __queryResult1733;
    }

    internal static IReadOnlyList<string> SelectClosure(IEnumerable<string> paths)
    {
        var candidates = new Dictionary<string, (string Path, string[] Dependencies, bool Native)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            if (DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(assembly.Name.Name)) continue;
            candidates[assembly.Name.Name] = (path, assembly.MainModule.AssemblyReferences.Select(static item => item.Name).ToArray(), Query2946(assembly.CustomAttributes));
            [global::BEngine.SkipLoopGuard]
            bool Query2946(global::System.Collections.Generic.IEnumerable<global::Mono.Cecil.CustomAttribute> querySource2946)
            {
                ArgumentNullException.ThrowIfNull(querySource2946, "source");
                bool Matches2946(global::Mono.Cecil.CustomAttribute attribute) => attribute.AttributeType.FullName == "BEngine.Hosting.RuntimeNativeExecutionAttribute";
                global::System.ReadOnlySpan<global::Mono.Cecil.CustomAttribute> queryValues2946;
                if (querySource2946.GetType() == typeof(global::Mono.Cecil.CustomAttribute[]))
                    queryValues2946 = (global::Mono.Cecil.CustomAttribute[])querySource2946;
                else if (querySource2946.GetType() == typeof(global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttribute>))
                    queryValues2946 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttribute>)querySource2946);
                else
                {
                    foreach (var attribute in querySource2946)
                        if (Matches2946(attribute))
                            return true;
                    return false;
                }
            
                foreach (var attribute in queryValues2946)
                    if (Matches2946(attribute))
                        return true;
                return false;
            }
        }
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, image) in candidates) if (image.Native) Visit(name);
        return selected.Order(StringComparer.Ordinal).Select(name => candidates[name].Path).ToArray();

        void Visit(string name)
        {
            if (!candidates.TryGetValue(name, out var image) || !selected.Add(name)) return;
            foreach (var dependency in image.Dependencies) Visit(dependency);
        }
    }
}
