using System.Diagnostics;
using System.Runtime.Loader;

namespace BEngine.Audio;

/// <summary>Outputs opting into the independent mixer must allow all buffered-output
/// operations on a single audio thread, and disposal after that thread has stopped.</summary>
public interface IIndependentAudioOutput : IAudioBufferedOutput { }

public readonly record struct IndependentAudioSnapshot(bool Running, string? FallbackReason,
    int PendingCommands, long MixedFrames, long QueueWaits, long ClockUnderruns, string? Error);

internal static class AudioIndependentClock
{
    private static AudioClockSession? _session;
    private static readonly Dictionary<AudioSource, AudioClockSource> Bindings = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<AudioSource> Present = new(ReferenceEqualityComparer.Instance);
    private static readonly List<AudioSource> Removed = [];
    private static string? _fallback;
    static AudioIndependentClock()
    {
        Application.quitting += Shutdown;
        var context = AssemblyLoadContext.GetLoadContext(typeof(AudioIndependentClock).Assembly);
        if (context?.IsCollectible == true) context.Unloading += _ => Shutdown();
    }
    internal static IndependentAudioSnapshot Snapshot => _session?.Snapshot(_fallback) ?? new(false, _fallback, 0, 0, 0, 0, null);
    internal static bool Synchronize(List<AudioSource> sources)
    {
        if (!AudioRuntimeSettings.useIndependentClock) return Fallback(null);
        if (OperatingSystem.IsBrowser()) return Fallback("This platform does not support an independent audio thread.");
        var output = AudioOutput.current;
        if (output is not IIndependentAudioOutput buffered)
            return Fallback("The output has not opted into thread-safe buffered mixing.");
        // Atomic frame fallback preserves mixer sends, tails, plugins and callbacks
        // rather than silently rendering them with reduced audio functionality.
        foreach (var source in sources)
            if (!source.CanUseIndependentClock(out var reason)) return Fallback(reason);
        if (_session is { } previous && (!ReferenceEquals(previous.Output, output) || previous.Error is not null))
        {
            var error = previous.Error; Shutdown();
            if (error is not null) return Fallback("Independent mixer failed: " + error);
        }
        if (buffered.sampleRate is < 8000 or > 384000 || buffered.channelCount is < 1 or > 8)
            return Fallback("The output has an unsupported independent mixing format.");
        _session ??= new(buffered);
        _fallback = null; Present.Clear();
        var session = _session;
        try
        {
            if (!session.Listener(AudioListener.pause, AudioListener.volume, AudioRuntimeSettings.maximumRealVoices))
                return Fallback("Independent mixer command queue is full.");
        }
        catch (InvalidOperationException listenerError)
        { return Fallback("Independent mixer command failed: " + listenerError.Message); }
        try
        {
            foreach (var source in sources)
            {
                // A failed command can shut the session down and detach every
                // source. Stop this synchronization pass before trying to add
                // another source to the disposed session.
                if (!ReferenceEquals(_session, session)) return Fallback("Independent mixer command queue failed.");
                Present.Add(source);
                if (!Bindings.TryGetValue(source, out var state))
                {
                    state = source.AttachIndependent(session); Bindings.Add(source, state);
                }
                source.SynchronizeIndependent(state);
                if (!ReferenceEquals(_session, session)) return Fallback("Independent mixer command queue failed.");
            }
        }
        catch (InvalidOperationException queueError)
        {
            return Fallback("Independent mixer command queue failed: " + queueError.Message);
        }
        Removed.Clear();
        foreach (var source in Bindings.Keys) if (!Present.Contains(source)) Removed.Add(source);
        foreach (var source in Removed) { source.DetachIndependent(); Bindings.Remove(source); }
        Removed.Clear(); Present.Clear();
        return true;
    }
    private static bool Fallback(string? reason) { Shutdown(); _fallback = reason; return false; }
    internal static bool Invalidate(AudioPlaybackData data, bool stop) => _session?.Invalidate(data, stop) ?? true;
    internal static void PublishListener()
    {
        if (_session is not { } session) return;
        if (session.Error is { } error) { Fallback("Independent mixer failed: " + error); return; }
        try
        {
            if (!session.Listener(AudioListener.pause, AudioListener.volume, AudioRuntimeSettings.maximumRealVoices))
                Fallback("Independent mixer command queue is full.");
        }
        catch (InvalidOperationException commandError)
        { Fallback("Independent mixer command failed: " + commandError.Message); }
    }
    internal static void Shutdown()
    {
        if (_session is not { } session) return;
        // Stop the clock before reading progress back into components or disposing
        // an output/ALC. The worker owns no Scene or component references.
        _session = null; session.Dispose();
        foreach (var source in Bindings.Keys) source.DetachIndependent(stopped: true);
        Bindings.Clear(); Present.Clear(); Removed.Clear();
    }
}

internal enum AudioClockCommandKind { Add, Remove, Parameters, Play, OneShot, Stop, Pause, Resume, Seek, Fade, CancelFade, Listener, Invalidate, Barrier }
internal readonly record struct AudioClockParameters(Fix64 Volume, Fix64 Pan, Fix64 Pitch, bool Loop,
    bool Muted, int Priority, int Id);
internal readonly record struct AudioClockCommand(AudioClockCommandKind Kind, AudioClockSource? Source = null,
    AudioPlaybackData? Data = null, AudioClockParameters Parameters = default, Fix64 A = default,
    Fix64 B = default, int Id = 0, bool Flag = false, ManualResetEventSlim? Barrier = null,
    AudioClockVoice? Voice = null, long Generation = 0);

internal sealed class AudioClockSession : IDisposable
{
    internal readonly IIndependentAudioOutput Output;
    private readonly object _gate = new();
    private readonly AudioClockCommand[] _commands = new AudioClockCommand[1024];
    private int _head, _count;
    private bool _stopping, _disposed;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private readonly List<AudioClockSource> _sources = [];
    private readonly PriorityQueue<AudioClockVoice, (int, Fix64, int, int, int)> _selected = new();
    private readonly List<AudioClockVoice> _voices = [];
    private readonly float[] _samples;
    private readonly int _blockFrames;
    private bool _paused;
    private Fix64 _volume = Fix64.One;
    private int _maximumVoices = 64;
    private (bool Pause, Fix64 Volume, int Maximum)? _lastListener;
    private long _mixedFrames, _waits, _underruns;
    private string? _error;
    internal string? Error => Volatile.Read(ref _error);
    internal AudioClockSession(IIndependentAudioOutput output)
    {
        if (output.sampleRate is < 8000 or > 384000 || output.channelCount is < 1 or > 8)
            throw new ArgumentException("Invalid independent audio output format.", nameof(output));
        Output = output; _blockFrames = Math.Max(1, output.sampleRate / 200);
        _samples = new float[checked(_blockFrames * output.channelCount)];
        _thread = new Thread(Run) { IsBackground = true, Name = "BEngine audio mixer" };
        _thread.Start();
    }
    internal IndependentAudioSnapshot Snapshot(string? fallback)
    { lock (_gate) return new(!_stopping && _error is null, fallback, _count, Interlocked.Read(ref _mixedFrames), Interlocked.Read(ref _waits), Interlocked.Read(ref _underruns), _error); }
    internal bool Enqueue(AudioClockCommand command)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping || _disposed, this);
            if (_error is not null) throw new InvalidOperationException("Independent audio mixer failed: " + _error);
            if (_count == _commands.Length)
            {
                // Parameter/listener updates are replaceable. Never make the
                // game thread wait for the mixer to consume a full queue.
                if ((command.Kind is AudioClockCommandKind.Parameters or AudioClockCommandKind.Listener) &&
                    TryReplacePending(command)) return true;
                // A burst of updates can fill the queue behind a stale
                // parameter command. Evict that stale command to admit a
                // lifecycle/voice command without blocking the frame thread.
                if (TryDropReplaceablePending())
                {
                    _commands[(_head + _count) % _commands.Length] = command;
                    _count++;
                    _wake.Set();
                    return true;
                }
                // A full queue must never turn into a game-thread wait or an
                // exception. Callers handle the rejected command by falling
                // back or marking the source for audio-thread cleanup.
                Interlocked.Increment(ref _waits);
                return false;
            }
            _commands[(_head + _count) % _commands.Length] = command; _count++;
        }
        _wake.Set();
        return true;
    }

    private bool TryReplacePending(AudioClockCommand command)
    {
        for (var index = _count - 1; index >= 0; index--)
        {
            var slot = (_head + index) % _commands.Length;
            var pending = _commands[slot];
            if (pending.Kind != command.Kind) continue;
            if (command.Kind == AudioClockCommandKind.Parameters && !ReferenceEquals(pending.Source, command.Source)) continue;
            _commands[slot] = command;
            return true;
        }
        return false;
    }

    private bool TryDropReplaceablePending()
    {
        for (var index = 0; index < _count; index++)
        {
            var slot = (_head + index) % _commands.Length;
            if (_commands[slot].Kind is not (AudioClockCommandKind.Parameters or AudioClockCommandKind.Listener)) continue;
            for (var move = index; move < _count - 1; move++)
            {
                var from = (_head + move + 1) % _commands.Length;
                var to = (_head + move) % _commands.Length;
                _commands[to] = _commands[from];
            }
            _commands[(_head + _count - 1) % _commands.Length] = default;
            _count--;
            return true;
        }
        return false;
    }
    internal bool Listener(bool pause, Fix64 volume, int maximum)
    {
        if (_lastListener == (pause, volume, maximum)) return true;
        if (!Enqueue(new(AudioClockCommandKind.Listener, A: volume, Id: maximum, Flag: pause))) return false;
        _lastListener = (pause, volume, maximum);
        return true;
    }
    internal bool Remove(AudioClockSource source)
    {
        try { if (Enqueue(new(AudioClockCommandKind.Remove, source))) return Flush(); }
        catch (InvalidOperationException) { }
        // The source is no longer owned by a scene. Let the mixer prune it at
        // its next command boundary even when a burst has filled the ring.
        Volatile.Write(ref source.Detached, true); _wake.Set(); return false;
    }
    internal bool Invalidate(AudioPlaybackData data, bool stop)
    {
        try
        {
            if (!Enqueue(new(AudioClockCommandKind.Invalidate, Data: data, Flag: stop))) return false;
            return Flush();
        }
        catch (InvalidOperationException) { return false; }
    }
    // A mutation of AudioClip PCM must not race an independent reader. Keep
    // the acknowledgement short and bounded so a stalled output cannot turn a
    // gameplay frame into an unbounded wait; callers can retry or fall back.
    private bool Flush(int milliseconds = 4)
    {
        if (Thread.CurrentThread == _thread) return true;
        var barrier = new ManualResetEventSlim(false);
        try { if (!Enqueue(new(AudioClockCommandKind.Barrier, Barrier: barrier))) return false; }
        catch (InvalidOperationException) { return false; }
        var deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * Math.Max(1, milliseconds) / 1000d);
        while (!barrier.IsSet)
        {
            var remaining = deadline - Stopwatch.GetTimestamp();
            if (remaining <= 0 || !_thread.IsAlive) return false;
            var wait = Math.Max(1, (int)Math.Min(1, remaining * 1000d / Stopwatch.Frequency));
            barrier.Wait(wait);
        }
        return Error is null;
    }
    private bool Take(out AudioClockCommand command)
    {
        lock (_gate)
        {
            if (_count == 0) { command = default; return false; }
            command = _commands[_head]; _commands[_head] = default;
            _head = (_head + 1) % _commands.Length; _count--; return true;
        }
    }
    private void Apply(AudioClockCommand command)
    {
        switch (command.Kind)
        {
            case AudioClockCommandKind.Add: _sources.Add(command.Source!); break;
            case AudioClockCommandKind.Remove: _sources.Remove(command.Source!); command.Source!.CloseReaders(); break;
            case AudioClockCommandKind.Listener: _paused = command.Flag; _volume = command.A; _maximumVoices = command.Id; break;
            case AudioClockCommandKind.Invalidate:
                foreach (var source in _sources) source.Invalidate(command.Data!, command.Flag); break;
            case AudioClockCommandKind.Barrier: command.Barrier!.Set(); break;
            default: command.Source!.Apply(command); break;
        }
    }
    private void Run()
    {
        var period = (double)_blockFrames / Output.sampleRate;
        var next = Stopwatch.GetTimestamp();
        try
        {
            while (true)
            {
                while (Take(out var command)) Apply(command);
                PruneDetachedAndInvalid();
                lock (_gate) if (_stopping) break;
                var now = Stopwatch.GetTimestamp();
                if (_paused || _sources.Count == 0)
                { next = now; _wake.WaitOne(5); continue; }
                var until = (next - now) / (double)Stopwatch.Frequency;
                if (until > 0) { _wake.WaitOne(Math.Max(1, (int)Math.Ceiling(until * 1000))); continue; }
                // A bounded output is the only producer-consumer boundary. Existing
                // waveOut/AAudio threads retain ownership of device writes.
                Output.SetExpectedSubmissionFrames(_blockFrames);
                var count = Math.Min(_blockFrames, Output.availableFrames);
                if (count == 0) { next = now; _wake.WaitOne(2); continue; }
                Render(count);
                next += (long)(period * Stopwatch.Frequency * count / _blockFrames);
                if ((now - next) / (double)Stopwatch.Frequency > .04)
                { Interlocked.Increment(ref _underruns); next = now; }
            }
        }
        catch (Exception error) { Volatile.Write(ref _error, error.ToString()); }
        finally
        {
            foreach (var source in _sources) source.CloseReaders();
            _sources.Clear();
            while (Take(out var pending)) pending.Barrier?.Set();
            lock (_gate) { _stopping = true; Monitor.PulseAll(_gate); }
        }
    }
    private void PruneDetachedAndInvalid()
    {
        for (var index = _sources.Count - 1; index >= 0; index--)
        {
            var source = _sources[index];
            if (Volatile.Read(ref source.Detached))
            {
                source.CloseReaders(); _sources.RemoveAt(index); continue;
            }
            source.CleanupInvalidData();
        }
    }
    private void Render(int frames)
    {
        var samples = _samples.AsSpan(0, frames * Output.channelCount); samples.Clear();
        _voices.Clear(); _selected.Clear();
        foreach (var source in _sources) source.Gather(_voices);
        foreach (var voice in _voices)
        {
            var rank = voice.Rank; voice.Real = _maximumVoices == 0;
            if (_maximumVoices == 0) continue;
            if (_selected.Count < _maximumVoices) _selected.Enqueue(voice, rank);
            else if (_selected.TryPeek(out _, out var worst) && rank.CompareTo(worst) > 0)
            { _selected.Dequeue(); _selected.Enqueue(voice, rank); }
        }
        foreach (var item in _selected.UnorderedItems) item.Element.Real = true;
        foreach (var source in _sources) source.Mix(samples, Output.sampleRate, Output.channelCount, frames);
        var gain = (float)_volume;
        for (var i = 0; i < samples.Length; i++) { var sample = samples[i] * gain; samples[i] = float.IsFinite(sample) ? sample : 0; }
        // This session is the sole submitter; availability cannot shrink except
        // through a faulty/custom output. Do not discard an accepted prefix's tail.
        var offset = 0;
        while (offset < frames)
        {
            Output.TrySubmit(samples[(offset * Output.channelCount)..], out var accepted);
            if (accepted < 0 || accepted > frames - offset) throw new InvalidDataException("Invalid audio output acceptance count.");
            offset += accepted;
            if (offset == frames) break;
            lock (_gate) if (_stopping) return;
            while (Take(out var command)) Apply(command);
            _wake.WaitOne(1);
        }
        Interlocked.Add(ref _mixedFrames, frames);
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _stopping = true; Monitor.PulseAll(_gate); }
        _wake.Set();
        if (Thread.CurrentThread != _thread) _thread.Join();
        _wake.Dispose();
    }
}

internal sealed class AudioClockSource
{
    internal readonly AudioClockSession Session;
    internal bool Detached;
    internal AudioClockParameters Parameters;
    internal long RequestedGeneration, AppliedGeneration;
    internal readonly AudioClockVoice Main = new();
    internal AudioPlaybackData? MainData => Main.Data;
    internal AudioPlaybackData? RequestedData;
    internal readonly List<AudioClockVoice> OneShots = [];
    internal bool Paused, Fading, StopAfterFade;
    internal Fix64 Delay, FadeStart, FadeTarget, FadeDuration, FadeElapsed;
    private long _position, _volume;
    private int _playing, _mainPlaying, _oneShots, _fading, _realVoices;
    internal Fix64 Position => Fix64.FromRaw(Interlocked.Read(ref _position));
    internal Fix64 CurrentVolume => Fix64.FromRaw(Interlocked.Read(ref _volume));
    internal bool IsPlaying => Volatile.Read(ref _playing) != 0;
    internal bool MainPlaying => Volatile.Read(ref _mainPlaying) != 0;
    internal int RealVoices => Volatile.Read(ref _realVoices);
    internal bool IsFading => Volatile.Read(ref _fading) != 0;
    internal int OneShotCount => Volatile.Read(ref _oneShots);
    internal AudioClockSource(AudioClockSession session, AudioClockParameters parameters)
    { Session = session; Parameters = parameters; _volume = parameters.Volume.RawValue; }
    internal void Apply(AudioClockCommand command)
    {
        switch (command.Kind)
        {
            case AudioClockCommandKind.Parameters:
                Parameters = command.Parameters with { Volume = command.Flag ? command.Parameters.Volume : Parameters.Volume }; break;
            case AudioClockCommandKind.Play:
                Main.Dispose(); Main.Data = command.Data; RequestedData = command.Data; if (!command.Flag) Main.Cursor = 0; Main.Playing = true; Paused = false; Delay = command.A; break;
            case AudioClockCommandKind.OneShot:
                OneShots.Add(command.Voice!); break;
            case AudioClockCommandKind.Stop: Stop(); break;
            case AudioClockCommandKind.Pause: Paused = Main.Playing || OneShots.Count > 0; CloseReaders(); break;
            case AudioClockCommandKind.Resume: Paused = false; break;
            case AudioClockCommandKind.Seek: Main.Cursor = command.A; Main.Dispose(); break;
            case AudioClockCommandKind.Fade:
                FadeStart = Parameters.Volume; FadeTarget = command.A; FadeDuration = command.B; FadeElapsed = 0; StopAfterFade = command.Flag;
                Fading = command.B > 0;
                if (!Fading) { Parameters = Parameters with { Volume = FadeTarget }; if (StopAfterFade) Stop(); } break;
            case AudioClockCommandKind.CancelFade: Fading = false; break;
        }
        Publish();
        if (command.Generation != 0) Volatile.Write(ref AppliedGeneration, command.Generation);
    }
    private void Stop()
    {
        Main.Playing = false; Main.Cursor = 0; Main.Dispose();
        foreach (var voice in OneShots) voice.Dispose(); OneShots.Clear();
        Paused = false; Delay = 0; Fading = false;
    }
    internal void Invalidate(AudioPlaybackData data, bool stop)
    {
        if (ReferenceEquals(Main.Data, data)) { if (stop) Main.Playing = false; Main.Dispose(); }
        for (var i = OneShots.Count - 1; i >= 0; i--)
            if (ReferenceEquals(OneShots[i].Data, data)) { OneShots[i].Dispose(); OneShots.RemoveAt(i); }
        Publish();
    }
    internal void Gather(List<AudioClockVoice> voices)
    {
        if (Volatile.Read(ref Detached)) return;
        if (Paused) { Main.Real = false; foreach (var voice in OneShots) voice.Real = false; return; }
        if (Main.Playing) Add(Main);
        foreach (var voice in OneShots) Add(voice);
        void Add(AudioClockVoice voice)
        {
            voice.Rank = (-Math.Clamp(Parameters.Priority, 0, 256), Parameters.Muted ? Fix64.Zero : Mathf.Clamp01(Parameters.Volume * voice.Volume), voice.Real ? 1 : 0, -Parameters.Id, -voice.Id);
            voice.Real = false;
            if (!Parameters.Muted && Parameters.Volume > 0 && voice.Volume > 0) voices.Add(voice);
        }
    }
    internal void CleanupInvalidData()
    {
        var changed = false;
        if (Main.Data is { IsValid: false }) { Main.Playing = false; Main.Dispose(); changed = true; }
        for (var index = OneShots.Count - 1; index >= 0; index--)
            if (OneShots[index].Data is { IsValid: false })
            { OneShots[index].Playing = false; OneShots[index].Dispose(); OneShots.RemoveAt(index); changed = true; }
        if (changed) Publish();
    }
    internal void Mix(Span<float> output, int rate, int channels, int frames)
    {
        if (Paused) return;
        var delta = (Fix64)frames / rate;
        if (Fading)
        {
            FadeElapsed += delta;
            Parameters = Parameters with { Volume = Mathf.Lerp(FadeStart, FadeTarget, Mathf.Clamp01(FadeElapsed / FadeDuration)) };
            if (FadeElapsed >= FadeDuration) { Fading = false; if (StopAfterFade) Stop(); }
        }
        var delay = Math.Min(frames, Mathf.CeilToInt(Delay * rate)); Delay = Fix64.Max(0, Delay - (Fix64)delay / rate);
        var gain = Parameters.Muted ? Fix64.Zero : Mathf.Clamp01(Parameters.Volume);
        if (Main.Playing) Main.Mix(output[(delay * channels)..], rate, channels, frames - delay, Parameters, gain, Parameters.Loop);
        for (var i = OneShots.Count - 1; i >= 0; i--)
        {
            var voice = OneShots[i]; voice.Mix(output, rate, channels, frames, Parameters, gain * voice.Volume, false);
            if (!voice.Playing) { voice.Dispose(); OneShots.RemoveAt(i); }
        }
        Publish();
    }
    internal void Publish()
    {
        Interlocked.Exchange(ref _position, Main.Cursor.RawValue); Interlocked.Exchange(ref _volume, Parameters.Volume.RawValue);
        Volatile.Write(ref _playing, !Paused && (Main.Playing || OneShots.Count > 0) ? 1 : 0);
        Volatile.Write(ref _mainPlaying, Main.Playing ? 1 : 0);
        Volatile.Write(ref _oneShots, OneShots.Count); Volatile.Write(ref _fading, Fading ? 1 : 0);
        var real = Main.Real && Main.Playing ? 1 : 0;
        foreach (var voice in OneShots) if (voice.Real) real++;
        Volatile.Write(ref _realVoices, real);
    }
    internal void CloseReaders() { Main.Dispose(); foreach (var voice in OneShots) voice.Dispose(); }
}

internal sealed class AudioClockVoice : IDisposable
{
    private static readonly Fix64 MinimumPitch = Fix64.Parse("0.01");
    internal AudioPlaybackData? Data;
    internal AudioVoiceReader? Reader;
    internal Fix64 Cursor, Volume = Fix64.One;
    internal bool Playing, Real;
    private bool _virtual;
    internal int Id;
    internal (int, Fix64, int, int, int) Rank;
    internal void Mix(Span<float> output, int rate, int channels, int frames, AudioClockParameters parameters, Fix64 gain, bool loop)
    {
        if (!Playing || frames == 0) return;
        if (Data is not { IsValid: true } data) { Playing = false; Dispose(); return; }
        var step = (Fix64)data.Frequency / rate * Fix64.Clamp(parameters.Pitch, MinimumPitch, 3);
        if (!Real || gain <= 0)
        {
            Reader?.WriteFadeOut(output, channels); Dispose(); _virtual = true;
            Cursor += step * frames; Finish(data, loop); return;
        }
        Reader ??= new(data, _virtual); _virtual = false;
        var pan = Fix64.Clamp(parameters.Pan, -1, 1);
        var leftGain = (float)(gain * (pan > 0 ? 1 - pan : 1));
        var rightGain = (float)(gain * (pan < 0 ? 1 + pan : 1));
        if (Reader.TryMixPcm(output, channels, step, leftGain, rightGain, loop, ref Cursor, out var ended))
        { if (ended) { Playing = false; Dispose(); } return; }
        for (var i = 0; i < frames; i++)
        {
            if (Cursor >= data.Samples) { if (!loop) break; Cursor %= data.Samples; }
            if (!Reader.TryStereo(Cursor, loop, out var left, out var right)) break;
            if (channels == 1) output[i] += (left * leftGain + right * rightGain) * .5f;
            else { output[i * channels] += left * leftGain; output[i * channels + 1] += right * rightGain; }
            Reader.LastLeft = left * leftGain; Reader.LastRight = right * rightGain; Cursor += step;
        }
        Finish(data, loop);
    }
    private void Finish(AudioPlaybackData data, bool loop)
    { if (loop) Cursor %= data.Samples; else if (Cursor >= data.Samples) { Cursor = data.Samples; Playing = false; Dispose(); } }
    public void Dispose() { Reader?.Dispose(); Reader = null; }
}
