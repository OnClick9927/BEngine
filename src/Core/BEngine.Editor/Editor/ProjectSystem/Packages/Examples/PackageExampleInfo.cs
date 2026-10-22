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
        ? EditorHostServices.Paths.GetFileNameWithoutExtension(ArchivePath)
        : Manifest.Name;
    public string ImportPath => Manifest is null
        ? string.Empty
        : PackageExampleLayout.GetImportPath(Manifest, EditorHostServices.Paths.GetFileNameWithoutExtension(ArchivePath));
    public long ArchiveSize => EditorHostServices.FileSystem.FileExists(ArchivePath)
        ? EditorHostServices.FileSystem.GetFileLength(ArchivePath)
        : 0;
}
