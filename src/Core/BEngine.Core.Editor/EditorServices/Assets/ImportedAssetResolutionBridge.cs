using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal sealed class ImportedAssetResolutionBridge : IAssetArtifactResolver
{
    [ModuleInitializer]
    internal static void Register() => DependencyInjection.RuntimeServiceContributions.RegisterDefault<IAssetArtifactResolver>(new ImportedAssetResolutionBridge());

    public string? Resolve(string source, Type assetType)
    {
        var host = EditorBridge.Host;
        if (host is null || string.IsNullOrWhiteSpace(source)) return null;

        EditorAssetInfo? record = null;
        if (source.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(source.AsSpan("guid:".Length), out var guid))
            record = host.GetAsset(guid);
        else
            record = host.GetAsset(source);

        if (record is null && Path.IsPathRooted(source))
        {
            var fullSource = Path.GetFullPath(source);
            record = Matches860();
            [global::BEngine.SkipLoopGuard]
            global::BEngine.Editor.EditorAssetInfo Matches860()
            {
                var matchSource860 = host.FindAssets(string.Empty);
                ArgumentNullException.ThrowIfNull(matchSource860, "source");
                global::System.ReadOnlySpan<global::BEngine.Editor.EditorAssetInfo> matchValues860 = matchSource860;
                foreach (var candidate in matchValues860)
                {
                    if (!candidate.IsSubAsset && Path.GetFullPath(candidate.SourcePath).Equals(fullSource, StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            
                return default!;
            }
            if (record.Value.Guid == Guid.Empty) record = null;
        }

        if (record is not { IsSubAsset: false } imported ||
            AssetTypeRegistry.ResolveAssetType(imported.SourcePath) is not { } registeredType ||
            !assetType.IsAssignableFrom(registeredType) ||
            !EditorCapabilities.GetRequiredService<FileUtility>().FileExists(imported.ArtifactPath)) return null;
        return imported.ArtifactPath;
    }
}
