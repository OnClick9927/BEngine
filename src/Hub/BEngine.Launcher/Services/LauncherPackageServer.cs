using System.IO.Pipes;
using BEngine.ProjectSystem;
using BEngine.Tooling;

namespace BEngine.Launcher;

internal sealed class LauncherPackageServer : IDisposable
{
    private readonly LauncherPackageCatalog _catalog;
    private readonly string _root;
    private readonly CancellationTokenSource _stop = new();
    private readonly Mutex _ownership;
    private readonly bool _owner;
    private readonly Task? _listener;

    internal LauncherPackageServer(LauncherPackageCatalog catalog, string root)
    {
        _catalog = catalog;
        _root = Path.GetFullPath(root);
        var name = PackageCatalogTransport.PipeName(root);
        _ownership = new Mutex(false, name + ".Owner");
        try { _owner = _ownership.WaitOne(0); }
        catch (AbandonedMutexException) { _owner = true; }
        if (_owner) _listener = ListenAsync(name);
    }

    internal bool IsOwner => _owner;

    private async Task ListenAsync(string name)
    {
        var clients = new List<Task>();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                clients.RemoveAll(static task => task.IsCompleted);
                if (clients.Count >= 7)
                {
                    await Task.WhenAny(clients).ConfigureAwait(false);
                    continue;
                }
                var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 8,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try { await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false); }
                catch { pipe.Dispose(); throw; }
                clients.Add(ServeAsync(pipe));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { await Task.WhenAll(clients).ConfigureAwait(false); }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                PackageCatalogResponse response;
                try
                {
                    var request = YamlUtility.Deserialize<PackageCatalogRequest>(
                        await PackageCatalogTransport.ReadAsync(pipe, timeout.Token).ConfigureAwait(false));
                    if (request.ProtocolVersion != 1) throw new InvalidDataException("Unsupported package catalog protocol. Update Hub and Editor together.");
                    var snapshot = _catalog.GetSnapshot(_root, request.EngineVersion);
                    response = request.Revision == snapshot.Revision
                        ? new PackageCatalogResponse { NotModified = true }
                        : new PackageCatalogResponse { Snapshot = snapshot };
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
                { response = new PackageCatalogResponse { Error = exception.Message }; }
                await PackageCatalogTransport.WriteAsync(pipe, YamlUtility.Serialize(response), timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException)
            { System.Diagnostics.Trace.WriteLine(exception.Message); }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener?.GetAwaiter().GetResult(); }
        finally
        {
            if (_owner) _ownership.ReleaseMutex();
            _ownership.Dispose();
            _stop.Dispose();
        }
    }
}
