using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class ImportedAssetResolutionBridge
{
    [ModuleInitializer]
    internal static void Register() => ImportedAssetResolver.ResolveArtifactPath = Resolve;

    private static string? Resolve(string source, Type assetType)
    {
        var host = EditorBridge.Host;
        if (host is null || string.IsNullOrWhiteSpace(source)) return null;

        EditorAssetRecord? record = null;
        if (source.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(source.AsSpan("guid:".Length), out var guid))
            record = host.GetAsset(guid);
        else
            record = host.GetAsset(source);

        if (record is null && Path.IsPathRooted(source))
        {
            var fullSource = Path.GetFullPath(source);
            record = host.FindAssets(string.Empty).FirstOrDefault(candidate =>
                !candidate.IsSubAsset &&
                Path.GetFullPath(candidate.SourcePath).Equals(fullSource, StringComparison.OrdinalIgnoreCase));
            if (record.Value.Guid == Guid.Empty) record = null;
        }

        if (record is not { IsSubAsset: false } imported ||
            AssetTypeRegistry.ResolveAssetType(imported.SourcePath) is not { } registeredType ||
            !assetType.IsAssignableFrom(registeredType) ||
            !EditorHostServices.FileSystem.FileExists(imported.ArtifactPath)) return null;
        return imported.ArtifactPath;
    }
}
