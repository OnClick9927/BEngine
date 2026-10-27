namespace BEngine.AssetBundles;

public sealed class AssetBundleInitializeOperation : AssetBundleLoadOperation, IDisposable
{
    internal AssetBundleInitializeOperation(IAssetBundleManager manager)
    {
        Start(async token =>
        {
            var loading = manager is AssetBundleManager runtime
                ? runtime.InitializeTrackedAsync(token, Tracker) : manager.InitializeAsync(token);
            while (!loading.IsCompleted)
            {
                SetProgress(Tracker.Progress);
                await BTask.Yield();
            }
            await loading;
            Tracker.Report(new(AssetBundleLoadKind.Initialization, AssetBundleLoadPhase.Completed, "catalog", 1, 1));
        });
    }
    public void Dispose() => Cancel();
}
