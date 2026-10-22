namespace BEngine.Editor;

public static class EditorAsyncOperationProgress
{
    public static IDisposable Track(
        AsyncOperation operation,
        string title,
        string info,
        bool cancelable = false)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return new Tracking(operation, title, info ?? string.Empty, cancelable);
    }

    private sealed class Tracking : IDisposable
    {
        private readonly AsyncOperation _operation;
        private readonly string _title;
        private readonly string _info;
        private readonly bool _cancelable;
        private int _disposed;

        internal Tracking(AsyncOperation operation, string title, string info, bool cancelable)
        {
            _operation = operation;
            _title = title;
            _info = info;
            _cancelable = cancelable;
            operation.progressChanged += OnProgressChanged;
            operation.completed += OnCompleted;
            if (cancelable) EditorUtility.progressChanged += OnEditorProgressChanged;
            Report();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _operation.progressChanged -= OnProgressChanged;
            _operation.completed -= OnCompleted;
            if (_cancelable) EditorUtility.progressChanged -= OnEditorProgressChanged;
            EditorUtility.ClearProgressBar();
        }

        private void OnProgressChanged(AsyncOperation _) => Report();

        private void OnCompleted(AsyncOperation _) => Dispose();

        private void OnEditorProgressChanged(EditorProgressInfo progress)
        {
            if (progress.IsCancellationRequested) _operation.Cancel();
        }

        private void Report()
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            if (_cancelable) EditorUtility.DisplayCancelableProgressBar(_title, _info, _operation.progress);
            else EditorUtility.DisplayProgressBar(_title, _info, _operation.progress);
        }
    }
}
