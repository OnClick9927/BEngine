using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.App.Mvc;

public interface IMvcElement : IDisposable { void Initialize(IServiceProvider services); }
public interface IMvcModel : IMvcElement { }
public interface IMvcCtrl : IMvcElement { }
public interface IMvcView : IMvcElement { }
public interface IMvcUpdate { void Update(Fix64 deltaTime); }
public interface IMvcFixedUpdate { void FixedUpdate(Fix64 deltaTime); }
public interface IMvcLateUpdate { void LateUpdate(Fix64 deltaTime); }

/// <summary>Optional lifetime helpers for pure data components; views need no MonoBehaviour or UI library.</summary>
public abstract class MvcElementBase : IMvcElement
{
    private IServiceProvider? _services;
    private LifetimeScope? _lifetime;
    private bool _initializationAttempted;
    private bool _disposed;
    protected IServiceProvider Services => _services ?? throw new InvalidOperationException("The MVC element has not been initialized or was disposed.");
    protected T Get<T>() where T : notnull => Services.GetRequiredService<T>();
    protected T Track<T>(T resource) where T : IDisposable
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (_lifetime ??= new LifetimeScope()).Track(resource);
    }

    public void Initialize(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initializationAttempted) throw new InvalidOperationException("An MVC element can only be initialized once.");
        _initializationAttempted = true; _services = services;
        OnInitialize();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? errors = null;
        try { OnDispose(); } catch (Exception error) { MvcErrors.Add(ref errors, error); }
        try { _lifetime?.Dispose(); } catch (Exception error) { MvcErrors.Add(ref errors, error); }
        _services = null; _lifetime = null;
        GC.SuppressFinalize(this);
        MvcErrors.Throw(errors);
    }

    protected virtual void OnInitialize() { }
    protected virtual void OnDispose() { }
}

public abstract class ModelBase : MvcElementBase, IMvcModel { }
public abstract class CtrlBase : MvcElementBase, IMvcCtrl { }
public abstract class ViewBase : MvcElementBase, IMvcView { }

internal static class MvcErrors
{
    internal static void Add(ref List<Exception>? errors, Exception error) => (errors ??= []).Add(error);
    internal static void Throw(List<Exception>? errors)
    {
        if (errors is null) return;
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        throw new AggregateException("MVC lifecycle cleanup failed.", errors);
    }
}
