using YamlDotNet.Serialization;

namespace BEngine.AssetBundles;

public sealed class AssetBundleDescriptor
{
    [YamlMember(Order = 0)] public string Name { get; set; } = string.Empty;
    [YamlMember(Order = 1)] public string FileName { get; set; } = string.Empty;
    [YamlMember(Order = 2)] public string Sha256 { get; set; } = string.Empty;
    [YamlMember(Order = 3)] public long Size { get; set; }
    [YamlMember(Order = 4)] public List<string> Dependencies { get; set; } = [];
    /// <summary>The immutable release containing this object. Empty uses the catalog version.</summary>
    [YamlMember(Order = 5)] public string SourceVersion { get; set; } = string.Empty;
}
