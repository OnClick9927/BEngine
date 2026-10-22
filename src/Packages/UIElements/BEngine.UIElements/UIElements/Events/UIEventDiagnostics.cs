namespace BEngine.UIElements;

public static class UIEventDiagnostics
{
    private static long _nextDispatchId;

    public static event Action<UIEventTrace>? traceEmitted;

    internal static bool hasObservers => traceEmitted is not null;
    internal static void SkipUnobservedDispatch() => ++_nextDispatchId;

    internal static long BeginDispatch(UIEventBase evt, ReadOnlySpan<VisualElement> path)
    {
        var dispatchId = ++_nextDispatchId;
        if (traceEmitted is null) return dispatchId;
        Emit(new UIEventTrace(
            dispatchId,
            Time.realtimeSinceStartup,
            UIEventTraceStage.DispatchStarted,
            evt,
            evt.target,
            null,
            UIEventPropagationPhase.None,
            string.Empty,
            path.ToArray()));
        return dispatchId;
    }

    internal static void CallbackInvoked(
        long dispatchId,
        UIEventBase evt,
        VisualElement currentTarget,
        UIEventPropagationPhase phase,
        Delegate callback)
    {
        if (traceEmitted is null) return;
        var method = callback.Method;
        var owner = method.DeclaringType?.FullName ?? callback.Target?.GetType().FullName ?? "<unknown>";
        Emit(new UIEventTrace(
            dispatchId,
            Time.realtimeSinceStartup,
            UIEventTraceStage.CallbackInvoked,
            evt,
            evt.target,
            currentTarget,
            phase,
            $"{owner}.{method.Name}",
            []));
    }

    internal static void EndDispatch(long dispatchId, UIEventBase evt)
    {
        if (traceEmitted is null) return;
        Emit(new UIEventTrace(
            dispatchId,
            Time.realtimeSinceStartup,
            UIEventTraceStage.DispatchCompleted,
            evt,
            evt.target,
            evt.currentTarget,
            UIEventPropagationPhase.None,
            string.Empty,
            []));
    }

    private static void Emit(UIEventTrace trace)
    {
        var handlers = traceEmitted;
        if (handlers is null) return;
        foreach (var handler in Delegate.EnumerateInvocationList(handlers))
        {
            try { handler(trace); }
            catch
            {
                // Diagnostics must never affect event delivery.
            }
        }
    }
}
