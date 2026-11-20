namespace BEngine.AssetBundles;

/// <summary>Creates a metadata view across managed domains; payloads and leases remain owned by the source manager.</summary>
public static class AssetBundleContentBridge
{
    public static IAssetBundleManager Create(
        byte[] catalogBytes, byte[] versionBytes, Func<string, byte[]> read,
        Func<string, BCancellationToken, BValueTask<(byte[] Bytes, Action Release)>> readAsync) =>
        VirtualAssetBundleManager.CreateSnapshotBridge(catalogBytes, versionBytes, read, readAsync);
}
