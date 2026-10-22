using BEngine.Platform;
using System.Reflection;
using System.Runtime.Loader;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
namespace BEngine.Editor;
public sealed class SystemAssemblyLoadingService(FileUtility files, PathUtility paths, PlatformUtility platform) : IAssemblyLoadingService
{
    public IEnumerable<Assembly> DefaultAssemblies => AssemblyLoadContext.Default.Assemblies;
    public bool IsCollectible(Assembly assembly) => AssemblyLoadContext.GetLoadContext(assembly)?.IsCollectible == true;
    public Assembly LoadDefault(AssemblyName name) => Assembly.Load(name);
    public AssemblyName GetAssemblyName(string path) => AssemblyName.GetAssemblyName(path);
    public IDisposable ObserveDefaultResolution(Func<AssemblyName, Assembly?> resolver) => new ResolutionSubscription(resolver);
    public IAssemblyScope CreateProjectScope(IReadOnlyDictionary<string, string> references, IReadOnlyDictionary<string, Assembly> shared) => new Scope(new ProjectScriptLoadContext(references, shared, files, paths, platform));
    public IAssemblyScope CreatePackageScope(string packageId, string directory, Func<AssemblyName, Assembly?> resolver) => new Scope(new PackageLoadContext(packageId, directory, resolver, files, paths));
    private sealed class Scope(AssemblyLoadContext context) : IAssemblyScope
    {
        public IEnumerable<Assembly> Assemblies => context.Assemblies;
        public WeakReference Lifetime { get; } = new(context);
        public Assembly LoadFromAssemblyPath(string path) => context.LoadFromAssemblyPath(path);
        public void Unload() => context.Unload();
    }
    private sealed class ResolutionSubscription : IDisposable
    {
        private readonly Func<AssemblyLoadContext, AssemblyName, Assembly?> _callback;
        public ResolutionSubscription(Func<AssemblyName, Assembly?> resolver)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            _callback = (_, name) => resolver(name);
            AssemblyLoadContext.Default.Resolving += _callback;
        }
        public void Dispose() => AssemblyLoadContext.Default.Resolving -= _callback;
    }
}
