namespace BEngine.Editor;

/// <summary>Attaches an editor host's services for the lifetime of a composition scope.</summary>
public static class EditorServiceScope
{
    /// <summary>
    /// Makes host services available to editor extensions. Disposing the scope restores the
    /// previous host, releases contribution-owned services, and leaves host ownership unchanged.
    /// </summary>
    /// <remarks>
    /// Hosts must keep the provider alive until the scope is disposed. This operation is for
    /// host composition; extensions should query <see cref="EditorServices"/> instead.
    /// </remarks>
    public static IDisposable Attach(IServiceProvider services) => EditorServiceAccess.Attach(services);
}
