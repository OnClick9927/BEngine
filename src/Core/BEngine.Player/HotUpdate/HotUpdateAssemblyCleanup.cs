using System.Reflection;

namespace BEngine.Player.HotUpdate;

/// <summary>Applies the managed-code lease policy before releasing engine registrations.</summary>
internal static class HotUpdateAssemblyCleanup
{
    internal static void Unregister(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        // AOT and game domains may share canonical assemblies. Preserve their
        // registrations while any stage still owns a managed-code lease.
        var unleased = ManagedCodePrecompiledAssemblies.GetUnleasedAssemblies(assemblies);
        RuntimeAssemblyCleanup.Unregister(unleased);
    }
}