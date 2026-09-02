namespace BEngine;

/// <summary>
/// Optionally supplies fully constructed assets when raw resource bytes are not enough to preserve
/// importer settings or object identity.
/// </summary>
public interface IResourceAssetProvider
{
    bool TryLoadAsset(string path, string folderName, Type assetType, out BAsset asset);
}
