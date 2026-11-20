using System.Reflection;
namespace BEngine.AssetBundles;

/// <summary>Startup-only composition point; no workflow is imposed by the content library.</summary>
public static class AssetBundleStartupWorkflows
{
    private static readonly List<Registration> Registrations = [];
    static AssetBundleStartupWorkflows() => RuntimeAssemblyCleanup.Register(Unregister);

    /// <summary>Higher priority wins; at equal priority the most recent registration wins. Keep the lease until unload.</summary>
    public static IDisposable Register(Func<AssetBundleStartupContext, IAssetBundleStartupWorkflow> factory, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var registration = new Registration(factory, priority);
        Registrations.Add(registration);
        return registration;
    }

    public static IAssetBundleStartupWorkflow Create(AssetBundleStartupContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Registration? selected = null;
        foreach (var registration in Registrations)
            if (selected is null || registration.Priority >= selected.Priority) selected = registration;
        if (selected is null)
            throw new InvalidOperationException("No staged content workflow is registered. Register a workflow before preparing content.");
        return selected.Factory(context) ?? throw new InvalidOperationException("The startup workflow factory returned null.");
    }

    private static void Unregister(Assembly owner) => Registrations.RemoveAll(r =>
        r.Factory.Method.Module.Assembly == owner || r.Factory.Target?.GetType().Assembly == owner);

    private sealed class Registration(Func<AssetBundleStartupContext, IAssetBundleStartupWorkflow> factory, int priority) : IDisposable
    {
        internal Func<AssetBundleStartupContext, IAssetBundleStartupWorkflow> Factory { get; } = factory;
        internal int Priority { get; } = priority;
        public void Dispose() => Registrations.Remove(this);
    }
}
