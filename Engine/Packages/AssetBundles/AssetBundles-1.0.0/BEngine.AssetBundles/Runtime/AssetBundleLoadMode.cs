namespace BEngine.AssetBundles;

public enum AssetBundleLoadMode
{
    PersistentCache = 0,
    /// <summary>Read the packaged release in place. Never install or update sandbox AB files.</summary>
    StreamingAssets = 1
}
