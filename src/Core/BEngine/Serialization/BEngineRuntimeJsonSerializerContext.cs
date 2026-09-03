using System.Text.Json.Serialization;
using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.HotUpdate;

namespace BEngine.Serialization;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(PlayerBootstrapManifest))]
[JsonSerializable(typeof(BuildTargetManifest))]
[JsonSerializable(typeof(AssetBundleCatalog))]
[JsonSerializable(typeof(AssetBundleVersion))]
[JsonSerializable(typeof(AssetBundleLatestPointer))]
[JsonSerializable(typeof(ManagedCodeReleaseManifest))]
internal sealed partial class BEngineRuntimeJsonSerializerContext : JsonSerializerContext;
