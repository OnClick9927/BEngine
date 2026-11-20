using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
namespace BEngine.Editor;

public sealed class PackageExampleInfo
{
    public PackageExampleInfo(string archivePath, BPackageManifest? manifest, string error)
    {
        ArchivePath = archivePath;
        Manifest = manifest;
        Error = error;
    }

    public string ArchivePath { get; }
    public BPackageManifest? Manifest { get; }
    public string Error { get; }
    public string DisplayName => string.IsNullOrWhiteSpace(Manifest?.Name)
        ? EditorCapabilities.GetRequiredService<PathUtility>().GetFileNameWithoutExtension(ArchivePath)
        : Manifest.Name;
    public string ImportPath => Manifest is null
        ? string.Empty
        : PackageExampleLayout.GetImportPath(Manifest, EditorCapabilities.GetRequiredService<PathUtility>().GetFileNameWithoutExtension(ArchivePath));
    public long ArchiveSize => EditorCapabilities.GetRequiredService<FileUtility>().FileExists(ArchivePath)
        ? EditorCapabilities.GetRequiredService<FileUtility>().GetFileLength(ArchivePath)
        : 0;
}
