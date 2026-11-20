namespace BEngine.Editor;

public interface IEditorTaskScheduler : IDisposable
{
    int WorkerCount { get; }
    bool IsMainThread { get; }
    int PendingBackgroundCount { get; }
    int PendingMainThreadCount { get; }

    Task ScheduleAsync(
        string name,
        Action<CancellationToken> operation,
        EditorTaskPriority priority = EditorTaskPriority.Normal,
        CancellationToken cancellationToken = default);

    Task<T> ScheduleAsync<T>(
        string name,
        Func<CancellationToken, T> operation,
        EditorTaskPriority priority = EditorTaskPriority.Normal,
        CancellationToken cancellationToken = default);

    Task ScheduleAsync(
        string name,
        Func<CancellationToken, Task> operation,
        EditorTaskPriority priority = EditorTaskPriority.Normal,
        CancellationToken cancellationToken = default);

    Task<T> ScheduleAsync<T>(
        string name,
        Func<CancellationToken, Task<T>> operation,
        EditorTaskPriority priority = EditorTaskPriority.Normal,
        CancellationToken cancellationToken = default);

    void Post(Action callback, string? name = null);
    int PumpMainThread(TimeSpan timeBudget, int maxCallbacks = 128);
}
