namespace BEngine.Editor;

public sealed record AssetBundleBuildProgress(
    AssetBundleBuildPhase Phase,
    int CompletedItems,
    int TotalItems,
    string ItemName = "")
{
    public float Fraction => TotalItems <= 0 ? 0f : Math.Clamp((float)CompletedItems / TotalItems, 0f, 1f);
}
