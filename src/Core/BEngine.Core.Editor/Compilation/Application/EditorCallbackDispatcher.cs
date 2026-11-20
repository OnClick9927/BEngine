namespace BEngine.Editor;

internal static class EditorCallbackDispatcher
{
    internal static void Invoke(Action? callback, string callbackName)
    {
        if (callback is null) return;
        foreach (Action handler in Delegate.EnumerateInvocationList(callback))
            InvokeSingle(handler, callbackName, handler);
    }

    internal static void Invoke<T>(Action<T>? callback, T value, string callbackName)
    {
        if (callback is null) return;
        foreach (Action<T> handler in Delegate.EnumerateInvocationList(callback))
            InvokeSingle((Handler: handler, Value: value), callbackName, handler,
                static state => state.Handler(state.Value));
    }

    internal static void Invoke<T1, T2>(Action<T1, T2>? callback, T1 first, T2 second, string callbackName)
    {
        if (callback is null) return;
        foreach (Action<T1, T2> handler in Delegate.EnumerateInvocationList(callback))
            InvokeSingle((Handler: handler, First: first, Second: second), callbackName, handler,
                static state => state.Handler(state.First, state.Second));
    }

    internal static void Invoke<T1, T2, T3>(
        Action<T1, T2, T3>? callback,
        T1 first,
        T2 second,
        T3 third,
        string callbackName)
    {
        if (callback is null) return;
        foreach (Action<T1, T2, T3> handler in Delegate.EnumerateInvocationList(callback))
            InvokeSingle((Handler: handler, First: first, Second: second, Third: third), callbackName, handler,
                static state => state.Handler(state.First, state.Second, state.Third));
    }

    private static void InvokeSingle(Action callback, string callbackName, Delegate source)
    {
        var feature = FeatureName(callbackName, source);
        try { EditorFeatureGuard.Invoke(feature, callback); }
        catch (EditorCallbackExitException) { }
    }

    private static void InvokeSingle<TState>(TState state, string callbackName, Delegate source,
        Action<TState> callback)
    {
        var feature = FeatureName(callbackName, source);
        try { EditorFeatureGuard.Invoke(feature, state, callback); }
        catch (EditorCallbackExitException) { }
    }

    private static string FeatureName(string callbackName, Delegate source)
    {
        var method = source.Method;
        return $"Editor callback {callbackName} " +
               $"[{method.Module.ModuleVersionId:N}:{method.MetadataToken}]";
    }
}
