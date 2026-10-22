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
        var isolation = GUI.BeginFeatureIsolation();
        var succeeded = false;
        try
        {
            callback();
            succeeded = true;
            Clear(feature);
            return true;
        }
        catch (ExitGUIException)
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
            isolation.Restore(succeeded);
        }
    }

    internal static bool TryInvoke<T>(string feature, Func<T> callback, T fallback, out T result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        ArgumentNullException.ThrowIfNull(callback);
        using var loopScope = LoopExecutionGuard.BeginScope(feature);
        var isolation = GUI.BeginFeatureIsolation();
        var succeeded = false;
        try
        {
            result = callback();
            succeeded = true;
            Clear(feature);
            return true;
        }
        catch (ExitGUIException)
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
            isolation.Restore(succeeded);
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

    private static void OnLoopLimitExceeded(LoopExecutionLimitException exception)
    {
        var signature = $"{exception.EntryPoint}\0{exception.SourcePath}\0{exception.Line}";
        lock (Gate)
            if (!LoopAlerts.Add(signature)) return;

        try
        {
            if (EditorApplication.isPlaying) EditorApplication.isPaused = true;
            EditorUtility.DisplayDialog(
                "Script Execution Stopped",
                $"BEngine stopped a script loop after {exception.IterationLimit} iterations so the " +
                $"editor can remain responsive.\n\nEntry: {exception.EntryPoint}\n" +
                $"Source: {exception.SourcePath}:{exception.Line}\n\n" +
                "Correct the loop condition and let script hot reload compile the change.",
                "OK");
        }
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
