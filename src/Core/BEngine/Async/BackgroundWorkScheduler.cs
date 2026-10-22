using System.Runtime.Loader;
using System.Reflection;

namespace BEngine;

public readonly record struct BackgroundWorkSnapshot(int Pending, int Active, int WorkerLimit,
    long Completed, long Rejected);

/// <summary>
/// Bounded pure-data work. Workers receive only a System cancellation token;
/// BTask completion and continuations always run on the owning frame thread.
/// Scene objects, engine services and graphics APIs must not be accessed by work.
/// </summary>
public sealed class BackgroundWorkScheduler : IDisposable
{
    private readonly Queue<Operation> _pending = new();
    private readonly List<Operation> _active = [];
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly int _workerLimit;
    private readonly int _maximumPending;
    private bool _subscribed, _disposed, _advancing;
    private long _completed, _rejected;

    private static BackgroundWorkScheduler? _main;
    public static BackgroundWorkScheduler Main => _main is { _disposed: false } ? _main : _main = new();
    public static bool IsSupported => !OperatingSystem.IsBrowser();
    public BackgroundWorkSnapshot Snapshot => new(_pending.Count, _active.Count, _workerLimit, _completed, _rejected);

    static BackgroundWorkScheduler() => Diagnostics.RuntimeDiagnosticRegistry.Register("budget.workers", "Background workers",
        new("pending", "Pending data work", Diagnostics.RuntimeDiagnosticUnit.Number, () => Main.Snapshot.Pending),
        new("active", "Active data work", Diagnostics.RuntimeDiagnosticUnit.Number, () => Main.Snapshot.Active),
        new("workers", "Worker limit", Diagnostics.RuntimeDiagnosticUnit.Number, () => Main.Snapshot.WorkerLimit),
        new("completed", "Completed data work", Diagnostics.RuntimeDiagnosticUnit.Number, () => Main.Snapshot.Completed),
        new("rejected", "Rejected data work", Diagnostics.RuntimeDiagnosticUnit.Number, () => Main.Snapshot.Rejected));

    public BackgroundWorkScheduler(int workerLimit = 2, int maximumPending = 128)
    {
        if (workerLimit is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(workerLimit));
        if (maximumPending is < 1 or > 8192) throw new ArgumentOutOfRangeException(nameof(maximumPending));
        _workerLimit = workerLimit; _maximumPending = maximumPending;
    }

    public BTask<T> Run<T>(Func<CancellationToken, T> work, BCancellationToken cancellationToken = default,
        Action<T>? discard = null)
        => RunOwned(work, work?.Method.Module.Assembly!, cancellationToken, discard);

    internal BTask<T> RunOwned<T>(Func<CancellationToken, T> work, Assembly owner,
        BCancellationToken cancellationToken = default, Action<T>? discard = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        CheckOwner(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsSupported) throw new PlatformNotSupportedException("Background data workers are unavailable on this platform.");
        cancellationToken.ThrowIfCancellationRequested();
        if (_pending.Count + _active.Count >= _maximumPending)
        { _rejected++; throw new InvalidOperationException("The background data queue is full."); }
        var operation = new Operation<T>(work, owner, cancellationToken, discard);
        _pending.Enqueue(operation);
        if (!_subscribed)
        {
            _subscribed = true;
            BTaskScheduler.Main.FrameAdvanced += Advance;
            Application.quitting += Dispose;
        }
        StartPending();
        return operation.Source.Task;
    }

    private void StartPending()
    {
        while (!_disposed && _active.Count < _workerLimit && _pending.TryDequeue(out var operation))
        {
            if (operation.Canceled) { operation.Complete(); _completed++; continue; }
            operation.Start(); _active.Add(operation);
        }
    }

    private void Advance(BFrameTick _)
    {
        CheckOwner();
        if (_advancing) return;
        _advancing = true;
        try
        {
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            var operation = _active[index];
            if (!operation.Finished) continue;
            _active.RemoveAt(index);
            operation.Complete(); _completed++;
        }
        StartPending();
        if (_pending.Count != 0 || _active.Count != 0 || !_subscribed) return;
        BTaskScheduler.Main.FrameAdvanced -= Advance;
        Application.quitting -= Dispose;
        _subscribed = false;
        }
        finally { _advancing = false; }
    }

    private void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Background data admission and completion belong to the owning engine thread.");
    }

    /// <summary>Cancels queued and running work. Running work must observe its token; it is never forcibly aborted.</summary>
    public void Dispose()
    {
        CheckOwner(); if (_disposed) return; _disposed = true;
        while (_pending.TryDequeue(out var operation))
        { operation.Cancel(); operation.Complete(); _completed++; }
        foreach (var operation in _active) operation.Cancel();
        // Keep the completion pump until workers acknowledge cancellation. A
        // canceled task must not release its input lease while a reader uses it.
        Advance(default);
    }

    private abstract class Operation
    {
        internal abstract bool Canceled { get; }
        internal abstract bool Finished { get; }
        internal abstract void Start();
        internal abstract void Cancel();
        internal abstract void Complete();
    }

    private sealed class Operation<T> : Operation
    {
        private Func<CancellationToken, T>? _work;
        private Action<T>? _discard;
        private readonly CancellationTokenSource _stop = new();
        private readonly BCancellationToken _token;
        private readonly BCancellationRegistration _registration;
        private readonly AssemblyLoadContext? _context;
        private Task<T>? _task;
        private bool _completed;
        internal readonly BTaskCompletionSource<T> Source = new();
        internal Operation(Func<CancellationToken, T> work, Assembly owner, BCancellationToken token, Action<T>? discard)
        {
            _work = work; _token = token; _discard = discard;
            _registration = token.Register(Cancel);
            _context = AssemblyLoadContext.GetLoadContext(owner);
            if (_context?.IsCollectible == true) _context.Unloading += Unloading;
        }
        internal override bool Canceled => _stop.IsCancellationRequested;
        internal override bool Finished => _task?.IsCompleted == true;
        internal override void Start()
        {
            var work = _work!; var token = _stop.Token;
            _task = Task.Run(() => { token.ThrowIfCancellationRequested(); return work(token); }, token);
            _work = null;
        }
        internal override void Cancel()
        {
            // Collectible-assembly unload may arrive on a CLR worker while
            // owner-thread completion is disposing this token source.
            try { if (!Volatile.Read(ref _completed)) _stop.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        private void Unloading(AssemblyLoadContext _) => Cancel();
        internal override void Complete()
        {
            if (_completed) return;
            _completed = true;
            try
            {
                // Observe the result even when the caller canceled, so worker
                // exceptions never become unobserved Task exceptions.
                if (_task is null || Canceled)
                {
                    _ = _task?.Exception;
                    if (_task is { IsCompletedSuccessfully: true }) _discard?.Invoke(_task.Result);
                    Source.TrySetCanceled(_token);
                }
                else Source.TrySetResult(_task.GetAwaiter().GetResult());
            }
            catch (OperationCanceledException) { Source.TrySetCanceled(_token); }
            catch (Exception error) { Source.TrySetException(error); }
            finally
            {
                _registration.Dispose();
                if (_context?.IsCollectible == true) _context.Unloading -= Unloading;
                _stop.Dispose(); _work = null; _discard = null; _task = null;
            }
        }
    }
}
