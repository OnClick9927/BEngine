using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.Editor;

/// <summary>
/// DI-owned package archive entry point. Static <see cref="BPackageArchive"/> methods are
/// compatibility facades and resolve this service from the active Editor container.
/// </summary>
public sealed class BPackageArchiveService : IBPackageArchiveService
{
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly PlatformUtility _platformService;
    private readonly WaitUtility _waitService;

    public BPackageArchiveService(
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        WaitUtility waitService)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        _waitService = waitService ?? throw new ArgumentNullException(nameof(waitService));
    }

    public BPackageManifest ExportPackage(
        IEnumerable<string> paths,
        string destination,
        BPackageExportOptions? options = null,
        Action<BPackageProgress>? progress = null) =>
        BPackageArchive.ExportPackage(
            paths, destination, options, progress, CreateServices());

    public BPackageManifest ExportPackage(
        ProjectWorkspace workspace,
        IEnumerable<string> paths,
        string destination,
        BPackageExportOptions? options = null,
        Action<BPackageProgress>? progress = null) =>
        BPackageArchive.ExportPackage(
            workspace, paths, destination, options, progress, CreateServices());

    public BPackageManifest ReadManifest(string archivePath) =>
        BPackageArchive.ReadManifest(archivePath, CreateServices());

    public BPackageImportReceipt? ReadImportReceipt(
        ProjectWorkspace workspace,
        string installationId) =>
        BPackageArchive.ReadImportReceipt(workspace, installationId, CreateServices());

    public BPackageImportStatus GetImportStatus(
        ProjectWorkspace workspace,
        string archivePath,
        string installationId,
        string? destinationDirectory = null) =>
        BPackageArchive.GetImportStatus(
            workspace, archivePath, installationId, destinationDirectory, CreateServices());

    public BPackageImportResult ImportPackage(
        ProjectWorkspace workspace,
        string archivePath,
        BPackageImportOptions? options = null,
        Action<BPackageProgress>? progress = null) =>
        BPackageArchive.ImportPackage(
            workspace, archivePath, options, progress, CreateServices());

    private BPackageArchiveServices CreateServices() =>
        new(_fileSystem, _pathService, _platformService, _waitService);
}
