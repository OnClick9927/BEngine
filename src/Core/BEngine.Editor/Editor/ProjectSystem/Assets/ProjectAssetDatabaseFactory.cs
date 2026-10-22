using BEngine.Platform;

namespace BEngine.ProjectSystem.Editor;

public sealed class ProjectAssetDatabaseFactory : IProjectAssetDatabaseFactory
{
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly PlatformUtility _platformService;

    public ProjectAssetDatabaseFactory(
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
    }

    public AssetDatabase Create(ProjectWorkspace workspace) =>
        new(workspace, _fileSystem, _pathService, _platformService);
}
