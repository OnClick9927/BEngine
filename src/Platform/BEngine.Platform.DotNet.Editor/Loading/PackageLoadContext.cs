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
            var own = Assemblies.FirstOrDefault(assembly => assembly.GetName().Name?.Equals(
                assemblyName.Name, StringComparison.OrdinalIgnoreCase) == true);
            if (own is not null) return own;
            if (resolveDependency(assemblyName) is { } dependency) return dependency;
            var localPath = paths.Combine(shadowRoot, $"{assemblyName.Name}.dll");
            return files.FileExists(localPath) ? LoadFromAssemblyPath(localPath) : null;
        }
    }
