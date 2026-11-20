namespace BEngine.Editor;

public static class CompilationPipeline
{
    private static CompilationScope? _activeCompilation;

    /// <summary>True through both compilation notifications, until the owning scope is released.</summary>
    public static bool isCompiling => Volatile.Read(ref _activeCompilation) is not null;

    public static event Action<object>? compilationStarted;
    public static event Action<object>? compilationFinished;
    public static event Action<string>? assemblyCompilationStarted;
    public static event Action<string, CompilerMessage[]>? assemblyCompilationFinished;

    public static void RequestScriptCompilation(
        RequestScriptCompilationOptions options = RequestScriptCompilationOptions.None)
    {
        _ = options;
        EditorToolingLifecycle.RequestScriptCompilation();
    }

    /// <summary>
    /// Begins an exclusive compilation transaction on the host's editor thread. Dispose on the
    /// same thread after completion, failure or cancellation. Worker compilation does not own
    /// this scope. UI, reload policy and progress presentation remain with the host.
    /// </summary>
    public static CompilationScope BeginCompilation(object context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scope = new CompilationScope(context);
        if (Interlocked.CompareExchange(ref _activeCompilation, scope, null) is not null)
            throw new InvalidOperationException("A compilation transaction is already active.");
        try
        {
            EditorFeatureGuard.ClearAll();
            EditorCallbackDispatcher.Invoke(compilationStarted, context, nameof(compilationStarted));
            return scope;
        }
        catch (Exception startFailure)
        {
            try { scope.Dispose(); }
            catch (Exception finishFailure)
            {
                throw new AggregateException("Compilation notification setup and cleanup failed.",
                    startFailure, finishFailure);
            }
            throw;
        }
    }

    /// <summary>Owns one paired start/finish notification and the associated compiling state.</summary>
    public sealed class CompilationScope : IDisposable
    {
        private object? _context;
        private readonly int _ownerThread = Environment.CurrentManagedThreadId;
        private bool _finishing;

        internal CompilationScope(object context) => _context = context;

        public void Dispose()
        {
            if (Environment.CurrentManagedThreadId != _ownerThread)
                throw new InvalidOperationException("Complete the compilation transaction on its owning editor thread.");
            if (_context is null || _finishing) return;
            _finishing = true;
            try
            {
                EditorCallbackDispatcher.Invoke(compilationFinished, _context, nameof(compilationFinished));
            }
            finally
            {
                _context = null;
                Interlocked.CompareExchange(ref _activeCompilation, null, this);
            }
        }
    }

    internal static void RaiseAssemblyCompilationStarted(string assemblyPath) =>
        EditorCallbackDispatcher.Invoke(assemblyCompilationStarted, assemblyPath,
            nameof(assemblyCompilationStarted));

    internal static void RaiseAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages) =>
        EditorCallbackDispatcher.Invoke(assemblyCompilationFinished, assemblyPath, messages,
            nameof(assemblyCompilationFinished));
}
