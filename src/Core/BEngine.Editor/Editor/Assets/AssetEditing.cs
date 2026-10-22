namespace BEngine.Editor;

public static class AssetEditing
{
    public static void Edit(BAsset asset, string label, Action change)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(change);
        ObjectDisposedException.ThrowIf(asset.isDestroyed, asset);
        EditorAssetWritePolicy.EnsureCanWrite(label);
        Undo.IncrementCurrentGroup();
        Undo.RecordObject(asset, label);
        change();
        EditorUtility.SetDirty(asset);
    }
}
