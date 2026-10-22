namespace BEngine.Reactive;

public static class Observable
{
    public static BObservable<T> Create<T>(Func<BObserver<T>, IDisposable?> subscribe) =>
        new AnonymousObservable<T>(subscribe ?? throw new ArgumentNullException(nameof(subscribe)));

    public static BObservable<T> Return<T>(T value) => Create<T>(observer =>
    {
        observer.OnNext(value);
        observer.OnCompleted();
        return BDisposable.Empty;
    });

    public static BObservable<T> Empty<T>() => Create<T>(observer =>
    {
        observer.OnCompleted();
        return BDisposable.Empty;
    });

    public static BObservable<T> Never<T>() => Create<T>(static _ => BDisposable.Empty);

    public static BObservable<int> Range(int start, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count > 0 && start > int.MaxValue - count + 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        return Create<int>(observer =>
        {
            for (var index = 0; index < count && !observer.IsDisposed; index++)
                observer.OnNext(start + index);
            observer.OnCompleted();
            return BDisposable.Empty;
        });
    }

    public static BObservable<T> FromEnumerable<T>(IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Create<T>(observer =>
        {
            try
            {
                foreach (var value in source)
                {
                    if (observer.IsDisposed) break;
                    observer.OnNext(value);
                }
                observer.OnCompleted();
            }
            catch (Exception exception) { observer.OnCompleted(exception); }
            return BDisposable.Empty;
        });
    }

    public static BObservable<BUnit> FromBTask(BTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return Create<BUnit>(observer =>
        {
            CompleteTask(task, observer);
            return BDisposable.Empty;
        });
    }

    public static BObservable<T> FromBTask<T>(BTask<T> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return Create<T>(observer =>
        {
            CompleteTask(task, observer);
            return BDisposable.Empty;
        });
    }

    public static BObservable<BUnit> FromBValueTask(BValueTask task) => FromBTask(task.AsBTask());
    public static BObservable<T> FromBValueTask<T>(BValueTask<T> task) => FromBTask(task.AsBTask());

    public static BObservable<BFrameTick> EveryUpdate(BTaskScheduler? scheduler = null)
    {
        scheduler ??= BTaskScheduler.Main;
        return Create<BFrameTick>(observer =>
        {
            void Handler(BFrameTick tick) => observer.OnNext(tick);
            scheduler.FrameAdvanced += Handler;
            return BDisposable.Create(() => scheduler.FrameAdvanced -= Handler);
        });
    }

    public static BObservable<long> IntervalFrame(
        int periodFrames = 1,
        BTaskScheduler? scheduler = null)
    {
        if (periodFrames <= 0) throw new ArgumentOutOfRangeException(nameof(periodFrames));
        return Create<long>(observer =>
        {
            var count = 0L;
            var remaining = periodFrames;
            void Handler(BFrameTick _)
            {
                if (--remaining > 0) return;
                remaining = periodFrames;
                observer.OnNext(count++);
            }
            var owner = scheduler ?? BTaskScheduler.Main;
            owner.FrameAdvanced += Handler;
            return BDisposable.Create(() => owner.FrameAdvanced -= Handler);
        });
    }

    public static BObservable<BUnit> Timer(TimeSpan dueTime) =>
        Create<BUnit>(observer => StartTimer(observer, dueTime, null));

    public static BObservable<long> Interval(TimeSpan period)
    {
        if (period <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(period));
        return Create<long>(observer => StartTimer(observer, period, period));
    }

    private static IDisposable StartTimer<T>(
        BObserver<T> observer,
        TimeSpan dueTime,
        TimeSpan? period)
    {
        if (dueTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(dueTime));
        var cancellation = new BCancellationSource();
        _ = RunTimer(observer, dueTime, period, cancellation.Token);
        return BDisposable.Create(() =>
        {
            if (!cancellation.IsCancellationRequested) cancellation.Cancel();
            cancellation.Dispose();
        });
    }

    private static async BTask RunTimer<T>(
        BObserver<T> observer,
        TimeSpan dueTime,
        TimeSpan? period,
        BCancellationToken cancellationToken)
    {
        try
        {
            await BTask.Delay(dueTime, cancellationToken);
            if (typeof(T) == typeof(BUnit))
            {
                observer.OnNext((T)(object)BUnit.Default);
                observer.OnCompleted();
                return;
            }
            var index = 0L;
            while (!cancellationToken.IsCancellationRequested && !observer.IsDisposed)
            {
                observer.OnNext((T)(object)index++);
                await BTask.Delay(period!.Value, cancellationToken);
            }
        }
        catch (BOperationCanceledException) { }
        catch (Exception exception) { observer.OnCompleted(exception); }
    }

    private static void CompleteTask(BTask task, BObserver<BUnit> observer)
    {
        void Complete()
        {
            try
            {
                task.GetAwaiter().GetResult();
                observer.OnNext(BUnit.Default);
                observer.OnCompleted();
            }
            catch (BOperationCanceledException) { observer.OnCompleted(); }
            catch (Exception exception) { observer.OnCompleted(exception); }
        }
        if (task.IsCompleted) Complete();
        else task.GetAwaiter().OnCompleted(Complete);
    }

    private static void CompleteTask<T>(BTask<T> task, BObserver<T> observer)
    {
        void Complete()
        {
            try
            {
                observer.OnNext(task.GetAwaiter().GetResult());
                observer.OnCompleted();
            }
            catch (BOperationCanceledException) { observer.OnCompleted(); }
            catch (Exception exception) { observer.OnCompleted(exception); }
        }
        if (task.IsCompleted) Complete();
        else task.GetAwaiter().OnCompleted(Complete);
    }
}
