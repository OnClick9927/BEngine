using BEngine.AssetBundles;
using BEngine.Content;
using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.Editor;

internal sealed class EditorVirtualContentBootstrapper : IContentBootstrapper, IDisposable
{
    private readonly VirtualAssetBundleManager _manager;
    private readonly AssetBundleResourceProvider _provider;
    private int _initialized;
    private int _disposed;

    private EditorVirtualContentBootstrapper(VirtualAssetBundleSnapshot snapshot)
    {
        _manager = new VirtualAssetBundleManager(snapshot);
        _provider = new AssetBundleResourceProvider(_manager);
    }

    internal IAssetBundleManager AssetBundles => _manager;

    internal static EditorVirtualContentBootstrapper Create(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        CancellationToken cancellationToken = default) =>
        new(EditorVirtualAssetBundleBuilder.Build(workspace, assetDatabase, cancellationToken));

    public async BValueTask<ActivatedContentRelease> PrepareAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
        {
            await _manager.InitializeAsync(cancellationToken).ConfigureAwait(false);
            Resources.RegisterResourceProvider(_provider);
        }
        return new ActivatedContentRelease(
            _manager.ActiveVersion!.Version,
            ContentEnvironmentKind.EditorVirtual,
            _manager,
            false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (Volatile.Read(ref _initialized) != 0) Resources.UnregisterResourceProvider(_provider);
        _manager.Dispose();
    }
}
