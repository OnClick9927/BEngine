namespace BEngine.Jobs;

/// <summary>Explicit worker-safe value job. Only copied unmanaged data may be accessed;
/// Scene, BObject, engine services and native-container APIs are forbidden.</summary>
public interface IBackgroundJob<TResult> where TResult : unmanaged
{
    TResult Execute(CancellationToken cancellationToken);
}

public readonly record struct BackgroundJobSnapshot(long Scheduled, long Completed, int Pending, int WorkerLimit);

/// <summary>Bounded pure-data jobs. Await the returned BTask on the engine thread;
/// no synchronous blocking Complete is provided.</summary>
public static class BackgroundJobs
{
    private static BCancellationSource? _shutdown;
    private static long _scheduled, _completed;
    private static int _pending;
    public static BackgroundJobSnapshot snapshot => new(_scheduled, _completed, _pending,
        BackgroundWorkScheduler.IsSupported ? BackgroundWorkScheduler.Main.Snapshot.WorkerLimit : 0);

    public static async BTask<TResult> ScheduleAsync<TJob, TResult>(TJob job, BCancellationToken cancellationToken = default)
        where TJob : unmanaged, IBackgroundJob<TResult> where TResult : unmanaged
    {
        cancellationToken.ThrowIfCancellationRequested();
        _shutdown ??= new();
        using var stop = new BCancellationSource();
        using var caller = cancellationToken.Register(() => stop.Cancel());
        using var shutdown = _shutdown.Token.Register(() => stop.Cancel());
        _scheduled++; _pending++;
        try
        {
            if (!BackgroundWorkScheduler.IsSupported)
            {
                // Web remains explicit cooperative work and never silently
                // executes a nominal background job inside ScheduleAsync.
                await BTask.Yield();
                stop.Token.ThrowIfCancellationRequested();
                return job.Execute(CancellationToken.None);
            }
            return await BackgroundWorkScheduler.Main.RunOwned(token => job.Execute(token), typeof(TJob).Assembly, stop.Token);
        }
        finally { _pending--; _completed++; }
    }

    internal static void Shutdown()
    {
        _shutdown?.Cancel(); _shutdown?.Dispose(); _shutdown = null;
    }
}
