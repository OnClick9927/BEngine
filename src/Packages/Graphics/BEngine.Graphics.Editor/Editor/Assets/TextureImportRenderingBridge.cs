using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
using System.Runtime.CompilerServices;
using BEngine.Rendering;

namespace BEngine.Editor;

internal static class TextureImportRenderingBridge
{
    [ModuleInitializer]
    internal static void Register() => TextureImportResolver.Resolve = source =>
    {
        var host = EditorBridge.Host;
        if (host is null) return null;
        var record = source.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(source.AsSpan(5), out var guid)
            ? host.GetAsset(guid) : host.GetAsset(source);
        if (record is null || record.Value.AssetType != nameof(Texture) ||
            !EditorCapabilities.GetRequiredService<FileUtility>().FileExists(record.Value.ArtifactPath)) return null;
        var settings = GraphicsAssetLoading.ReadTextureSamplingSettings(record.Value.SourcePath);
        return new ImportedTextureSource(record.Value.ArtifactPath, settings.FilterMode, settings.WrapMode);
    };
}
