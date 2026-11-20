using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace BEngine.Player.Hosts.Android;

/// <summary>Stops an embedded runtime before its Android render thread can exit.</summary>
internal sealed class AndroidRuntimeThread(Action<Action> dispatch, Action stop)
{
    private readonly ManualResetEventSlim _stopped = new(false);
    private int _ownerThread;
    private int _stopRequested;
    private int _stopStarted;
    private ExceptionDispatchInfo? _failure;

    internal bool IsStopping => Volatile.Read(ref _stopRequested) != 0;

    internal void BindCurrentThread()
    {
        var current = Environment.CurrentManagedThreadId;
        var owner = Interlocked.CompareExchange(ref _ownerThread, current, 0);
        if (owner != 0 && owner != current)
            throw new InvalidOperationException("The Android runtime belongs to a different render thread.");
    }

    internal void StopOnOwner()
    {
        BindCurrentThread();
        Volatile.Write(ref _stopRequested, 1);
        if (Interlocked.Exchange(ref _stopStarted, 1) != 0) return;
        try { stop(); }
        catch (Exception error) { _failure = ExceptionDispatchInfo.Capture(error); }
        finally { _stopped.Set(); }
    }

    internal void StopAndWait()
    {
        if (Environment.CurrentManagedThreadId == Volatile.Read(ref _ownerThread))
            StopOnOwner();
        else if (Interlocked.Exchange(ref _stopRequested, 1) == 0)
            dispatch(StopOnOwner);

        // GLSurfaceView also waits for its GL thread when it pauses or detaches.
        // Acknowledging here first prevents a queued cleanup being discarded by
        // that thread's exit, or being run later on an unrelated UI thread.
        _stopped.Wait();
        _failure?.Throw();
    }
}
