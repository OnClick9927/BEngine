using System.Reflection;
namespace BEngine.Editor;
public interface IAssemblyScope
{
    IEnumerable<Assembly> Assemblies { get; }
    WeakReference Lifetime { get; }
    Assembly LoadFromAssemblyPath(string path);
    void Unload();
}
