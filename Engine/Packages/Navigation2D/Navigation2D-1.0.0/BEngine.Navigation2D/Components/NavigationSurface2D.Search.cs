namespace BEngine.Navigation2D;

public sealed partial class NavigationSurface2D
{
    private readonly HashSet<SearchRequest> _searches = [];
    public int pendingSearchCount => _searches.Count;
    /// <summary>Searches in 32-node steps within the shared frame budget. Does not start a worker thread.</summary>
    public BTask<bool> CalculatePathAsync(Vector2 source, Vector2 target, NavigationPath2D path,
        ulong areaMask = Navigation2D.AllAreas, BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_grid is null) { path.ClearCorners(); return BTask.FromResult(false); }
        if (_searches.Count >= 64) throw new InvalidOperationException("This navigation surface already has 64 pending searches.");
        var request = new SearchRequest(this, _grid.SearchSteps(source, target, path, areaMask), cancellationToken);
        _searches.Add(request);
        try { request.Work = CooperativeWorkScheduler.Main.Schedule(request.Step, request.Fail); }
        catch { request.Dispose(); throw; }
        return request.Source.Task;
    }
    public override void OnDisable() => CancelSearches();
    public override void OnDestroy() => CancelSearches();
    private void CancelSearches() { foreach (var search in _searches.ToArray()) search.Dispose(); }
    private sealed class SearchRequest(NavigationSurface2D owner, IEnumerator<bool> steps, BCancellationToken token) : IDisposable
    {
        internal readonly BTaskCompletionSource<bool> Source = new();
        internal IDisposable? Work;
        private readonly long _revision = Navigation2D.Revision;
        private readonly Vector2 _position = owner.transform.position;
        private readonly Fix64 _rotation = owner.transform.rotation;
        private readonly Vector2 _scale = owner.transform.lossyScale;
        private bool _result, _disposed;
        internal CooperativeWorkStatus Step()
        {
            token.ThrowIfCancellationRequested();
            if (owner.isDestroyed || _revision != Navigation2D.Revision || owner.transform.position != _position ||
                owner.transform.rotation != _rotation || owner.transform.lossyScale != _scale)
                throw new InvalidOperationException("Navigation data or surface transform changed during the search. Submit a new search.");
            if (steps.MoveNext()) { _result = steps.Current; return CooperativeWorkStatus.Continue; }
            Source.TrySetResult(_result); Dispose(); return CooperativeWorkStatus.Complete;
        }
        internal void Fail(Exception error) { Source.TrySetException(error); Dispose(); }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            Work?.Dispose(); Work = null; steps.Dispose(); owner._searches.Remove(this);
            Source.TrySetCanceled();
        }
    }
}
