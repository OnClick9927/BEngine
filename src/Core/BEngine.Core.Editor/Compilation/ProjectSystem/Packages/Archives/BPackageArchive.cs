using BEngine.ProjectSystem;

namespace BEngine.Editor;

/// <summary>Package archive extension entry points provided by editor tooling.</summary>
public static class BPackageArchive
{
    public static BPackageManifest ExportPackage(
        IEnumerable<string> paths,
        string destination,
        BPackageExportOptions? options = null,
        Action<BPackageProgress>? progress = null) =>
        EditorServiceAccess.GetRequiredService<IBPackageArchiveService>()
            .ExportPackage(paths, destination, options, progress);

    public static BPackageManifest ExportPackage(
        ProjectWorkspace workspace,
        IEnumerable<string> paths,
        string destination,
        BPackageExportOptions? options = null,
        Action<BPackageProgress>? progress = null) =>
        EditorServiceAccess.GetRequiredService<IBPackageArchiveService>()
            .ExportPackage(workspace, paths, destination, options, progress);

    public static BPackageManifest ReadManifest(string archivePath) =>
        EditorServiceAccess.GetRequiredService<IBPackageArchiveService>().ReadManifest(archivePath);

    public static BPackageImportReceipt? ReadImportReceipt(
        ProjectWorkspace workspace,
        string installationId) =>
        EditorServiceAccess.GetRequiredService<IBPackageArchiveService>()
            .ReadImportReceipt(workspace, installationId);

    public static BPackageImportStatus GetImportStatus(
        ProjectWorkspace workspace,
        string archivePath,
        string installationId,
        string? destinationDirectory = null) =>
        EditorServiceAccess.GetRequiredService<IBPackageArchiveService>()
            .GetImportStatus(workspace, archivePath, installationId, destinationDirectory);

    public static BPackageImportResult ImportPackage(
        ProjectWorkspace workspace,
        string archivePath,
        BPackageImportOptions? options = null,
        Action<BPackageProgress>? progress = null) =>
        EditorServiceAccess.GetRequiredService<IBPackageArchiveService>()
            .ImportPackage(workspace, archivePath, options, progress);
}
