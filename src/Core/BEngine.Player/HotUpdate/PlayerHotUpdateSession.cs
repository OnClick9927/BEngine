using BEngine.HotUpdate;
using System.Reflection;

namespace BEngine.Player;

internal sealed class PlayerHotUpdateSession : IDisposable
{
    private readonly PlayerManagedCodeStage _stage;
    private PreparedHotUpdateDomain? _prepared;
    private HotUpdateDomain? _active;
    private Assembly[] _assemblies;
    private int _disposed;

    internal PlayerHotUpdateSession(
        PreparedHotUpdateDomain prepared,
        PlayerManagedCodeStage stage = PlayerManagedCodeStage.HotUpdate)
    {
        _prepared = prepared ?? throw new ArgumentNullException(nameof(prepared));
        _stage = stage;
        _assemblies = prepared.Assemblies.ToArray();
    }

    internal IReadOnlyList<Assembly> Assemblies => Array.AsReadOnly(_assemblies);

    internal void Activate(IServiceProvider hostServices)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_active is not null) return;
        var prepared = Interlocked.Exchange(ref _prepared, null) ??
                       throw new InvalidOperationException("The HotUpdate session has no prepared domain.");
        try
        {
            _active = prepared.ActivateAsync(hostServices).AsTask()
                .ConfigureAwait(false).GetAwaiter().GetResult();
            if (_stage == PlayerManagedCodeStage.Aot)
            {
                PlayerStartupDiagnostics.Phase("03_AOT_ASSEMBLY_ACTIVATED",
                    $"release={_active.Release.ReleaseId};runtime={_active.RuntimeKind};assemblies={_assemblies.Length}");
                Debug.Log($"Activated AOT startup release '{_active.Release.ReleaseId}' using {_active.RuntimeKind}.");
            }
            else
            {
                PlayerStartupDiagnostics.Phase("05_HOTUPDATE_INJECTED",
                    $"release={_active.Release.ReleaseId};runtime={_active.RuntimeKind};assemblies={_assemblies.Length}");
                Debug.Log($"Activated HotUpdate release '{_active.Release.ReleaseId}' using {_active.RuntimeKind}.");
            }
        }
        catch
        {
            prepared.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            _assemblies = [];
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _active?.Dispose();
        _active = null;
        var prepared = Interlocked.Exchange(ref _prepared, null);
        prepared?.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
        _assemblies = [];
    }
}
