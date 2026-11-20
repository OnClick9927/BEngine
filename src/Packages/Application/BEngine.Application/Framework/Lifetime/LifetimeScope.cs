using System;
using System.Collections.Generic;

namespace BEngine.App;

/// <summary>
/// Owns cancellation and disposable resources on one game loop, without a global owner registry.
/// A late resource is disposed immediately. Resources are released in reverse registration order
/// even when cancellation callbacks or other resources fail. Operations and cancellation must
/// remain on that same loop; cancellation does not schedule background work.
/// </summary>
public sealed class LifetimeScope : IDisposable
{
    private BCancellationSource? _engineCancellation;
    private BCancellationRegistration _externalCancellation;
    private List<IDisposable>? _resources;
    private bool _disposed;
    private bool _cleanupCompleted;

    /// <summary>An optional external cancellation token disposes the complete scope.</summary>
    public LifetimeScope(BCancellationToken cancellationToken = default)
    {
        if (cancellationToken.CanBeCanceled)
        {
            var registration = cancellationToken.Register(Dispose);
            if (_disposed) registration.Dispose();
            else _externalCancellation = registration;
        }
    }

    public BCancellationToken Token
    {
        get
        {
            if (_engineCancellation is null)
            {
                var source = new BCancellationSource();
                // Preserve a stable, canceled token even when first requested during cleanup
                // or after disposal. Empty scopes otherwise need no cancellation source.
                if (_disposed)
                {
                    source.Cancel();
                    if (_cleanupCompleted) source.Dispose();
                }
                _engineCancellation = source;
            }
            return _engineCancellation.Token;
        }
    }
    public bool IsDisposed => _disposed;

    /// <summary>Tracks and returns the resource. Track the same resource once per ownership scope.</summary>
    public T Track<T>(T resource) where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!_disposed)
        {
            (_resources ??= new List<IDisposable>()).Add(resource);
            return resource;
        }
        resource.Dispose();
        return resource;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var resources = _resources;
        _resources = null;
        List<Exception>? failures = null;
        try { _engineCancellation?.Cancel(); }
        catch (Exception failure) { (failures ??= new()).Add(failure); }
        if (resources is not null)
        {
            for (var i = resources.Count - 1; i >= 0; i--)
            {
                try { resources[i].Dispose(); }
                catch (Exception failure) { (failures ??= new()).Add(failure); }
            }
            resources.Clear();
        }
        _externalCancellation.Dispose();
        _externalCancellation = default;
        _cleanupCompleted = true;
        _engineCancellation?.Dispose();
        if (failures is not null) throw new AggregateException("Lifetime cleanup failed.", failures);
    }
}
