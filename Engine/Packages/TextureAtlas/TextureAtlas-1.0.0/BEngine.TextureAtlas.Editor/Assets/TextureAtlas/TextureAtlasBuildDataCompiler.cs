using System.Text;
using YamlDotNet.Core;

namespace BEngine.Editor;

/// <summary>Compiles atlas publication payloads without changing authoring or imported data.</summary>
public static class TextureAtlasBuildDataCompiler
{
    public static byte[] Compile(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var original = TextureAtlasRuntimeData.Deserialize(bytes.Span);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TextureAtlasRuntimeData.IsRuntimeData(bytes.Span) ||
                !YamlUtility.TryGetExclusiveConverterVersion(TextureAtlasRegistration.Converter, out var version))
                return bytes.ToArray();
            try
            {
                // Preserve original DTO defaults/legacy fields. FromAsset would
                // capture a random generated Id when the source omitted it and
                // cannot round-trip legacy path-only Sources.
                var dto = YamlUtility.Deserialize<TextureAtlasYamlData>(Encoding.UTF8.GetString(bytes.Span));
                var canonical = YamlUtility.SerializeToUtf8Bytes(dto);
                cancellationToken.ThrowIfCancellationRequested();
                var binary = BinaryDataSerializer.CompileYaml(canonical, cancellationToken);
                var restored = BinaryDataSerializer.Deserialize<TextureAtlasYamlData>(binary);
                cancellationToken.ThrowIfCancellationRequested();
                if (SameData(dto, restored) &&
                    YamlUtility.TryGetExclusiveConverterVersion(TextureAtlasRegistration.Converter, out var after) && after == version)
                {
                    var validated = TextureAtlasRuntimeData.CreateValidated(restored);
                    BObject.DestroyImmediate(validated);
                    return TextureAtlasRuntimeData.WriteEnvelope(binary, bytes.Span);
                }
            }
            catch (Exception error) when (error is InvalidDataException or YamlException or
                                          NotSupportedException or ArgumentException or InvalidOperationException)
            {
                // A source accepted by the ordinary reader may use a custom
                // conversion or valid construct outside the compact DTO format.
            }
            cancellationToken.ThrowIfCancellationRequested();
            return bytes.ToArray();
        }
        finally { BObject.DestroyImmediate(original); }
    }

    // Build-only closed DTO comparison. No Type, converter, Assembly or mutable
    // authoring object is cached by the runtime format.
    private static bool SameData(TextureAtlasYamlData a, TextureAtlasYamlData b) =>
        a.Format == b.Format && a.Texture == b.Texture && a.Width == b.Width && a.Height == b.Height &&
        a.MaxSize == b.MaxSize && a.Padding == b.Padding && a.Extrude == b.Extrude &&
        a.Id == b.Id && a.Name == b.Name && a.HideFlags == b.HideFlags && a.Version == b.Version &&
        SameList(a.SpriteReferences, b.SpriteReferences, static (left, right) => left == right) &&
        SameList(a.Sprites, b.Sprites, static (left, right) => left is null || right is null ? ReferenceEquals(left, right) :
            left.Name == right.Name && left.Source == right.Source && left.X == right.X && left.Y == right.Y &&
            left.Width == right.Width && left.Height == right.Height && left.PivotX == right.PivotX && left.PivotY == right.PivotY) &&
        SameList(a.Sources, b.Sources, static (left, right) => left is null || right is null ? ReferenceEquals(left, right) :
            left.OwnerGuid == right.OwnerGuid && left.LocalIdentifier == right.LocalIdentifier &&
            left.Texture == right.Texture && left.Path == right.Path && left.AssetPath == right.AssetPath &&
            left.Name == right.Name && left.PivotX == right.PivotX && left.PivotY == right.PivotY);

    private static bool SameList<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b, Func<T, T, bool> same)
    {
        if (a is null || b is null) return ReferenceEquals(a, b);
        if (a.Count != b.Count) return false;
        for (var index = 0; index < a.Count; index++) if (!same(a[index], b[index])) return false;
        return true;
    }
}
