namespace BEngine;

/// <summary>Optional mapping from a logical source to a prepared artifact. Implementation owns import policy.</summary>
public interface IAssetArtifactResolver
{
    string? Resolve(string sourcePath, Type assetType);
}
