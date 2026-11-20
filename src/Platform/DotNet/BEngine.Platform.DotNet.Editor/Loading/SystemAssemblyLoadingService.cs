using BEngine.Platform;
using BEngine.Hosting;
using System.Reflection;
using System.Runtime.Loader;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
namespace BEngine.Editor;
public sealed class SystemAssemblyLoadingService(FileUtility files, PathUtility paths, PlatformUtility platform) : IAssemblyLoadingService
{
    public IEnumerable<Assembly> DefaultAssemblies => AssemblyDomains.Current.DefaultAssemblies;
    public bool IsCollectible(Assembly assembly) => AssemblyDomains.Current.IsCollectible(assembly);
    public Assembly LoadDefault(AssemblyName name) => AssemblyDomains.Current.LoadDefault(name);
    public AssemblyName GetAssemblyName(string path) => AssemblyName.GetAssemblyName(path);
    public IDisposable ObserveDefaultResolution(Func<AssemblyName, Assembly?> resolver) => AssemblyDomains.Current.ObserveDefaultResolution(resolver);
    public IAssemblyScope CreateProjectScope(IReadOnlyDictionary<string, string> references, IReadOnlyDictionary<string, Assembly> shared) => new Scope(new ProjectScriptLoadContext(references, shared, files, paths, platform));
    public IAssemblyScope CreatePackageScope(string packageId, string directory, Func<AssemblyName, Assembly?> resolver) => new Scope(new PackageLoadContext(packageId, directory, resolver, files, paths));
    private sealed class Scope(AssemblyLoadContext context) : IAssemblyScope
    {
        public IEnumerable<Assembly> Assemblies => context.Assemblies;
        public WeakReference Lifetime { get; } = new(context);
        public Assembly LoadFromAssemblyPath(string path) => context.LoadFromAssemblyPath(path);
        public void Unload() => context.Unload();
    }
}
