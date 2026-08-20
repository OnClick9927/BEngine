namespace BEngine.Editor;

internal sealed class PackageExampleInfo
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
        ? Path.GetFileNameWithoutExtension(ArchivePath)
        : Manifest.Name;
    public string ImportPath => Manifest is null
        ? string.Empty
        : PackageExampleLayout.GetImportPath(Manifest, Path.GetFileNameWithoutExtension(ArchivePath));
    public long ArchiveSize => File.Exists(ArchivePath) ? new FileInfo(ArchivePath).Length : 0;
}
