namespace BEngine;

internal static class AsyncHostBridge
{
    private static readonly object Gate = new();
    private static readonly HashSet<Action> QuittingCallbacks = [];
    private static Action<Action>? _subscribeQuitting;
    private static Action<Action>? _unsubscribeQuitting;

    internal static Func<int> TargetFrameRate { get; private set; } = static () => 60;
    internal static Action<string> WriteDiagnostic { get; private set; } = Console.Error.WriteLine;
    internal static Action<Exception> LogException { get; private set; } = Console.Error.WriteLine;

    internal static void RegisterHost(Func<int> targetFrameRate, Action<string> writeDiagnostic,
        Action<Exception> logException, Action<Action> subscribeQuitting, Action<Action> unsubscribeQuitting)
    {
        ArgumentNullException.ThrowIfNull(targetFrameRate);
        ArgumentNullException.ThrowIfNull(writeDiagnostic);
        ArgumentNullException.ThrowIfNull(logException);
        ArgumentNullException.ThrowIfNull(subscribeQuitting);
        ArgumentNullException.ThrowIfNull(unsubscribeQuitting);
        lock (Gate)
        {
            foreach (var callback in QuittingCallbacks) _unsubscribeQuitting?.Invoke(callback);
            TargetFrameRate = targetFrameRate;
            WriteDiagnostic = writeDiagnostic;
            LogException = logException;
            _subscribeQuitting = subscribeQuitting;
            _unsubscribeQuitting = unsubscribeQuitting;
            foreach (var callback in QuittingCallbacks) _subscribeQuitting(callback);
        }
    }

    internal static void SubscribeQuitting(Action callback)
    {
        lock (Gate)
            if (QuittingCallbacks.Add(callback)) _subscribeQuitting?.Invoke(callback);
    }

    internal static void UnsubscribeQuitting(Action callback)
    {
        lock (Gate)
            if (QuittingCallbacks.Remove(callback)) _unsubscribeQuitting?.Invoke(callback);
    }
}
