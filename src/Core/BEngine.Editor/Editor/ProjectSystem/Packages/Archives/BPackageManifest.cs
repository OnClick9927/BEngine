using BEngine.Documents;
using YamlDotNet.Serialization;

namespace BEngine.Editor;

public sealed class BPackageManifest : Document
{
    public string Format { get; set; } = "BEngine.BPackage";
    public int Version { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = "1.0.0";
    public string Description { get; set; } = string.Empty;
    public string DefaultImportPath { get; set; } = string.Empty;
    public List<BPackageManifestEntry> Entries { get; set; } = [];

    [YamlIgnore]
    public int FileCount => Entries?.Count(entry => !entry.IsDirectory) ?? 0;

    [YamlIgnore]
    public long TotalSize => Entries?.Where(entry => !entry.IsDirectory).Sum(entry => entry.Size) ?? 0;
}
