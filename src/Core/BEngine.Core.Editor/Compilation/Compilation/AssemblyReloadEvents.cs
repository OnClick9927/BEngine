namespace BEngine.Editor;

public static class AssemblyReloadEvents
{
    private static ReloadScope? _activeReload;

    public static event Action? beforeAssemblyReload;
    public static event Action? afterAssemblyReload;

    /// <summary>Begins an exclusive editor-thread replacement. Disposal publishes the
    /// paired after notification, including failed replacement/cleanup paths. Does not
    /// compile, load, initialize extensions, reset state or choose reload policy.</summary>
    public static ReloadScope BeginReload() => Begin(initialLoad: false);

    /// <summary>Begins first-load coordination without a before notification. Call
    /// Complete after extension initialization and host preparation; disposal alone
    /// aborts an incomplete first load without announcing readiness.</summary>
    public static ReloadScope BeginInitialization() => Begin(initialLoad: true);

    private static ReloadScope Begin(bool initialLoad)
    {
        var scope = new ReloadScope(initialLoad);
        if (Interlocked.CompareExchange(ref _activeReload, scope, null) is not null)
            throw new InvalidOperationException("An editor assembly lifecycle transaction is already active.");
        try
        {
            if (!initialLoad) RaiseBeforeAssemblyReload();
            return scope;
        }
        catch (Exception failure)
        {
            try { scope.Dispose(); }
            catch (Exception cleanup)
            {
                throw new AggregateException("Reload notification setup and cleanup failed.", failure, cleanup);
            }
            throw;
        }
    }

    /// <summary>Owns lifecycle notification pairing. Holds no project, assembly or host.
    /// Use and release on the creating editor thread; nested transactions are rejected.</summary>
    public sealed class ReloadScope : IDisposable
    {
        private readonly int _ownerThread = Environment.CurrentManagedThreadId;
        private readonly bool _initialLoad;
        private bool _finished, _finishing;

        internal ReloadScope(bool initialLoad) => _initialLoad = initialLoad;

        /// <summary>Publishes readiness once. For first load this is explicit; a reload
        /// also publishes its paired after notification when disposed after failure.</summary>
        public void Complete() => Finish(notify: true);

        public void Dispose() => Finish(notify: !_initialLoad);

        private void Finish(bool notify)
        {
            if (Environment.CurrentManagedThreadId != _ownerThread)
                throw new InvalidOperationException("Complete the assembly lifecycle on its owning editor thread.");
            if (_finished || _finishing) return;
            _finishing = true;
            try { if (notify) RaiseAfterAssemblyReload(); }
            finally
            {
                _finished = true;
                Interlocked.CompareExchange(ref _activeReload, null, this);
            }
        }
    }

    internal static void RaiseBeforeAssemblyReload() =>
        EditorCallbackDispatcher.Invoke(beforeAssemblyReload, nameof(beforeAssemblyReload));

    internal static void RaiseAfterAssemblyReload() =>
        EditorCallbackDispatcher.Invoke(afterAssemblyReload, nameof(afterAssemblyReload));
}
