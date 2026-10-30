namespace BEngine.AssetBundles;

public sealed class AssetBundleInitializeOperation : AssetBundleLoadOperation, IDisposable
{
    internal AssetBundleInitializeOperation(IAssetBundleManager manager)
    {
        Start(async token =>
        {
            try
            {
                await (manager is AssetBundleManager runtime
                    ? runtime.InitializeTrackedAsync(token, Tracker) : manager.InitializeAsync(token));
                Tracker.Report(new(AssetBundleLoadKind.Initialization, AssetBundleLoadPhase.Completed, "catalog", 1, 1));
            }
            finally
            {
                SetProgress(Tracker.Progress);
            }
        });
    }
    public void Dispose() => Cancel();
}
