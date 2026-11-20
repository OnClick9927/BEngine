namespace BEngine.Editor;
public static class AssetBundleImporterExtensions
{
    public static void SetAssetBundleNameAndVariant(this AssetImporter importer, string bundleName, string variantName)
    {
        importer.extensionSettings["assetBundleName"] = bundleName ?? string.Empty;
        importer.extensionSettings["assetBundleVariant"] = variantName ?? string.Empty;
        importer.SaveAndReimport();
    }
    public static string GetAssetBundleName(this AssetImporter importer) => importer.extensionSettings.TryGetValue("assetBundleName", out var value) ? value : string.Empty;
    public static string GetAssetBundleVariant(this AssetImporter importer) => importer.extensionSettings.TryGetValue("assetBundleVariant", out var value) ? value : string.Empty;
}
