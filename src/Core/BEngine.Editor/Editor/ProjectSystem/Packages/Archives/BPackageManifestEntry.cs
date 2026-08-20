namespace BEngine.Editor;

public sealed class BPackageManifestEntry
{
    public string RelativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
    public bool IsDirectory { get; set; }
}
