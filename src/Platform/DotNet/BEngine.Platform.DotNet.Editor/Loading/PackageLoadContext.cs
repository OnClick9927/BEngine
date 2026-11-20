using BEngine.Platform;
using System.Reflection;
using System.Runtime.Loader;
namespace BEngine.ProjectSystem;
internal sealed class PackageLoadContext(
        string packageId,
        string shadowRoot,
        Func<AssemblyName, Assembly?> resolveDependency,
        FileUtility files, PathUtility paths)
        : AssemblyLoadContext($"BEngine.Package:{packageId}", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var own = Matches499();
            [global::BEngine.SkipLoopGuard]
            global::System.Reflection.Assembly? Matches499()
            {
                var matchSource499 = Assemblies;
                ArgumentNullException.ThrowIfNull(matchSource499, "source");
                global::System.ReadOnlySpan<global::System.Reflection.Assembly> matchValues499;
                if (matchSource499.GetType() == typeof(global::System.Reflection.Assembly[]))
                    matchValues499 = (global::System.Reflection.Assembly[])(object)matchSource499;
                else if (matchSource499.GetType() == typeof(global::System.Collections.Generic.List<global::System.Reflection.Assembly>))
                    matchValues499 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::System.Reflection.Assembly>)(object)matchSource499);
                else
                {
                    foreach (var assembly in (global::System.Collections.Generic.IEnumerable<global::System.Reflection.Assembly>)matchSource499)
                    {
                        if (assembly.GetName().Name?.Equals(assemblyName.Name, StringComparison.OrdinalIgnoreCase) == true)
                            return assembly;
                    }
            
                    return default!;
                }
            
                foreach (var assembly in matchValues499)
                {
                    if (assembly.GetName().Name?.Equals(assemblyName.Name, StringComparison.OrdinalIgnoreCase) == true)
                        return assembly;
                }
            
                return default!;
            }
            if (own is not null) return own;
            if (resolveDependency(assemblyName) is { } dependency) return dependency;
            var localPath = paths.Combine(shadowRoot, $"{assemblyName.Name}.dll");
            return files.FileExists(localPath) ? LoadFromAssemblyPath(localPath) : null;
        }
    }
