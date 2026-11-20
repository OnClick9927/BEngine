namespace BEngine.Editor;

/// <summary>
/// Executes editor extensions with cooperative loop protection, failure logging and
/// the optional presentation-state isolation supplied by the active UI backend.
/// </summary>
public static class EditorCallbacks
{
    /// <summary>Notifies presentation providers that a guarded script loop was stopped.</summary>
    /// <remarks>Core records the failure; the workbench chooses how to present it.</remarks>
    public static event Action<LoopExecutionLimitException>? LoopExecutionStopped
    {
        add => EditorFeatureGuard.LoopExecutionStopped += value;
        remove => EditorFeatureGuard.LoopExecutionStopped -= value;
    }

    /// <summary>Opens a loop-protection scope without changing presentation state.</summary>
    public static LoopExecutionGuard.ExecutionScope BeginLoopScope(string feature) =>
        EditorFeatureGuard.BeginLoopScope(feature);

    /// <summary>Runs a callback, logging ordinary failures and reporting success.</summary>
    /// <remarks>EditorCallbackExitException propagates to the presentation boundary.</remarks>
    public static bool Invoke(string feature, Action callback) => EditorFeatureGuard.Invoke(feature, callback);

    public static bool Invoke(object? owner, string callbackName, Action callback) =>
        EditorFeatureGuard.Invoke(owner, callbackName, callback);

    public static bool Invoke<TState>(string feature, TState state, Action<TState> callback) =>
        EditorFeatureGuard.Invoke(feature, state, callback);

    /// <summary>Uses the fallback result when the callback fails with an ordinary exception.</summary>
    public static bool TryInvoke<TResult>(string feature, Func<TResult> callback, TResult fallback, out TResult result) =>
        EditorFeatureGuard.TryInvoke(feature, callback, fallback, out result);

    public static bool TryInvoke<TState, TResult>(string feature, TState state,
        Func<TState, TResult> callback, TResult fallback, out TResult result) =>
        EditorFeatureGuard.TryInvoke(feature, state, callback, fallback, out result);

    /// <summary>Records a failure caught at an editor extension boundary.</summary>
    public static void Report(string feature, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        ArgumentNullException.ThrowIfNull(exception);
        EditorFeatureGuard.Report(feature, exception);
    }

    /// <summary>
    /// Dispatches a notification to each handler independently. Ordinary failures are logged;
    /// callback exit signals end the current handler. Remaining handlers still execute.
    /// </summary>
    public static void Dispatch(Action? callback, string callbackName) =>
        EditorCallbackDispatcher.Invoke(callback, callbackName);

    public static void Dispatch<T>(Action<T>? callback, T value, string callbackName) =>
        EditorCallbackDispatcher.Invoke(callback, value, callbackName);

    public static void Dispatch<T1, T2>(Action<T1, T2>? callback, T1 first, T2 second, string callbackName) =>
        EditorCallbackDispatcher.Invoke(callback, first, second, callbackName);

    public static void Dispatch<T1, T2, T3>(Action<T1, T2, T3>? callback,
        T1 first, T2 second, T3 third, string callbackName) =>
        EditorCallbackDispatcher.Invoke(callback, first, second, third, callbackName);
}
