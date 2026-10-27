using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace BEngine.Audio;

/// <summary>Opt-in, bounded attribution for decoder opens, exact seeks and voice eviction.
/// Set BENGINE_AUDIO_PLAYBACK_PROFILE=1 before loading Audio to enable startup profiling.
/// Disabled playback does not create measurements or read clocks/allocation counters.</summary>
public static class AudioPlaybackDiagnostics
{
    private const int Capacity = 128;
    private const double SlowOperationMilliseconds = 16;
    private static readonly object Gate = new();
    private static int _enabled = Environment.GetEnvironmentVariable("BENGINE_AUDIO_PLAYBACK_PROFILE") == "1" ? 1 : 0;
    private static int _generation;
    private static Event[]? _events;
    private static int _next, _count;
    private static long _sequence;
    private static long _decoderOpens, _backwardSeeks, _forwardSeeks, _voiceResumes, _evictions;

    public static bool enabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        set
        {
            lock (Gate)
            {
                Volatile.Write(ref _enabled, value ? 1 : 0);
                ResetCore();
            }
        }
    }

    /// <summary>Clears retained labels and invalidates measurements already in progress.</summary>
    public static void Reset() { lock (Gate) ResetCore(); }
    private static void ResetCore()
    {
        Interlocked.Increment(ref _generation);
        if (_events is not null) Array.Clear(_events);
        _next = _count = 0; _sequence = 0;
        _decoderOpens = _backwardSeeks = _forwardSeeks = _voiceResumes = _evictions = 0;
    }

    /// <summary>Creates JSON only on explicit inspection; no reflection serializer is required
    /// when Audio was loaded dynamically in a browser player.</summary>
    public static string GetSnapshotJson()
    {
        lock (Gate)
        {
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                writer.WriteStartObject();
                writer.WriteBoolean("Enabled", enabled);
                writer.WriteNumber("Capacity", Capacity);
                writer.WriteNumber("Operations", _sequence);
                writer.WriteNumber("DecoderOpens", _decoderOpens); writer.WriteNumber("BackwardSeeks", _backwardSeeks);
                writer.WriteNumber("ForwardSeeks", _forwardSeeks); writer.WriteNumber("VoiceResumes", _voiceResumes);
                writer.WriteNumber("Evictions", _evictions);
                writer.WriteNumber("TimestampFrequency", Stopwatch.Frequency);
                writer.WriteStartArray("Events");
                for (var i = 0; i < _count; i++) WriteEvent(writer, _events![(_next - _count + i + Capacity) % Capacity]);
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(bytes.GetBuffer(), 0, checked((int)bytes.Length));
        }
    }

    internal enum Operation { DecoderOpen, VorbisBackwardSeek, VorbisForwardSeek, MpegBackwardSeek, MpegForwardSeek, VoiceOpen, VoiceResume, VirtualEvict, MutedEvict }

    internal static Measurement Begin(Operation operation, string? clipName, int fromFrame = -1, int targetFrame = 0)
    {
        if (!enabled) return default;
        return new(operation, clipName, fromFrame, targetFrame, Volatile.Read(ref _generation));
    }

    internal static void Mark(Operation operation, string? clipName, int fromFrame, int targetFrame)
    {
        if (!enabled) return;
        Add(new(0, operation, clipName, Time.frameCount, fromFrame, targetFrame, Stopwatch.GetTimestamp(), 0, 0), Volatile.Read(ref _generation));
    }

    internal readonly struct Measurement : IDisposable
    {
        private readonly bool _active;
        private readonly Operation _operation;
        private readonly string? _clipName;
        private readonly int _fromFrame, _targetFrame, _generation;
        private readonly long _frame, _started, _allocated;
        internal Measurement(Operation operation, string? clipName, int fromFrame, int targetFrame, int generation)
        {
            _active = true; _operation = operation; _clipName = clipName;
            _fromFrame = fromFrame; _targetFrame = targetFrame; _generation = generation;
            _frame = Time.frameCount; _allocated = GC.GetAllocatedBytesForCurrentThread();
            _started = Stopwatch.GetTimestamp();
        }
        public void Dispose()
        {
            if (!_active) return;
            var milliseconds = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            var allocated = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - _allocated);
            Add(new(0, _operation, _clipName, _frame, _fromFrame, _targetFrame, _started, milliseconds, allocated), _generation);
        }
    }

    private readonly record struct Event(long Sequence, Operation Operation, string? ClipName, long Frame,
        int FromFrame, int TargetFrame, long StartedTimestamp, double Milliseconds, long AllocatedBytes);

    private static void Add(Event entry, int generation)
    {
        lock (Gate)
        {
            if (!enabled || generation != _generation) return;
            _events ??= new Event[Capacity];
            entry = entry with { Sequence = ++_sequence };
            switch (entry.Operation)
            {
                case Operation.DecoderOpen: _decoderOpens++; break;
                case Operation.VorbisBackwardSeek or Operation.MpegBackwardSeek: _backwardSeeks++; break;
                case Operation.VorbisForwardSeek or Operation.MpegForwardSeek: _forwardSeeks++; break;
                case Operation.VoiceResume: _voiceResumes++; break;
                case Operation.VirtualEvict or Operation.MutedEvict: _evictions++; break;
            }
            _events[_next] = entry; _next = (_next + 1) % Capacity; _count = Math.Min(Capacity, _count + 1);
        }
        if (entry.Milliseconds <= SlowOperationMilliseconds) return;
        // Never let a diagnostic listener change decoder error/ownership semantics.
        try
        {
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes)) WriteEvent(writer, entry);
            Debug.Log("BENGINE_AUDIO_PROFILE|" + Encoding.UTF8.GetString(bytes.GetBuffer(), 0, checked((int)bytes.Length)));
        }
        catch { }
    }

    private static void WriteEvent(Utf8JsonWriter writer, Event entry)
    {
        writer.WriteStartObject(); writer.WriteNumber("Sequence", entry.Sequence);
        writer.WriteString("Operation", entry.Operation.ToString()); writer.WriteString("ClipName", entry.ClipName);
        writer.WriteNumber("Frame", entry.Frame); writer.WriteNumber("FromFrame", entry.FromFrame);
        writer.WriteNumber("TargetFrame", entry.TargetFrame); writer.WriteNumber("StartedTimestamp", entry.StartedTimestamp);
        writer.WriteNumber("Milliseconds", entry.Milliseconds); writer.WriteNumber("AllocatedBytes", entry.AllocatedBytes);
        writer.WriteEndObject();
    }
}
