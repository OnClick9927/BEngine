using System.Reflection;
namespace BEngine.Editor;
public sealed class AssemblyLoadingUtility(IAssemblyLoadingService implementation)
{
    private readonly IAssemblyLoadingService _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));
    public static AssemblyLoadingUtility Current => EditorServices.GetRequiredService<AssemblyLoadingUtility>();
    public IEnumerable<Assembly> DefaultAssemblies => _implementation.DefaultAssemblies;
    public bool IsCollectible(Assembly assembly) => _implementation.IsCollectible(assembly);
    public Assembly LoadDefault(AssemblyName name) => _implementation.LoadDefault(name);
    public AssemblyName GetAssemblyName(string path) => _implementation.GetAssemblyName(path);
    public IDisposable ObserveDefaultResolution(Func<AssemblyName, Assembly?> resolver) => _implementation.ObserveDefaultResolution(resolver);
    public AssemblyScope CreateProjectScope(IReadOnlyDictionary<string, string> references, IReadOnlyDictionary<string, Assembly> shared) => new(_implementation.CreateProjectScope(references, shared));
    public AssemblyScope CreatePackageScope(string packageId, string directory, Func<AssemblyName, Assembly?> resolver) => new(_implementation.CreatePackageScope(packageId, directory, resolver));
}
