using System.Diagnostics;

namespace BEngine.Editor;

/// <summary>A process-wide, scoped startup trace. The creator owns stage changes,
/// completion and disposal; package compilation may measure details on workers.
/// This recorder does not choose an output location or write files.</summary>
public sealed class EditorStartupTrace : IDisposable
{
    private static EditorStartupTrace? _current;
    private readonly object _gate = new();
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly List<EditorStartupTiming> _stages = [];
    private readonly List<EditorStartupTiming> _details = [];
    private long _stageStarted;
    private string _stage;
    private bool _finished;
    private EditorStartupTraceSnapshot? _snapshot;

    private EditorStartupTrace(string initialStage)
    {
        _stage = initialStage;
        _stageStarted = _started;
    }

    public static EditorStartupTrace Begin(string initialStage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(initialStage);
        var trace = new EditorStartupTrace(initialStage);
        if (Interlocked.CompareExchange(ref _current, trace, null) is not null)
            throw new InvalidOperationException("A startup trace is already active.");
        return trace;
    }

    public void Mark(string stage)
    {
        VerifyOwner();
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_finished, this);
            var now = Stopwatch.GetTimestamp();
            AddStage(now);
            _stage = stage;
            _stageStarted = now;
        }
    }

    /// <summary>Records one detail until the returned scope is disposed. Scopes
    /// may finish on a worker; late scopes cannot write into a completed or newer trace.</summary>
    public IDisposable Measure(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_finished, this);
            return new Measurement(this, name);
        }
    }

    public EditorStartupTraceSnapshot Complete()
    {
        VerifyOwner();
        lock (_gate)
        {
            if (_snapshot is not null) return _snapshot;
            ObjectDisposedException.ThrowIf(_finished, this);
            var now = Stopwatch.GetTimestamp();
            AddStage(now);
            _snapshot = new EditorStartupTraceSnapshot(
                Stopwatch.GetElapsedTime(_started, now).TotalMilliseconds,
                Array.AsReadOnly(_stages.ToArray()), Array.AsReadOnly(_details.ToArray()));
            Finish();
            return _snapshot;
        }
    }

    public void Dispose()
    {
        VerifyOwner();
        lock (_gate)
        {
            if (!_finished) Finish();
        }
    }

    internal static void MarkCurrent(string stage) => Volatile.Read(ref _current)?.Mark(stage);

    internal static IDisposable MeasureCurrent(string name)
    {
        var trace = Volatile.Read(ref _current);
        if (trace is null) return EmptyMeasurement.Instance;
        lock (trace._gate)
            return trace._finished ? EmptyMeasurement.Instance : new Measurement(trace, name);
    }

    private void AddStage(long now) => _stages.Add(new(_stage,
        Stopwatch.GetElapsedTime(_started, _stageStarted).TotalMilliseconds,
        Stopwatch.GetElapsedTime(_stageStarted, now).TotalMilliseconds));

    private void Finish()
    {
        _finished = true;
        Interlocked.CompareExchange(ref _current, null, this);
        _stages.Clear();
        _details.Clear();
    }

    private void VerifyOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Startup trace lifecycle belongs to its creating thread.");
    }

    private sealed class Measurement(EditorStartupTrace trace, string name) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            var end = Stopwatch.GetTimestamp();
            lock (trace._gate)
                if (!trace._finished)
                    trace._details.Add(new(name,
                        Stopwatch.GetElapsedTime(trace._started, _start).TotalMilliseconds,
                        Stopwatch.GetElapsedTime(_start, end).TotalMilliseconds));
        }
    }

    private sealed class EmptyMeasurement : IDisposable
    {
        internal static readonly EmptyMeasurement Instance = new();
        public void Dispose() { }
    }
}

public sealed record EditorStartupTiming(string Name, double StartMilliseconds, double Milliseconds);

public sealed class EditorStartupTraceSnapshot
{
    internal EditorStartupTraceSnapshot(double milliseconds, IReadOnlyList<EditorStartupTiming> stages,
        IReadOnlyList<EditorStartupTiming> details) =>
        (ManagedStartupMilliseconds, Stages, Details) = (milliseconds, stages, details);
    public double ManagedStartupMilliseconds { get; }
    public IReadOnlyList<EditorStartupTiming> Stages { get; }
    public IReadOnlyList<EditorStartupTiming> Details { get; }
}

// Core producers record neutral stages/details without depending on a workbench.
internal static class EditorStartupTimings
{
    internal static void Mark(string stage) => EditorStartupTrace.MarkCurrent(stage);
    internal static IDisposable Measure(string name) => EditorStartupTrace.MeasureCurrent(name);
}
