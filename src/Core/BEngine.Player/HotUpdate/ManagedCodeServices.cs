using System.Reflection;

namespace BEngine.Player.HotUpdate;

/// <summary>Dependency ordering and registration lifetime operations for managed-code providers.</summary>
public static class ManagedCodeServices
{
    /// <summary>Orders named modules by dependency, rejecting duplicate names, cycles and missing dependencies.</summary>
    public static T[] OrderModules<T>(IEnumerable<T> modules, Func<T, string> getName,
        Func<T, IEnumerable<string>> getDependencies)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(getName);
        ArgumentNullException.ThrowIfNull(getDependencies);
        return ManagedCodeGraph.Order(modules, getName, getDependencies);
    }

    /// <summary>Removes registrations for assemblies without an active managed-code lease.</summary>
    /// <remarks>Shared assemblies retain their registrations while another stage still holds a lease. This does not unload an assembly domain.</remarks>
    public static void ReleaseRegistrations(IEnumerable<Assembly> assemblies) => HotUpdateAssemblyCleanup.Unregister(assemblies);
}
