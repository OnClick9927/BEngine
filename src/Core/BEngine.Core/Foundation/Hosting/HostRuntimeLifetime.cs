using System.Reflection;

namespace BEngine.Hosting;

/// <summary>
/// An engine run's initialization scope. The early host owns it until its application
/// execution thread takes over; closing a splash never ends the runtime lifetime.
/// </summary>
public sealed class HostRuntimeLifetime : IDisposable
{
    private static readonly object Gate = new();
    private static HostRuntimeLifetime? _pendingHost;
    private readonly RuntimeInitializationSession _initializationSession;
    private readonly bool _releasedForHandoff;
    private bool _transferred;
    private bool _disposed;

    private HostRuntimeLifetime(RuntimeInitializationSession initializationSession, bool releasedForHandoff)
    {
        _initializationSession = initializationSession;
        _releasedForHandoff = releasedForHandoff;
    }

    public RuntimeInitializationSession InitializationSession => _initializationSession;

    public static HostRuntimeLifetime Begin(bool allowRuntimeThreadHandoff = false)
    {
        lock (Gate)
        {
            if (_pendingHost is not null)
                throw new InvalidOperationException("An early engine lifetime is already awaiting its runtime owner.");
            var session = RuntimeLifecycle.BeginSession();
            try
            {
                session.InitializeThrough(RuntimeInitializeLoadType.BeforeSplashScreen);
                if (allowRuntimeThreadHandoff) session.ReleaseForHostHandoff();
                return _pendingHost = new HostRuntimeLifetime(session, allowRuntimeThreadHandoff);
            }
            catch { session.Dispose(); throw; }
        }
    }

    /// <summary>
    /// Takes the pending early host scope, or creates a fresh runtime scope when
    /// an embedding host has no splash. A successful handoff cannot be repeated.
    /// </summary>
    public static HostRuntimeLifetime Acquire()
    {
        lock (Gate)
        {
            if (_pendingHost is { } host)
            {
                host._initializationSession.ClaimHostHandoff();
                host._transferred = true;
                _pendingHost = null;
                return new HostRuntimeLifetime(host._initializationSession, false);
            }
            var lifetime = Begin();
            _pendingHost = null;
            return lifetime;
        }
    }

    public void InitializeThrough(RuntimeInitializeLoadType phase, IEnumerable<Assembly>? assemblies = null)
    {
        ObjectDisposedException.ThrowIf(_disposed || _transferred, this);
        _initializationSession.InitializeThrough(phase, assemblies);
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return;
            if (!_transferred)
            {
                if (_releasedForHandoff) _initializationSession.ClaimHostHandoff();
                _initializationSession.Dispose();
            }
            _disposed = true;
            if (ReferenceEquals(_pendingHost, this)) _pendingHost = null;
        }
    }
}
