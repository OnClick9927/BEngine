namespace BEngine.Editor.Build;

/// <summary>Content configuration owned by the AssetBundles provider, independent of platform publishing.</summary>
public static class AssetBundlePlayerBuildOptions
{
    private const string CompressionKey = "com.bengine.asset-bundles.compress";
    private const string LegacyCompressionKey = "legacy.compressAssetBundles";

    public static bool GetCompression(IReadOnlyDictionary<string, string>? options)
    {
        if (options is null) return true;
        if (!options.TryGetValue(CompressionKey, out var value) &&
            !options.TryGetValue(LegacyCompressionKey, out value)) return true;
        if (bool.TryParse(value, out var enabled)) return enabled;
        throw new InvalidDataException("AssetBundle compression must be true or false.");
    }

    public static void SetCompression(PlayerBuildSettings settings, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ContentOptions ??= new(StringComparer.Ordinal);
        settings.ContentOptions.Remove(LegacyCompressionKey);
        settings.ContentOptions[CompressionKey] = enabled ? "true" : "false";
    }
}
