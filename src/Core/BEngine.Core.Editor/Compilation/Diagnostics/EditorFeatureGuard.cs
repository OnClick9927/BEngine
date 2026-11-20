using System.Diagnostics;
using System.Reflection;

namespace BEngine.Editor;

internal static class EditorFeatureGuard
{
    private const long RepeatLogIntervalMilliseconds = 5000;
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, FaultState> Faults = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LoopAlerts = new(StringComparer.Ordinal);

    static EditorFeatureGuard() => LoopExecutionGuard.LimitExceeded += OnLoopLimitExceeded;

    internal static int activeFaultCount
    {
        get { lock (Gate) return Faults.Count; }
    }

    internal static LoopExecutionGuard.ExecutionScope BeginLoopScope(string feature) =>
        LoopExecutionGuard.BeginScope(feature);

    internal static bool Invoke(object? owner, string callbackName, Action callback) =>
        Invoke(FeatureName(owner, callbackName), callback);

    internal static bool Invoke(string feature, Action callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        ArgumentNullException.ThrowIfNull(callback);
        using var loopScope = LoopExecutionGuard.BeginScope(feature);
        var isolationBackend = EditorFeatureIsolation.Current;
        var isolation = isolationBackend?.Begin() ?? -1;
        var succeeded = false;
        try
        {
            callback();
            succeeded = true;
            Clear(feature);
            return true;
        }
        catch (EditorCallbackExitException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Report(feature, exception);
            return false;
        }
        finally
        {
            isolationBackend?.Restore(isolation, succeeded);
        }
    }

    internal static bool TryInvoke<T>(string feature, Func<T> callback, T fallback, out T result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        ArgumentNullException.ThrowIfNull(callback);
        using var loopScope = LoopExecutionGuard.BeginScope(feature);
        var isolationBackend = EditorFeatureIsolation.Current;
        var isolation = isolationBackend?.Begin() ?? -1;
        var succeeded = false;
        try
        {
            result = callback();
            succeeded = true;
            Clear(feature);
            return true;
        }
        catch (EditorCallbackExitException)
        {
            throw;
        }
        catch (Exception exception)
        {
            result = fallback;
            Report(feature, exception);
            return false;
        }
        finally
        {
            isolationBackend?.Restore(isolation, succeeded);
        }
    }

    internal static bool Invoke<TState>(string feature, TState state, Action<TState> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        ArgumentNullException.ThrowIfNull(callback);
        using var loopScope = LoopExecutionGuard.BeginScope(feature);
        var isolationBackend = EditorFeatureIsolation.Current;
        var isolation = isolationBackend?.Begin() ?? -1;
        var succeeded = false;
        try
        {
            callback(state);
            succeeded = true;
            Clear(feature);
            return true;
        }
        catch (EditorCallbackExitException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Report(feature, exception);
            return false;
        }
        finally
        {
            isolationBackend?.Restore(isolation, succeeded);
        }
    }

    internal static bool TryInvoke<TState, TResult>(string feature, TState state,
        Func<TState, TResult> callback, TResult fallback, out TResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        ArgumentNullException.ThrowIfNull(callback);
        using var loopScope = LoopExecutionGuard.BeginScope(feature);
        var isolationBackend = EditorFeatureIsolation.Current;
        var isolation = isolationBackend?.Begin() ?? -1;
        var succeeded = false;
        try
        {
            result = callback(state);
            succeeded = true;
            Clear(feature);
            return true;
        }
        catch (EditorCallbackExitException)
        {
            throw;
        }
        catch (Exception exception)
        {
            result = fallback;
            Report(feature, exception);
            return false;
        }
        finally
        {
            isolationBackend?.Restore(isolation, succeeded);
        }
    }

    internal static void Report(string feature, Exception exception)
    {
        var cause = Unwrap(exception);
        var signature = $"{cause.GetType().FullName}:{cause.Message}";
        var platformService = EditorServiceAccess.GetRequiredService<BEngine.Platform.PlatformUtility>();
        var now = platformService.TickCount64;
        var shouldLog = false;
        lock (Gate)
        {
            if (!Faults.TryGetValue(feature, out var state) || state.Signature != signature ||
                now - state.LastLoggedAt >= RepeatLogIntervalMilliseconds)
            {
                Faults[feature] = new FaultState(signature, now, state.Count + 1);
                shouldLog = true;
            }
            else Faults[feature] = state with { Count = state.Count + 1 };
        }
        if (!shouldLog) return;
        try { Debug.LogException(new EditorFeatureException(feature, cause)); }
        catch (Exception loggingException)
        {
            Trace.WriteLine($"Editor feature '{feature}' failed and its log could not be published: " +
                            $"{loggingException}{platformService.NewLine}Original failure: {cause}");
        }
    }

    internal static void ClearAll()
    {
        lock (Gate)
        {
            Faults.Clear();
            LoopAlerts.Clear();
        }
    }

    public static event Action<LoopExecutionLimitException>? LoopExecutionStopped;

    private static void OnLoopLimitExceeded(LoopExecutionLimitException exception)
    {
        var signature = $"{exception.EntryPoint}\0{exception.SourcePath}\0{exception.Line}";
        lock (Gate)
            if (!LoopAlerts.Add(signature)) return;

        try { LoopExecutionStopped?.Invoke(exception); }
        catch (Exception dialogException)
        {
            Trace.WriteLine($"Could not display the loop execution warning: {dialogException}");
        }
    }

    private static void Clear(string feature)
    {
        lock (Gate) Faults.Remove(feature);
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException { InnerException: not null } invocation)
            exception = invocation.InnerException!;
        return exception;
    }

    private static string FeatureName(object? owner, string callbackName)
    {
        var type = owner switch
        {
            null => "BEngine.Editor",
            Type value => value.FullName ?? value.Name,
            _ => owner.GetType().FullName ?? owner.GetType().Name
        };
        return $"{type}.{callbackName}";
    }

    private readonly record struct FaultState(string Signature, long LastLoggedAt, int Count);
}
