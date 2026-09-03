using System.Text.Json.Serialization;

namespace BEngine.AssetBundles;

public sealed class AssetBundleLatestPointer
{
    // These members are retained as ignored compatibility surface for callers that
    // constructed the former four-field pointer. New latest.json files intentionally
    // contain only the mutable version selector.
    public const string CurrentFormat = "BEngine.AssetBundleLatest";
    public const int CurrentSchemaVersion = 1;

    [JsonIgnore] public string Format { get; set; } = CurrentFormat;
    [JsonIgnore] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    [JsonIgnore] public string PackageName { get; set; } = string.Empty;
    [JsonPropertyOrder(0)] public string Version { get; set; } = string.Empty;

    public void Validate() => AssetBundleValidation.ValidateLatestPointer(this);
}
