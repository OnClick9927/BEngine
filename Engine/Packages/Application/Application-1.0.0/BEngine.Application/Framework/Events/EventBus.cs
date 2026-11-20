using System;
using System.Collections.Generic;

namespace BEngine.App.Events;

/// <summary>
/// A single-game-loop event bus. All operations, token cancellation and asynchronous
/// continuations must run on that same loop; this service creates no background work.
/// Each publish captures a subscription snapshot: additions run on the next publish, and removal
/// suppresses callbacks not yet invoked. Nested publishes have independent snapshots.
/// Handler failures do not skip later handlers and are reported together as AggregateException.
/// PublishAsync stops between callbacks when its token is canceled. Dispose removes all handlers
/// and cancels waiters, but does not interrupt a callback that has already started.
/// </summary>
public sealed class EventBus : IEventBus
{
    private Dictionary<EventKey, IChannel>? _channels;
    private HashSet<IWaiter>? _waiters;
    private bool _disposed;
    private bool _initialized;

    void IGameService.Initialize(GameDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ThrowIfDisposed();
        if (_initialized) throw new InvalidOperationException("An event bus can only be initialized once.");
        _initialized = true;
    }

    public IDisposable Subscribe<T>(Action<T> handler, string? topic = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ThrowIfDisposed();
        var channel = GetOrCreateChannel<T>(topic);
        var subscription = new Subscription<T>(this, channel, handler, null);
        channel.Add(subscription);
        return subscription.Handle;
    }

    public IDisposable Subscribe<T>(IEventHandler<T> handler, string? topic = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Subscribe<T>(handler.Handle, topic);
    }

    public IDisposable SubscribeAsync<T>(Func<T, BCancellationToken, BTask> handler, string? topic = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ThrowIfDisposed();
        var channel = GetOrCreateChannel<T>(topic);
        var subscription = new Subscription<T>(this, channel, null, handler);
        channel.Add(subscription);
        return subscription.Handle;
    }

    public IDisposable SubscribeAsync<T>(IAsyncEventHandler<T> handler, string? topic = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return SubscribeAsync<T>(handler.HandleAsync, topic);
    }

    public void Publish<T>(T message, string? topic = null)
    {
        var snapshot = Snapshot<T>(topic);
        List<Exception>? failures = null;
        foreach (var subscription in snapshot)
        {
            var handler = subscription.SyncHandler;
            if (handler is null) continue;
            try { handler(message); }
            catch (Exception failure) { (failures ??= new()).Add(failure); }
        }
        if (failures is not null) throw new AggregateException("Event handlers failed.", failures);
    }

    public BTask PublishAsync<T>(T message, BCancellationToken cancellationToken = default,
        string? topic = null)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = Snapshot<T>(topic);
            List<Exception>? failures = null;
            for (var i = 0; i < snapshot.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var subscription = snapshot[i];
                try
                {
                    if (subscription.SyncHandler is { } sync) sync(message);
                    else if (subscription.AsyncHandler is { } handler)
                    {
                        var pending = handler(message, cancellationToken)
                            ?? throw new InvalidOperationException("An event handler returned a null BTask.");
                        if (!pending.IsCompleted)
                            return ResumeAsync(snapshot, i + 1, pending, message, cancellationToken, failures);
                        pending.GetAwaiter().GetResult();
                    }
                }
                catch (BOperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception failure) { (failures ??= new()).Add(failure); }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return failures is null ? BTask.CompletedTask
                : BTask.FromException(new AggregateException("Event handlers failed.", failures));
        }
        catch (Exception failure) { return BTask.FromException(failure); }
    }

    private static async BTask ResumeAsync<T>(Subscription<T>[] snapshot, int next, BTask pending,
        T message, BCancellationToken cancellationToken, List<Exception>? failures)
    {
        try { await pending; }
        catch (BOperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception failure) { (failures ??= new()).Add(failure); }
        for (var i = next; i < snapshot.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var subscription = snapshot[i];
            try
            {
                if (subscription.SyncHandler is { } sync) sync(message);
                else if (subscription.AsyncHandler is { } handler)
                    await (handler(message, cancellationToken)
                        ?? throw new InvalidOperationException("An event handler returned a null BTask."));
            }
            catch (BOperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception failure) { (failures ??= new()).Add(failure); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (failures is not null) throw new AggregateException("Event handlers failed.", failures);
    }

    public BTask<T> WaitAsync<T>(BCancellationToken cancellationToken = default, string? topic = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var channel = GetOrCreateChannel<T>(topic);
        var waiter = new Waiter<T>(this, cancellationToken);
        var subscription = new Subscription<T>(this, channel, waiter.Complete, null);
        waiter.Attach(subscription.Handle);
        channel.Add(subscription);
        (_waiters ??= new()).Add(waiter);
        try { waiter.RegisterCancellation(); }
        catch { waiter.CancelFromBus(); throw; }
        return waiter.Task;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_channels is { } channels)
        {
            foreach (var channel in channels.Values) channel.DetachAll();
            _channels = null;
        }
        var waiters = _waiters;
        _waiters = null;
        if (waiters is null || waiters.Count == 0) return;
        var pending = new IWaiter[waiters.Count];
        waiters.CopyTo(pending);
        foreach (var waiter in pending) waiter.CancelFromBus();
    }

    private Channel<T> GetOrCreateChannel<T>(string? topic)
    {
        var key = new EventKey(typeof(T), topic ?? string.Empty);
        var channels = _channels ??= new();
        if (channels.TryGetValue(key, out var existing)) return (Channel<T>)existing;
        var channel = new Channel<T>(key);
        channels.Add(key, channel);
        return channel;
    }

    private Subscription<T>[] Snapshot<T>(string? topic)
    {
        ThrowIfDisposed();
        return _channels is not null && _channels.TryGetValue(new EventKey(typeof(T), topic ?? string.Empty), out var channel)
            ? ((Channel<T>)channel).Snapshot : Array.Empty<Subscription<T>>();
    }

    private void Remove<T>(Subscription<T> subscription)
    {
        var channel = subscription.Channel;
        if (channel is null) return;
        channel.Remove(subscription);
        subscription.Detach();
        if (channel.Count == 0) _channels!.Remove(channel.Key);
    }

    private void RemoveWaiter(IWaiter waiter) => _waiters?.Remove(waiter);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    private readonly record struct EventKey(Type Type, string Topic);
    private interface IChannel { void DetachAll(); }
    private interface IWaiter { void CancelFromBus(); }

    private sealed class Channel<T>(EventKey key) : IChannel
    {
        private Subscription<T>? _first;
        private Subscription<T>? _last;
        private Subscription<T>[]? _snapshot;
        internal EventKey Key { get; } = key;
        internal int Count { get; private set; }
        // Subscription churn does not copy arrays. A publisher owns its captured array even
        // when a nested publish or suspended BTask observes a newer channel version.
        internal Subscription<T>[] Snapshot => _snapshot ??= CreateSnapshot();

        private Subscription<T>[] CreateSnapshot()
        {
            if (Count == 0) return Array.Empty<Subscription<T>>();
            var snapshot = new Subscription<T>[Count];
            var index = 0;
            for (var current = _first; current is not null; current = current.Next)
                snapshot[index++] = current;
            return snapshot;
        }

        internal void Add(Subscription<T> subscription)
        {
            subscription.Previous = _last;
            if (_last is null) _first = subscription;
            else _last.Next = subscription;
            _last = subscription;
            Count++;
            _snapshot = null;
        }

        internal void Remove(Subscription<T> subscription)
        {
            if (subscription.Previous is { } previous) previous.Next = subscription.Next;
            else _first = subscription.Next;
            if (subscription.Next is { } next) next.Previous = subscription.Previous;
            else _last = subscription.Previous;
            Count--;
            _snapshot = null;
        }

        public void DetachAll()
        {
            var current = _first;
            _first = _last = null;
            Count = 0;
            _snapshot = null;
            while (current is not null)
            {
                var next = current.Next;
                current.Detach();
                current = next;
            }
        }
    }

    // The public token is non-generic so retaining an already disposed token cannot retain
    // a collectible event type through Subscription<T>'s constructed runtime type.
    private sealed class SubscriptionHandle(IDisposable subscription) : IDisposable
    {
        private IDisposable? _subscription = subscription;
        public void Dispose()
        {
            var subscription = _subscription;
            _subscription = null;
            subscription?.Dispose();
        }
        internal void Detach() => _subscription = null;
    }

    private sealed class Subscription<T> : IDisposable
    {
        private EventBus? _owner;
        private Action<T>? _sync;
        private Func<T, BCancellationToken, BTask>? _async;
        internal Channel<T>? Channel { get; private set; }
        internal Subscription<T>? Previous { get; set; }
        internal Subscription<T>? Next { get; set; }
        internal SubscriptionHandle Handle { get; }
        internal Action<T>? SyncHandler => _sync;
        internal Func<T, BCancellationToken, BTask>? AsyncHandler => _async;

        internal Subscription(EventBus owner, Channel<T> channel, Action<T>? sync,
            Func<T, BCancellationToken, BTask>? async)
        {
            _owner = owner;
            Channel = channel;
            _sync = sync;
            _async = async;
            Handle = new SubscriptionHandle(this);
        }

        public void Dispose() => _owner?.Remove(this);

        internal void Detach()
        {
            _sync = null;
            _async = null;
            _owner = null;
            Channel = null;
            Previous = Next = null;
            Handle.Detach();
        }
    }

    private sealed class Waiter<T>(EventBus owner, BCancellationToken token) : IWaiter
    {
        private readonly BTaskCompletionSource<T> _completion = new();
        private EventBus? _owner = owner;
        private IDisposable? _subscription;
        private BCancellationRegistration _registration;
        private bool _done;
        internal BTask<T> Task => _completion.Task;

        internal void Attach(IDisposable subscription) => _subscription = subscription;

        internal void RegisterCancellation()
        {
            if (!token.CanBeCanceled) return;
            var registration = token.Register(Cancel);
            if (!_done) { _registration = registration; return; }
            registration.Dispose();
        }

        internal void Complete(T message)
        {
            if (Finish()) _completion.TrySetResult(message);
        }

        private void Cancel()
        {
            if (Finish()) _completion.TrySetCanceled(token);
        }

        public void CancelFromBus()
        {
            if (Finish()) _completion.TrySetCanceled();
        }

        private bool Finish()
        {
            if (_done) return false;
            _done = true;
            var subscription = _subscription;
            _subscription = null;
            var owner = _owner;
            _owner = null;
            var registration = _registration;
            _registration = default;
            subscription?.Dispose();
            owner?.RemoveWaiter(this);
            registration.Dispose();
            return true;
        }
    }
}
