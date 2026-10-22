using System.Reflection;
namespace BEngine.Editor;
public interface IAssemblyLoadingService
{
    IEnumerable<Assembly> DefaultAssemblies { get; }
    bool IsCollectible(Assembly assembly);
    Assembly LoadDefault(AssemblyName name);
    AssemblyName GetAssemblyName(string path);
    IDisposable ObserveDefaultResolution(Func<AssemblyName, Assembly?> resolver);
    IAssemblyScope CreateProjectScope(IReadOnlyDictionary<string, string> references, IReadOnlyDictionary<string, Assembly> shared);
    IAssemblyScope CreatePackageScope(string packageId, string directory, Func<AssemblyName, Assembly?> resolver);
}
