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
        private float _reportedProgress = -1;
        private int _disposed;

        internal Tracking(AsyncOperation operation, string title, string info, bool cancelable)
        {
            _operation = operation;
            _title = title;
            _info = info;
            _cancelable = cancelable;
            EditorApplication.update += Poll;
            if (cancelable) EditorUtility.progressChanged += OnEditorProgressChanged;
            Poll();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            EditorApplication.update -= Poll;
            if (_cancelable) EditorUtility.progressChanged -= OnEditorProgressChanged;
            EditorUtility.ClearProgressBar();
        }

        private void Poll()
        {
            if (_operation.isDone) { Dispose(); return; }
            if (_reportedProgress == _operation.progress) return;
            _reportedProgress = _operation.progress;
            Report();
        }

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
