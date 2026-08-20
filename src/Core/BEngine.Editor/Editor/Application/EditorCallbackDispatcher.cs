namespace BEngine.Editor;

internal static class EditorCallbackDispatcher
{
    internal static void Invoke(Action? callback, string callbackName)
    {
        if (callback is null) return;
        foreach (Action handler in callback.GetInvocationList())
            InvokeSingle(handler, callbackName, handler);
    }

    internal static void Invoke<T>(Action<T>? callback, T value, string callbackName)
    {
        if (callback is null) return;
        foreach (Action<T> handler in callback.GetInvocationList())
            InvokeSingle(() => handler(value), callbackName, handler);
    }

    internal static void Invoke<T1, T2>(Action<T1, T2>? callback, T1 first, T2 second, string callbackName)
    {
        if (callback is null) return;
        foreach (Action<T1, T2> handler in callback.GetInvocationList())
            InvokeSingle(() => handler(first, second), callbackName, handler);
    }

    internal static void Invoke<T1, T2, T3>(
        Action<T1, T2, T3>? callback,
        T1 first,
        T2 second,
        T3 third,
        string callbackName)
    {
        if (callback is null) return;
        foreach (Action<T1, T2, T3> handler in callback.GetInvocationList())
            InvokeSingle(() => handler(first, second, third), callbackName, handler);
    }

    private static void InvokeSingle(Action callback, string callbackName, Delegate source)
    {
        var method = source.Method;
        var feature = $"Editor callback {callbackName} " +
                      $"[{method.Module.ModuleVersionId:N}:{method.MetadataToken}]";
        try { EditorFeatureGuard.Invoke(feature, callback); }
        catch (ExitGUIException) { }
    }
}
