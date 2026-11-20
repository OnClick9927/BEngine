using System.Collections;
using System.Reflection;

namespace BEngine.Serialization;

/// <summary>Compact published Scene data; editable source and imported artifacts remain YAML.</summary>
internal static class SceneRuntimeDataSerializer
{
    internal static SceneAssetData Deserialize(ReadOnlySpan<byte> bytes) =>
        BinaryDataSerializer.IsBinaryData(bytes)
            ? BinaryDataSerializer.Deserialize<SceneAssetData>(bytes)
            : YamlUtility.Deserialize<SceneAssetData>(bytes);

}
