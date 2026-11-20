using BEngine.AssetBundles;

namespace BEngine.Editor.Build;

internal sealed class EditorRealContentSession(AssetBundleManager manager) : IEditorVirtualContentSession
{
    private readonly EditorRealResourceProvider _provider = new(manager);
    private bool _registered;
    private bool _disposed;
    public IAssetBundleManager AssetBundles => manager;

    public async BValueTask PrepareAsync(BCancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_registered) return;
        await manager.InitializeAsync(cancellationToken);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        Resources.RegisterResourceProvider(_provider);
        _registered = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_registered) { Resources.UnregisterResourceProvider(_provider); _registered = false; }
        manager.Dispose();
    }

    // A catalog-owned failure must not be hidden by a same-named source file in the Editor project.
    // Resources absent from this catalog remain available from the ordinary main-package providers.
    private sealed class EditorRealResourceProvider(AssetBundleManager manager) : IAsyncResourceProvider,
        IAsyncResourceReferenceProvider, IResourceAssetProvider, IResourceObjectProvider
    {
        private readonly AssetBundleResourceProvider _inner = new(manager);
        public bool ContainsReference(string path, string folderName) => _inner.ContainsReference(path, folderName);
        public BTask<bool> PrepareReferenceAsync(string path, string folderName, AsyncLoadContext context) =>
            _inner.PrepareReferenceAsync(path, folderName, context);
        public BTask<ResourceLoadResult> LoadAsync(string path, string folderName, Type type, AsyncLoadContext context) =>
            _inner.LoadAsync(path, folderName, type, context);
        public IEnumerable<string> Enumerate(string path, string folderName) => _inner.Enumerate(path, folderName);
        public bool TryLoad(string path, string folderName, out ResourceContent content)
        {
            if (_inner.TryLoad(path, folderName, out content)) return true;
            ThrowIfOwned(path, folderName);
            return false;
        }
        public bool TryLoadAsset(string path, string folderName, Type assetType, out BAsset asset)
        {
            if (_inner.TryLoadAsset(path, folderName, assetType, out asset)) return true;
            if (typeof(BAsset).IsAssignableFrom(assetType)) ThrowIfOwned(path, folderName);
            return false;
        }
        public bool TryLoadObject(string path, string folderName, Type objectType, out BObject value)
        {
            if (_inner.TryLoadObject(path, folderName, objectType, out value)) return true;
            if (objectType == typeof(Sprite)) ThrowIfOwned(path, folderName);
            return false;
        }
        private void ThrowIfOwned(string path, string folderName)
        {
            if (_inner.ContainsReference(path, folderName))
                throw new InvalidDataException($"Real Editor AssetBundle resource '{path}' could not be read or decoded. Rebuild or repair the selected AB release.");
        }
    }
}
