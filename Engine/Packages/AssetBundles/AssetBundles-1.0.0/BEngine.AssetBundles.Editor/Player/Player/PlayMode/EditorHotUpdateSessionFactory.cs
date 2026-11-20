using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.AssetBundles;
using BEngine.Player.HotUpdate;
using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.Editor.Build;

public sealed class EditorHotUpdateSessionFactory : IEditorHotUpdateSessionFactory
{
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly PlatformUtility _platformService;
    private readonly ApplicationPathUtility _applicationPaths;
    private readonly IManagedCodeRuntimeFactory _managedCodeRuntimeFactory;

    public EditorHotUpdateSessionFactory(
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        ApplicationPathUtility applicationPaths,
        IManagedCodeRuntimeFactory managedCodeRuntimeFactory)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        _applicationPaths = applicationPaths ?? throw new ArgumentNullException(nameof(applicationPaths));
        _managedCodeRuntimeFactory = managedCodeRuntimeFactory ??
                                     throw new ArgumentNullException(nameof(managedCodeRuntimeFactory));
    }

    public EditorHotUpdateSessionFactory() : this(
        EditorCapabilities.GetRequiredService<FileUtility>(),
        EditorCapabilities.GetRequiredService<PathUtility>(),
        EditorCapabilities.GetRequiredService<PlatformUtility>(),
        EditorCapabilities.GetRequiredService<ApplicationPathUtility>(),
        EditorCapabilities.GetRequiredService<IManagedCodeRuntimeFactory>()) { }

    public async BValueTask<IEditorHotUpdateSession?> CreateAsync(
        ProjectWorkspace workspace,
        IAssetBundleManager assetBundles,
        BCancellationToken cancellationToken = default) =>
        await EditorHotUpdateSession.CreateAsync(
            workspace, assetBundles, _fileSystem, _pathService,
            _platformService, _applicationPaths, _managedCodeRuntimeFactory,
            cancellationToken);
}
