using System.Reflection;
using BEngine.Hosting;

namespace BEngine.Hosting;

/// <summary>Assemblies supplied by the running host and shared with managed-code releases.</summary>
public static class HostAssemblyRegistry
{
    private static readonly Dictionary<string, Assembly> Assemblies = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> Anchors = new(StringComparer.OrdinalIgnoreCase);
    public static int Version { get; private set; }
    /// <summary>Declares a public registration anchor for build-time static-host composition.</summary>
    public static void RegisterAnchor(Type anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        if (!anchor.IsVisible || anchor.ContainsGenericParameters) throw new ArgumentException("A public closed anchor type is required.", nameof(anchor));
        if (anchor.Assembly.IsCollectible || AssemblyDomains.Available is { } loader && !loader.IsDefaultAssembly(anchor.Assembly)) return;
        Register(anchor.Assembly);
        lock (Assemblies)
        {
            if (Anchors.TryAdd(anchor.Assembly.GetName().Name!, anchor.FullName!.Replace('+', '.'))) Version++;
        }
    }
    public static IReadOnlyDictionary<string, string> GetAnchors()
    {
        lock (Assemblies) return new Dictionary<string, string>(Anchors, StringComparer.OrdinalIgnoreCase);
    }
    public static void Register(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        // Host module initializers may run before the platform's initializer. Record their
        // candidates now; Find validates the default-domain boundary before exposing them.
        if (assembly.IsCollectible || AssemblyDomains.Available is { } loader && !loader.IsDefaultAssembly(assembly))
            throw new ArgumentException("A host assembly must belong to the default load context.", nameof(assembly));
        lock (Assemblies) Assemblies[assembly.GetName().Name!] = assembly;
    }

    public static Assembly? Find(string name)
    {
        lock (Assemblies)
        {
            var assembly = Assemblies.GetValueOrDefault(name);
            if (assembly is not null && !AssemblyDomains.Current.IsDefaultAssembly(assembly))
                throw new ArgumentException("A host assembly must belong to the default load context.", nameof(assembly));
            return assembly;
        }
    }

    public static bool Contains(Assembly assembly) => ReferenceEquals(Find(assembly.GetName().Name!), assembly);
}
