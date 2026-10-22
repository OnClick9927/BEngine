using System.Reflection;
using System.Runtime.Loader;

namespace BEngine.HotUpdate;

/// <summary>Assemblies supplied by the running host and shared with managed-code releases.</summary>
public static class ManagedCodeHostAssemblies
{
    private static readonly Dictionary<string, Assembly> Assemblies = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (AssemblyLoadContext.GetLoadContext(assembly) != AssemblyLoadContext.Default)
            throw new ArgumentException("A host assembly must belong to the default load context.", nameof(assembly));
        lock (Assemblies) Assemblies[assembly.GetName().Name!] = assembly;
    }

    internal static Assembly? Find(string name)
    {
        lock (Assemblies) return Assemblies.GetValueOrDefault(name);
    }

    internal static bool Contains(Assembly assembly) => ReferenceEquals(Find(assembly.GetName().Name!), assembly);
}
