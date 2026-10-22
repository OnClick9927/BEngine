using YamlDotNet.Serialization;

namespace BEngine.AssetBundles;

public sealed class AssetBundleLatestPointer
{
    // These members are retained as ignored compatibility surface for callers that
    // constructed the former four-field pointer. New latest.yaml files intentionally
    // contain only the mutable version selector.
    public const string CurrentFormat = "BEngine.AssetBundleLatest";
    public const int CurrentSchemaVersion = 1;

    [YamlIgnore] public string Format { get; set; } = CurrentFormat;
    [YamlIgnore] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    [YamlIgnore] public string PackageName { get; set; } = string.Empty;
    [YamlMember(Order = 0)] public string Version { get; set; } = string.Empty;

    public void Validate() => AssetBundleValidation.ValidateLatestPointer(this);
}
