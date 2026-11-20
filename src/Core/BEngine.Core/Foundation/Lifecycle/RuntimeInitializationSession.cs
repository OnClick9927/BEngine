using System.Reflection;

namespace BEngine;

/// <summary>One run's initialization state, shared by its scenes and code domains.</summary>
public sealed class RuntimeInitializationSession : IDisposable
{
    private static readonly RuntimeInitializeLoadType[] Phases =
    [
        RuntimeInitializeLoadType.SubsystemRegistration,
        RuntimeInitializeLoadType.AfterAssembliesLoaded,
        RuntimeInitializeLoadType.BeforeSplashScreen,
        RuntimeInitializeLoadType.BeforeSceneLoad,
        RuntimeInitializeLoadType.AfterSceneLoad
    ];
    private readonly HashSet<Action> _initialized = [];
    private readonly HashSet<RuntimeInitializeLoadType> _notified = [];
    private int _ownerThread = Environment.CurrentManagedThreadId;
    private bool _disposed;

    internal RuntimeInitializationSession() { }

    /// <summary>
    /// Runs each available initializer at most once, including initializers registered after an earlier
    /// domain started. With an assembly filter, only that domain advances; other domains are unaffected.
    /// Initializer failures are logged once and do not restart on each scene load.
    /// </summary>
    public void InitializeThrough(RuntimeInitializeLoadType phase, IEnumerable<Assembly>? assemblies = null)
    {
        CheckOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var last = Array.IndexOf(Phases, phase);
        if (last < 0) throw new ArgumentOutOfRangeException(nameof(phase));
        var selected = assemblies?.ToHashSet();
        if (selected is { Count: 0 }) return;
        for (var index = 0; index <= last && !_disposed; index++)
        {
            var current = Phases[index];
            var ran = false;
            foreach (var initializer in RuntimeTypeCache.GetRuntimeInitializers(current))
            {
                if (_disposed) break;
                if (selected is not null && !selected.Contains(initializer.Callback.Method.Module.Assembly)) continue;
                // Add before invoking: an initializer can load a scene or advance another domain.
                if (!_initialized.Add(initializer.Callback)) continue;
                ran = true;
                try { initializer.Callback(); }
                catch (Exception exception)
                {
                    Debug.LogError($"runtime initialization {current} callback {initializer.Description} failed: " +
                                   exception.Message);
                }
            }
            if (!_disposed && (_notified.Add(current) || ran)) RuntimeLifecycle.NotifyInitialization(current);
        }
    }

    internal void ForgetAssemblies(IReadOnlySet<Assembly> assemblies) =>
        _initialized.RemoveWhere(callback => assemblies.Contains(callback.Method.Module.Assembly));

    // Only an early host lifetime may hand its still scene-free initialization
    // state from a platform UI thread to the engine execution thread.
    internal void ReleaseForHostHandoff()
    {
        CheckOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_notified.Contains(RuntimeInitializeLoadType.BeforeSceneLoad))
            throw new InvalidOperationException("A runtime with initialized scenes cannot change execution threads.");
        Volatile.Write(ref _ownerThread, 0);
    }

    internal void ClaimHostHandoff()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var current = Environment.CurrentManagedThreadId;
        var previous = Interlocked.CompareExchange(ref _ownerThread, current, 0);
        if (previous != 0 && previous != current)
            throw new InvalidOperationException("The runtime initialization lifetime has not been released by its host.");
    }

    public void Dispose()
    {
        CheckOwner();
        if (_disposed) return;
        _disposed = true;
        _initialized.Clear();
        _notified.Clear();
        RuntimeLifecycle.EndSession(this);
    }

    private void CheckOwner()
    {
        if (_ownerThread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Runtime initialization must run on its owning engine thread.");
    }
}
