namespace BEngine.Editor;

internal sealed class AssetBundleBuildProgressReporter : IProgress<AssetBundleBuildProgress>
{
    private readonly CancellationTokenSource _cancellation;

    internal AssetBundleBuildProgressReporter(CancellationTokenSource cancellation)
    {
        _cancellation = cancellation;
    }

    public void Report(AssetBundleBuildProgress value)
    {
        void ShowProgress()
        {
            var item = string.IsNullOrWhiteSpace(value.ItemName) ? value.Phase.ToString() : value.ItemName;
            if (EditorUtility.DisplayCancelableProgressBar("AssetBundle Build",
                    $"{value.Phase}: {item}", value.Fraction))
                _cancellation.Cancel();
        }

        if (EditorApplication.TaskScheduler?.IsMainThread != false)
            ShowProgress();
        else
            EditorApplication.QueueMainThread(ShowProgress, "AssetBundle build progress");
    }
}
