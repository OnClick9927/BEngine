using BEngine.Networking;
using BEngine.Platform;

namespace BEngine.AssetBundles;

public sealed class AssetBundleManagerFactory : IAssetBundleManagerFactory
{
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly IHttpNetworkRequestFactory _httpRequestFactory;

    public AssetBundleManagerFactory(
        FileUtility fileSystem,
        PathUtility pathService,
        IHttpNetworkRequestFactory httpRequestFactory)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _httpRequestFactory = httpRequestFactory ??
                              throw new ArgumentNullException(nameof(httpRequestFactory));
    }

    public ITransactionalAssetBundleManager Create(AssetBundleRuntimeOptions options) =>
        new AssetBundleManager(
            options ?? throw new ArgumentNullException(nameof(options)),
            _fileSystem,
            _pathService,
            _httpRequestFactory);
}
