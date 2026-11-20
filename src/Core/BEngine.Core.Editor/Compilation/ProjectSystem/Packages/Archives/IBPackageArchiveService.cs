using BEngine.ProjectSystem;

namespace BEngine.Editor;

public interface IBPackageArchiveService
{
    BPackageManifest ExportPackage(
        IEnumerable<string> paths,
        string destination,
        BPackageExportOptions? options = null,
        Action<BPackageProgress>? progress = null);

    BPackageManifest ExportPackage(
        ProjectWorkspace workspace,
        IEnumerable<string> paths,
        string destination,
        BPackageExportOptions? options = null,
        Action<BPackageProgress>? progress = null);

    BPackageManifest ReadManifest(string archivePath);

    BPackageImportReceipt? ReadImportReceipt(
        ProjectWorkspace workspace,
        string installationId);

    BPackageImportStatus GetImportStatus(
        ProjectWorkspace workspace,
        string archivePath,
        string installationId,
        string? destinationDirectory = null);

    BPackageImportResult ImportPackage(
        ProjectWorkspace workspace,
        string archivePath,
        BPackageImportOptions? options = null,
        Action<BPackageProgress>? progress = null);
}
