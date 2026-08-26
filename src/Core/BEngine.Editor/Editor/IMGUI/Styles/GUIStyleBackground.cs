namespace BEngine.Editor;

internal static class GUIStyleBackground
{
    internal const string SegmentedButtonReference = "builtin://gui-style/segmented-button";

    internal static Texture SegmentedButton { get; } = CreateBuiltIn(SegmentedButtonReference,
        "Segmented Button");

    internal static bool IsSegmentedButton(Texture? texture) =>
        ReferenceEquals(texture, SegmentedButton) ||
        ToReference(texture).Equals(SegmentedButtonReference, StringComparison.Ordinal);

    internal static string ToReference(Texture? texture)
    {
        if (texture is null) return string.Empty;
        if (ReferenceEquals(texture, SegmentedButton)) return SegmentedButtonReference;
        if (!string.IsNullOrWhiteSpace(texture.assetPath)) return texture.assetPath.Replace('\\', '/');
        return texture.sourcePath.Replace('\\', '/');
    }

    internal static string ToRenderSource(Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return !string.IsNullOrWhiteSpace(texture.sourcePath)
            ? texture.sourcePath
            : ToReference(texture);
    }

    internal static Texture? FromReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var normalized = reference.Trim().Replace('\\', '/');
        if (normalized.Equals(SegmentedButtonReference, StringComparison.Ordinal)) return SegmentedButton;

        try
        {
            if (AssetDatabase.LoadAssetAtPath<Texture>(normalized) is { } asset) return asset;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
                                           InvalidOperationException or ArgumentException)
        {
            // GUISkin assets can also be loaded without an open editor project.
        }

        try
        {
            if (BAsset.Load<Texture>(normalized) is { } asset) return asset;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
                                           NotSupportedException or ArgumentException)
        {
            // Keep unresolved project references intact for the next editor session.
        }

        var texture = new Texture { name = Path.GetFileName(normalized), assetType = nameof(Texture) };
        texture.BindAssetReference(normalized);
        if (Path.IsPathRooted(normalized)) texture.sourcePath = Path.GetFullPath(normalized);
        return texture;
    }

    private static Texture CreateBuiltIn(string reference, string displayName)
    {
        var texture = new Texture
        {
            name = displayName,
            assetType = nameof(Texture),
            hideFlags = HideFlags.NotEditable | HideFlags.DontSaveInEditor
        };
        texture.BindAssetReference(reference);
        return texture;
    }
}
