namespace BEngine.Editor;

public struct AssetBundleBuild
{
    public string assetBundleName;
    public string assetBundleVariant;
    public string[] assetNames;

    internal readonly AssetBundleBuildDefinition ToDefinition() => new()
    {
        Name = string.IsNullOrWhiteSpace(assetBundleVariant)
            ? assetBundleName : $"{assetBundleName}.{assetBundleVariant}",
        AssetPaths = assetNames ?? []
    };
}
