using System.Diagnostics;
using BEngine.Serialization;

namespace BEngine.Editor;

public class DefaultAsset : BAsset
{
    public string packageId { get; internal set; } = string.Empty;
    public string packageVersion { get; internal set; } = string.Empty;

    /// <summary>Creates an unsaved, read-only browser reference. Does not import or read the
    /// source, assign a persistent identity, or insert it into the loaded-asset cache.</summary>
    public static DefaultAsset CreateReadOnlyReference(string displayName, string address,
        string sourcePath, string assetType, string? packageId = null, string? packageVersion = null)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(assetType);
        var asset = new DefaultAsset
        {
            name = displayName,
            packageId = packageId ?? string.Empty,
            packageVersion = packageVersion ?? string.Empty,
            hideFlags = HideFlags.NotEditable | HideFlags.DontSaveInEditor
        };
        RuntimeAssetReferences.BindReference(asset, address, sourcePath);
        RuntimeAssetReferences.EnsureAssetType(asset, assetType);
        return asset;
    }
}
