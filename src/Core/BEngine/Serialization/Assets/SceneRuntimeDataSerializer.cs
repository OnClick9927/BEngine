using System.Collections;
using System.Reflection;
using YamlDotNet.Core;

namespace BEngine.Serialization;

/// <summary>Compact published Scene data; editable source and imported artifacts remain YAML.</summary>
internal static class SceneRuntimeDataSerializer
{
    internal static SceneAssetData Deserialize(ReadOnlySpan<byte> bytes) =>
        BinaryDataSerializer.IsBinaryData(bytes)
            ? BinaryDataSerializer.Deserialize<SceneAssetData>(bytes)
            : YamlUtility.Deserialize<SceneAssetData>(bytes);

    internal static byte[] CompileRuntimeBytes(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = Deserialize(bytes);
        AssetDataValidation.ValidateScene(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (BinaryDataSerializer.IsBinaryData(bytes)) return bytes.ToArray();
        try
        {
            // Normalize aliases, tags, defaults and ignored members through the same typed
            // reader as the Player. Scene references are explicit IDs, not YAML aliases.
            var canonical = YamlUtility.SerializeToUtf8Bytes(document);
            cancellationToken.ThrowIfCancellationRequested();
            var compiled = BinaryDataSerializer.CompileYaml(canonical, cancellationToken);
            var restored = BinaryDataSerializer.Deserialize<SceneAssetData>(compiled);
            cancellationToken.ThrowIfCancellationRequested();
            if (SameData(document, restored, 0)) return compiled;
        }
        catch (Exception error) when (error is InvalidDataException or YamlException or
                                      NotSupportedException or ArgumentException or InvalidOperationException)
        {
            // A valid source may use a custom YAML conversion or another construct outside
            // the compact format. Preserve its original bytes and ordinary runtime reader.
        }
        cancellationToken.ThrowIfCancellationRequested();
        return bytes.ToArray();
    }

    // Build-only equivalence check over the closed Scene DTO graph. Do not retain Type,
    // PropertyInfo or a custom converter from a project/collectible assembly in a cache.
    private static bool SameData(object? left, object? right, int depth)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.GetType() != right.GetType() || depth > 256) return false;
        var type = left.GetType();
        if (type.IsValueType || left is string) return left.Equals(right);
        if (left is IDictionary dictionary && right is IDictionary otherDictionary)
        {
            if (dictionary.Count != otherDictionary.Count) return false;
            foreach (DictionaryEntry entry in dictionary)
                if (!otherDictionary.Contains(entry.Key) || !SameData(entry.Value, otherDictionary[entry.Key], depth + 1)) return false;
            return true;
        }
        if (left is IList list && right is IList otherList)
        {
            if (list.Count != otherList.Count) return false;
            for (var index = 0; index < list.Count; index++)
                if (!SameData(list[index], otherList[index], depth + 1)) return false;
            return true;
        }
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            if (property.GetMethod is not null && property.GetIndexParameters().Length == 0 &&
                !SameData(property.GetValue(left), property.GetValue(right), depth + 1)) return false;
        return true;
    }
}
