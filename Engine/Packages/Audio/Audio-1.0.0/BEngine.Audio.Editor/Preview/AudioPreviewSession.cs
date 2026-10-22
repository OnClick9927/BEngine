using BEngine.Editor;

namespace BEngine.Audio.Editor;

/// <summary>Owns an isolated output device; never changes the scene's AudioSource or global listener.</summary>
public sealed class AudioPreviewSession : IDisposable
{
    private readonly Func<IAudioOutput> _createOutput;
    private IAudioOutput? _output;
    private AudioClip? _clip;
    private float[] _buffer = [], _waveBuffer = [];
    private AudioVoiceReader? _playback;
    private AudioSampleReader? _waveReader;
    private readonly float[] _peaks = new float[192];
    private double _cursor, _lastUpdate, _fraction;
    private bool _disposed;
    private bool _waveStarted;
    public bool IsPlaying { get; private set; }
    public bool HasPendingWork => IsPlaying || _waveReader is not null || !_waveStarted && _clip is { isDestroyed: false, loadState: AudioDataLoadState.Loading };
    public bool Loop { get; set; }
    public float Volume { get; set; } = .8f;
    public double Speed { get; set; } = 1;
    public string Error { get; private set; } = "";
    public AudioClip? Clip => _clip;
    public double Time
    {
        get => _clip is { frequency: > 0 } clip ? _cursor / clip.frequency : 0;
        set { _cursor = Math.Clamp(double.IsFinite(value) ? value : 0, 0, (double)(_clip?.length ?? 0)) * (_clip?.frequency ?? 0); ReleaseOutput(); }
    }
    public AudioPreviewSession(Func<IAudioOutput>? createOutput = null)
    {
        _createOutput = createOutput ?? (() => AudioRuntimeServices.OutputFactory.CreateDefault());
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }
    public void SetClip(AudioClip? clip)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(_clip, clip)) return;
        Stop(); _waveReader?.Dispose(); _waveReader = null; _waveStarted = false; _clip = clip;
        Array.Clear(_peaks);
        if (!EditorApplication.isPlaying) AdvanceWaveform();
    }
    public void Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_clip is not { isDestroyed: false } || EditorApplication.isPlaying) return;
        if (_clip.loadState == AudioDataLoadState.Unloaded) _clip.LoadAudioData();
        if (_clip.loadState == AudioDataLoadState.Failed) { Error = _clip.loadError; return; }
        if (_cursor >= _clip.samples) _cursor = 0;
        IsPlaying = true; Error = ""; _lastUpdate = EditorApplication.timeSinceStartup;
    }
    public void Pause() { IsPlaying = false; ReleaseOutput(); }
    public void Stop() { Pause(); _cursor = 0; _playback?.Dispose(); _playback = null; }
    private void OnPlayMode(PlayModeStateChange state) { Stop(); _waveReader?.Dispose(); _waveReader = null; _waveStarted = false; }
    private void Update()
    {
        var now = EditorApplication.timeSinceStartup;
        var delta = Math.Clamp(now - _lastUpdate, 0, .1); _lastUpdate = now;
        if (EditorApplication.isPlaying) { Stop(); return; }
        Advance(delta);
    }
    public void Advance(double delta)
    {
        AdvanceWaveform();
        if (!IsPlaying) return;
        if (_clip?.loadState == AudioDataLoadState.Loading) return;
        if (_clip is not { isDestroyed: false, loadState: AudioDataLoadState.Loaded } clip) { Stop(); return; }
        try
        {
            _output ??= _createOutput();
            if (_playback is null || !_playback.IsCurrent) { _playback?.Dispose(); _playback = new AudioVoiceReader(clip); }
            if (_output.sampleRate <= 0 || _output.channelCount is < 1 or > 8) throw new InvalidOperationException("Invalid preview audio output format.");
            var exactFrames = Math.Clamp(double.IsFinite(delta) ? delta : 0, 0, .1) * _output.sampleRate + _fraction;
            var frames = (int)exactFrames; _fraction = exactFrames - frames;
            if (frames == 0) return;
            var count = checked(frames * _output.channelCount);
            if (_buffer.Length < count) _buffer = new float[count];
            var output = _buffer.AsSpan(0, count); output.Clear();
            var step = (double)clip.frequency / _output.sampleRate * Math.Clamp(Speed, .01, 3);
            using var playbackBatch = LoopExecutionGuard.AllowIterations(count);
            for (var frame = 0; frame < frames; frame++)
            {
                if (_cursor >= clip.samples)
                {
                    if (Loop) _cursor %= clip.samples;
                    else { IsPlaying = false; _cursor = clip.samples; break; }
                }
                if (!_playback.TryStereo((Fix64)_cursor, Loop, out var left, out var right)) break;
                if (_output.channelCount == 1) output[frame] = (left + right) * .5f * Math.Clamp(Volume, 0, 1);
                else
                { output[frame * _output.channelCount] = left * Math.Clamp(Volume, 0, 1); output[frame * _output.channelCount + 1] = right * Math.Clamp(Volume, 0, 1); }
                _cursor += step;
            }
            _output.Submit(output);
            if (!IsPlaying) { _playback?.Dispose(); _playback = null; }
        }
        catch (Exception exception) { Error = exception.Message; Stop(); }
    }
    public void DrawControls(AudioClip? clip)
    {
        SetClip(clip);
        GUILayout.Label("Editor Preview", EditorStyles.boldLabel);
        using var disabled = new EditorGUI.DisabledScope(clip is null || clip.isDestroyed || EditorApplication.isPlaying);
        var wave = GUILayoutUtility.GetControlRect(70);
        GUI.DrawRect(wave, new Color((Fix64).08, (Fix64).1, (Fix64).12));
        for (var index = 0; index < _peaks.Length; index++)
        {
            var height = (Fix64)Math.Clamp(_peaks[index], .015f, 1) * (wave.height - 8);
            GUI.DrawRect(new Rect(wave.x + wave.width * index / _peaks.Length,
                wave.y + (wave.height - height) / 2, Fix64.Max(1, wave.width / _peaks.Length - 1), height),
                new Color((Fix64).25, (Fix64).7, (Fix64).85));
        }
        if (clip is { isDestroyed: false } && clip.length > 0)
        {
            GUI.DrawRect(new Rect(wave.x + wave.width * (Fix64)Time / clip.length, wave.y, 1, wave.height), Color.white);
            if (GUI.enabled && wave.Contains(Event.current.mousePosition) && Event.current.button == 0 &&
                Event.current.type is EventType.MouseDown or EventType.MouseDrag)
            { Time = (double)((Event.current.mousePosition.x - wave.x) / wave.width * clip.length); Event.current.Use(); }
        }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(IsPlaying ? "Pause" : "Play")) { if (IsPlaying) Pause(); else Play(); }
        if (GUILayout.Button("Stop")) Stop();
        Loop = EditorGUILayout.Toggle("Loop", Loop);
        GUILayout.EndHorizontal();
        var time = EditorGUILayout.Slider("Time (s)", (float)Time, 0, Math.Max(.001f, (float)(clip?.length ?? 0)));
        if (Math.Abs(time - Time) > .001) Time = time;
        Volume = EditorGUILayout.Slider("Preview Volume", Volume, 0, 1);
        if (Error.Length > 0) GUILayout.Label(Error, EditorStyles.wordWrappedLabel);
    }
    private void ReleaseOutput() { (_output as IDisposable)?.Dispose(); _output = null; _fraction = 0; }
    private void AdvanceWaveform()
    {
        if (EditorApplication.isPlaying || _disposed) return;
        if (!_waveStarted && _clip is { isDestroyed: false, samples: > 0 } clip)
        {
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            if (clip.loadState == AudioDataLoadState.Loading) return;
            _waveStarted = true;
            if (clip.loadState == AudioDataLoadState.Failed) { Error = clip.loadError; return; }
            _waveReader = clip.OpenReader(); _waveBuffer = new float[4096 * clip.channels];
        }
        if (_waveReader is not { } reader) return;
        try
        {
            if (_clip is not { isDestroyed: false, loadState: AudioDataLoadState.Loaded })
            { reader.Dispose(); _waveReader = null; return; }
            var start = reader.position; var count = reader.Read(_waveBuffer, 0, _waveBuffer.Length);
            using var budget = LoopExecutionGuard.AllowIterations(count + 1);
            for (var i = 0; i < count; i++)
            {
                var column = Math.Min(_peaks.Length - 1, (int)((long)(start + i / reader.channels) * _peaks.Length / reader.samples));
                _peaks[column] = Math.Max(_peaks[column], Math.Abs(_waveBuffer[i]));
            }
            if (count == 0 || reader.position >= reader.samples) { reader.Dispose(); _waveReader = null; }
        }
        catch (Exception error) { Error = error.Message; reader.Dispose(); _waveReader = null; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        Stop(); _waveReader?.Dispose(); _waveReader = null; _clip = null; _waveBuffer = []; _buffer = []; _disposed = true;
        EditorApplication.update -= Update; EditorApplication.playModeStateChanged -= OnPlayMode;
    }
}
