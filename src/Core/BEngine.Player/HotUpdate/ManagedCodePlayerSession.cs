using System.Reflection;
using System.Threading;

namespace BEngine.Player.HotUpdate;

/// <summary>Owns one prepared or active managed-code domain and its service lifetime.</summary>
public sealed class ManagedCodePlayerSession : IDisposable
{
    private readonly Action<HotUpdateDomain>? _onActivated;
    private readonly Action<Exception>? _onDisposalFailure;
    private PreparedHotUpdateDomain? _prepared;
    private HotUpdateDomain? _active;
    private Assembly[] _assemblies;
    private IDisposable? _runtimeFactoryServices;
    private IDisposable? _typePreference;
    private BTask? _disposeOperation;
    private BTaskCompletionSource? _activationCompletion;
    private bool _activating;
    private bool _disposed;

    public ManagedCodePlayerSession(
        PreparedHotUpdateDomain prepared,
        Action<HotUpdateDomain>? onActivated = null,
        Action<Exception>? onDisposalFailure = null)
    {
        _prepared = prepared ?? throw new ArgumentNullException(nameof(prepared));
        _onActivated = onActivated;
        _onDisposalFailure = onDisposalFailure;
        _assemblies = prepared.Assemblies.ToArray();
    }

    public IReadOnlyList<Assembly> Assemblies => Array.AsReadOnly(_assemblies);

    public void OwnRuntimeFactoryServices(IDisposable services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_runtimeFactoryServices is not null)
            throw new InvalidOperationException("The HotUpdate session already owns runtime-factory services.");
        _runtimeFactoryServices = services;
    }

    public async BValueTask ActivateAsync(
        IServiceProvider hostServices,
        BCancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_active is not null) return;
        if (_activating)
            throw new InvalidOperationException("The HotUpdate session is already being activated.");

        var prepared = _prepared ??
                       throw new InvalidOperationException("The HotUpdate session has no prepared domain.");
        _prepared = null;
        _activating = true;
        var completion = _activationCompletion = new BTaskCompletionSource();
        try
        {
            _typePreference = RuntimeTypeCache.PreferAssemblies(_assemblies);
            _active = await prepared.ActivateAsync(hostServices, cancellationToken);
            _onActivated?.Invoke(_active);
        }
        catch
        {
            try { await prepared.DisposeAsync(); }
            finally
            {
                Interlocked.Exchange(ref _typePreference, null)?.Dispose();
                _assemblies = [];
            }
            throw;
        }
        finally
        {
            _activating = false;
            _activationCompletion = null;
            completion.TrySetResult();
        }
    }

    public BValueTask DisposeAsync()
    {
        if (_disposeOperation is not null) return new(_disposeOperation);
        // Publish before invoking module or service teardown, which may reenter.
        var completion = new BTaskCompletionSource();
        _disposeOperation = completion.Task;
        _disposed = true;
        _ = CompleteDisposalAsync(completion);
        return new BValueTask(_disposeOperation);
    }

    private async BTask CompleteDisposalAsync(BTaskCompletionSource completion)
    {
        try { await DisposeCoreAsync(); completion.SetResult(); }
        catch (Exception exception) { completion.SetException(exception); }
    }

    private async BTask DisposeCoreAsync()
    {
        // A prepared domain transfers ownership only when activation finishes.
        // Keep its services and type preference alive until that transfer settles.
        if (_activationCompletion is { } activation) await activation.Task;
        var active = _active;
        _active = null;
        var prepared = _prepared;
        _prepared = null;
        _assemblies = [];
        var runtimeFactoryServices = _runtimeFactoryServices;
        _runtimeFactoryServices = null;
        List<Exception>? failures = null;
        if (active is not null)
        {
            try { await active.DisposeAsync(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        if (prepared is not null)
        {
            try { await prepared.DisposeAsync(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        try { Interlocked.Exchange(ref _typePreference, null)?.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        try { runtimeFactoryServices?.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        if (failures is { Count: > 0 })
            throw new AggregateException("Player managed-code session shutdown failed.", failures);
    }

    public void Dispose()
    {
        var operation = DisposeAsync().AsBTask();
        if (operation.IsCompleted)
        {
            PlayerExecution.Complete(operation);
            return;
        }
        _ = ObserveDisposalAsync(operation);
    }

    private async BValueTask ObserveDisposalAsync(BTask operation)
    {
        try { await operation; }
        catch (Exception exception)
        {
            if (_onDisposalFailure is { } report) report(exception);
            else Debug.LogError($"Managed-code session shutdown failed: {exception}");
        }
    }
}
