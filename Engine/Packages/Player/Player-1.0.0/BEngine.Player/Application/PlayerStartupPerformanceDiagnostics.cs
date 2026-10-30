using System.Diagnostics;
using System.Threading;

namespace BEngine.Player;

/// <summary>Optional, bounded synchronous startup timings. Nested samples are not additive.</summary>
public static class PlayerStartupPerformanceDiagnostics
{
    private const int MaximumSamples = 128;
    private static readonly object Gate = new();
    private static List<PendingSample>? Samples;
    private static bool _enabled;
    private static int _generation;
    [ThreadStatic] private static int _parentId;

    public static bool Enabled
    {
        get => Volatile.Read(ref _enabled);
        set => Volatile.Write(ref _enabled, value);
    }

    public static void Reset()
    {
        lock (Gate)
        {
            Samples?.Clear();
            unchecked { _generation++; }
            _parentId = 0;
        }
    }

    public static PlayerStartupPerformanceSample[] Snapshot
    {
        get
        {
            lock (Gate)
                return Samples?.Where(static sample => sample.Completed)
                    .Select(static sample => new PlayerStartupPerformanceSample(
                        sample.Id, sample.ParentId, sample.Name, sample.Milliseconds)).ToArray() ?? [];
        }
    }

    internal static Scope Begin(string name)
    {
        if (!Enabled) return default;
        lock (Gate)
        {
            if (!Enabled) return default;
            Samples ??= new(MaximumSamples);
            if (Samples.Count == MaximumSamples) return default;
            var id = Samples.Count + 1;
            var parent = _parentId;
            Samples.Add(new(id, parent, name));
            _parentId = id;
            return new(id, parent, _generation, Stopwatch.GetTimestamp());
        }
    }

    internal readonly struct Scope(int id, int parentId, int generation, long started) : IDisposable
    {
        public void Dispose()
        {
            if (id == 0) return;
            lock (Gate)
            {
                if (generation != _generation || Samples is null || id > Samples.Count) return;
                var sample = Samples[id - 1];
                if (sample.Completed) return;
                sample.Milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                sample.Completed = true;
                if (_parentId == id) _parentId = parentId;
            }
        }
    }

    private sealed class PendingSample(int id, int parentId, string name)
    {
        internal readonly int Id = id;
        internal readonly int ParentId = parentId;
        internal readonly string Name = name;
        internal bool Completed;
        internal double Milliseconds;
    }
}

public readonly record struct PlayerStartupPerformanceSample(int Id, int ParentId, string Name, double Milliseconds);
