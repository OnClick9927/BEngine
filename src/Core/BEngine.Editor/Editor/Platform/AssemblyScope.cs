using System.Reflection;
namespace BEngine.Editor;

/// <summary>Application-facing API; the platform host supplies IAssemblyScope through dependency injection.</summary>
public sealed class AssemblyScope
{
    private readonly IAssemblyScope _implementation;

    public AssemblyScope(IAssemblyScope implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public IEnumerable<Assembly> Assemblies => _implementation.Assemblies;

    public WeakReference Lifetime => _implementation.Lifetime;

    public Assembly LoadFromAssemblyPath(string path) =>
        _implementation.LoadFromAssemblyPath(path);

    public void Unload() =>
        _implementation.Unload();
}
