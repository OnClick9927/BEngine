namespace BEngine.Reactive;

public static class ObservableExtensions
{
    public static BObservable<TResult> Select<T, TResult>(
        this BObservable<T> source,
        Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);
        return Observable.Create<TResult>(observer => source.Subscribe(
            value =>
            {
                try { observer.OnNext(selector(value)); }
                catch (Exception exception) { observer.OnErrorResume(exception); }
            }, observer.OnErrorResume, observer.OnCompleted));
    }

    public static BObservable<T> Where<T>(this BObservable<T> source, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);
        return Observable.Create<T>(observer => source.Subscribe(
            value =>
            {
                try
                {
                    if (predicate(value)) observer.OnNext(value);
                }
                catch (Exception exception) { observer.OnErrorResume(exception); }
            }, observer.OnErrorResume, observer.OnCompleted));
    }

    public static BObservable<T> Take<T>(this BObservable<T> source, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return Observable.Empty<T>();
        return Observable.Create<T>(observer =>
        {
            var remaining = count;
            var subscription = new BSerialDisposable();
            subscription.Disposable = source.Subscribe(
                value =>
                {
                    if (remaining <= 0) return;
                    observer.OnNext(value);
                    if (--remaining != 0) return;
                    observer.OnCompleted();
                    subscription.Dispose();
                }, observer.OnErrorResume, observer.OnCompleted);
            return subscription;
        });
    }

    public static BObservable<T> Skip<T>(this BObservable<T> source, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        return Observable.Create<T>(observer =>
        {
            var remaining = count;
            return source.Subscribe(value =>
            {
                if (remaining > 0) remaining--;
                else observer.OnNext(value);
            }, observer.OnErrorResume, observer.OnCompleted);
        });
    }

    public static BObservable<T> DistinctUntilChanged<T>(
        this BObservable<T> source,
        IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        comparer ??= EqualityComparer<T>.Default;
        return Observable.Create<T>(observer =>
        {
            var hasValue = false;
            var last = default(T);
            return source.Subscribe(value =>
            {
                if (hasValue && comparer.Equals(last!, value)) return;
                hasValue = true;
                last = value;
                observer.OnNext(value);
            }, observer.OnErrorResume, observer.OnCompleted);
        });
    }

    public static BObservable<T> Merge<T>(this BObservable<T> first, BObservable<T> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return Observable.Create<T>(observer =>
        {
            var completions = 0;
            void Complete(BResult result)
            {
                if (result.IsFailure) observer.OnCompleted(result);
                else if (++completions == 2) observer.OnCompleted();
            }
            return BDisposable.Combine(
                first.Subscribe(observer.OnNext, observer.OnErrorResume, Complete),
                second.Subscribe(observer.OnNext, observer.OnErrorResume, Complete));
        });
    }

    public static BObservable<(TFirst First, TSecond Second)> CombineLatest<TFirst, TSecond>(
        this BObservable<TFirst> first,
        BObservable<TSecond> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return Observable.Create<(TFirst, TSecond)>(observer =>
        {
            var hasFirst = false;
            var hasSecond = false;
            var firstValue = default(TFirst);
            var secondValue = default(TSecond);
            var completions = 0;
            void Complete(BResult result)
            {
                if (result.IsFailure) observer.OnCompleted(result);
                else if (++completions == 2) observer.OnCompleted();
            }
            return BDisposable.Combine(
                first.Subscribe(value =>
                {
                    hasFirst = true;
                    firstValue = value;
                    if (hasSecond) observer.OnNext((firstValue!, secondValue!));
                }, observer.OnErrorResume, Complete),
                second.Subscribe(value =>
                {
                    hasSecond = true;
                    secondValue = value;
                    if (hasFirst) observer.OnNext((firstValue!, secondValue!));
                }, observer.OnErrorResume, Complete));
        });
    }

    public static BObservable<T> Delay<T>(this BObservable<T> source, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        return Observable.Create<T>(observer =>
        {
            var lifetime = new BCancellationSource();
            var upstream = source.Subscribe(
                value => _ = DelayValue(value, observer, delay, lifetime.Token),
                observer.OnErrorResume,
                result => _ = DelayCompletion(result, observer, delay, lifetime.Token));
            return BDisposable.Combine(upstream, BDisposable.Create(() =>
            {
                if (!lifetime.IsCancellationRequested) lifetime.Cancel();
                lifetime.Dispose();
            }));
        });
    }

    public static BObservable<T> Debounce<T>(this BObservable<T> source, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        return Observable.Create<T>(observer =>
        {
            var state = new DebounceState<T>(observer, delay);
            state.SetUpstream(source.Subscribe(state.Next, observer.OnErrorResume, state.Complete));
            return state;
        });
    }

    public static ReadOnlyReactiveProperty<T> ToReadOnlyReactiveProperty<T>(
        this BObservable<T> source,
        T initialValue = default!) => new(source, initialValue);

    public static BTask<T> FirstAsync<T>(this BObservable<T> source, BCancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (token.IsCancellationRequested) return BTask.FromCanceled<T>(token);
        var completion = new BTaskCompletionSource<T>();
        var subscription = new BSerialDisposable();
        var cancellation = default(BCancellationRegistration);
        void Release()
        {
            cancellation.Dispose();
            subscription.Dispose();
        }
        subscription.Disposable = source.Subscribe(value =>
        {
            if (token.IsCancellationRequested) completion.TrySetCanceled(token);
            else completion.TrySetResult(value);
            Release();
        }, exception =>
        {
            completion.TrySetException(exception);
            Release();
        }, result =>
        {
            if (completion.Task.IsCompleted) return;
            if (result.IsFailure) completion.TrySetException(result.Exception!);
            else completion.TrySetException(new InvalidOperationException("The observable completed without a value."));
            Release();
        });
        if (!completion.Task.IsCompleted && token.CanBeCanceled)
            cancellation = token.Register(() =>
            {
                completion.TrySetCanceled(token);
                Release();
            });
        return completion.Task;
    }

    public static BTask ForEachAsync<T>(
        this BObservable<T> source,
        Action<T> action,
        BCancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(action);
        if (token.IsCancellationRequested) return BTask.FromCanceled(token);
        var completion = new BTaskCompletionSource();
        var subscription = new BSerialDisposable();
        var cancellation = default(BCancellationRegistration);
        void Release()
        {
            cancellation.Dispose();
            subscription.Dispose();
        }
        subscription.Disposable = source.Subscribe(value =>
        {
            if (token.IsCancellationRequested)
            {
                completion.TrySetCanceled(token);
                Release();
                return;
            }
            try { action(value); }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
                Release();
            }
        }, exception =>
        {
            completion.TrySetException(exception);
            Release();
        }, result =>
        {
            if (result.IsSuccess) completion.TrySetResult();
            else completion.TrySetException(result.Exception!);
            Release();
        });
        if (!completion.Task.IsCompleted && token.CanBeCanceled)
            cancellation = token.Register(() =>
            {
                completion.TrySetCanceled(token);
                Release();
            });
        return completion.Task;
    }

    private static async BTask DelayValue<T>(
        T value,
        BObserver<T> observer,
        TimeSpan delay,
        BCancellationToken token)
    {
        try
        {
            await BTask.Delay(delay, token);
            observer.OnNext(value);
        }
        catch (BOperationCanceledException) { }
    }

    private static async BTask DelayCompletion<T>(
        BResult result,
        BObserver<T> observer,
        TimeSpan delay,
        BCancellationToken token)
    {
        try
        {
            await BTask.Delay(delay, token);
            observer.OnCompleted(result);
        }
        catch (BOperationCanceledException) { }
    }

    private sealed class DebounceState<T>(BObserver<T> observer, TimeSpan delay) : IDisposable
    {
        private readonly BSerialDisposable _pending = new();
        private IDisposable? _upstream;
        private bool _hasValue;
        private T? _value;
        private BResult? _completion;

        internal void SetUpstream(IDisposable upstream) => _upstream = upstream;

        internal void Next(T value)
        {
            _hasValue = true;
            _value = value;
            Schedule();
        }

        internal void Complete(BResult result)
        {
            _completion = result;
            if (!_hasValue) observer.OnCompleted(result);
        }

        private void Schedule()
        {
            var cancellation = new BCancellationSource();
            _pending.Disposable = BDisposable.Create(() =>
            {
                if (!cancellation.IsCancellationRequested) cancellation.Cancel();
                cancellation.Dispose();
            });
            _ = Publish(cancellation.Token);
        }

        private async BTask Publish(BCancellationToken token)
        {
            try
            {
                await BTask.Delay(delay, token);
                if (!_hasValue) return;
                _hasValue = false;
                observer.OnNext(_value!);
                if (_completion is { } completion) observer.OnCompleted(completion);
            }
            catch (BOperationCanceledException) { }
        }

        public void Dispose()
        {
            _pending.Dispose();
            _upstream?.Dispose();
            _upstream = null;
        }
    }
}
