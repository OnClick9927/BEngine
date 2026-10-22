using System.Reflection;
using System.Threading;
using BEngine.HotUpdate;

namespace BEngine.Player;

internal sealed class PlayerHotUpdateSession : IDisposable
{
    private readonly PlayerManagedCodeStage _stage;
    private PreparedHotUpdateDomain? _prepared;
    private HotUpdateDomain? _active;
    private Assembly[] _assemblies;
    private IDisposable? _runtimeFactoryServices;
    private IDisposable? _typePreference;
    private BTask? _disposeOperation;
    private bool _activating;
    private bool _disposed;

    internal PlayerHotUpdateSession(
        PreparedHotUpdateDomain prepared,
        PlayerManagedCodeStage stage = PlayerManagedCodeStage.HotUpdate)
    {
        _prepared = prepared ?? throw new ArgumentNullException(nameof(prepared));
        _stage = stage;
        _assemblies = prepared.Assemblies.ToArray();
    }

    internal IReadOnlyList<Assembly> Assemblies => Array.AsReadOnly(_assemblies);

    internal void OwnRuntimeFactoryServices(IDisposable services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_runtimeFactoryServices is not null)
            throw new InvalidOperationException("The HotUpdate session already owns runtime-factory services.");
        _runtimeFactoryServices = services;
    }

    internal async BValueTask ActivateAsync(
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
        try
        {
            _typePreference = RuntimeTypeCache.PreferAssemblies(_assemblies);
            _active = await prepared.ActivateAsync(hostServices, cancellationToken);
            if (_stage == PlayerManagedCodeStage.Aot)
            {
                PlayerStartupDiagnostics.Phase("03_AOT_ASSEMBLY_ACTIVATED",
                    $"release={_active.Release.ReleaseId};runtime={_active.RuntimeKind};" +
                    $"assemblies={_assemblies.Length}");
                Debug.Log(
                    $"Activated AOT startup release '{_active.Release.ReleaseId}' using {_active.RuntimeKind}.");
            }
            else
            {
                PlayerStartupDiagnostics.Phase("05_HOTUPDATE_INJECTED",
                    $"release={_active.Release.ReleaseId};runtime={_active.RuntimeKind};" +
                    $"assemblies={_assemblies.Length}");
                Debug.Log(
                    $"Activated HotUpdate release '{_active.Release.ReleaseId}' using {_active.RuntimeKind}.");
            }
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
        }
    }

    internal BValueTask DisposeAsync()
    {
        _disposeOperation ??= DisposeCoreAsync();
        return new BValueTask(_disposeOperation);
    }

    private async BTask DisposeCoreAsync()
    {
        if (_disposed) return;
        _disposed = true;
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
            PlayerMainThread.Complete(operation);
            return;
        }
        _ = ObserveDisposalAsync(operation);
    }

    private static async BValueTask ObserveDisposalAsync(BTask operation)
    {
        try { await operation; }
        catch (Exception exception)
        {
            PlayerStartupDiagnostics.Failure("HOTUPDATE_RUNTIME_STOP", exception);
        }
    }
}
