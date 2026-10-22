using BEngine.AssetBundles;
using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.Editor;

internal sealed class EditorVirtualContentBootstrapper : IEditorVirtualContentSession
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

    public IAssetBundleManager AssetBundles => _manager;

    internal static EditorVirtualContentBootstrapper Create(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        EditorVirtualAssetBundleBuilder builder,
        CancellationToken cancellationToken = default) =>
        new(builder.BuildSnapshot(workspace, assetDatabase, cancellationToken));

    public async BValueTask PrepareAsync(
        BCancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
        {
            await _manager.InitializeAsync(cancellationToken);
            Resources.RegisterResourceProvider(_provider);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (Volatile.Read(ref _initialized) != 0) Resources.UnregisterResourceProvider(_provider);
        _manager.Dispose();
    }
}
