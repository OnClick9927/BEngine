using BEngine.Audio.Editor;
using BEngine.Editor;

namespace BEngine.Video.Editor;

public sealed class VideoPreviewSession : IDisposable
{
    private readonly AudioPreviewSession _audio = new();
    private VideoClip? _clip;
    private Texture? _texture;
    private byte[] _rgba = [];
    private double _time, _lastUpdate;
    private int _frame = -1;
    private bool _disposed;
    private VideoFrameReader? _reader;
    private AsyncVideoFrameReader? _asyncReader;
    private double _requestedTime = double.NaN;
    private double _presentedTimestamp = double.NegativeInfinity;
    public bool IsPlaying { get; private set; }
    public bool HasPendingWork => IsPlaying || _asyncReader?.HasPendingWork == true;
    public bool Loop { get; set; }
    public string Error { get; private set; } = "";
    public Texture? FrameTexture => _texture;
    public int Frame => _frame;
    public double Time
    {
        get => _time;
        set
        {
            _time = Math.Clamp(double.IsFinite(value) ? value : 0, 0, (double)(_clip?.length ?? 0));
            _audio.Time = _time; Present();
        }
    }
    public VideoPreviewSession()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }
    public void SetClip(VideoClip? clip)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(_clip, clip)) return;
        Stop(); ReleaseFrame(); _clip = clip; Error = "";
        _audio.SetClip(clip?.audioClip);
        if (clip is not { isDestroyed: false }) return;
        try
        {
            if (clip.isNative)
            {
                _asyncReader = clip.OpenAsyncReader();
                _ = _asyncReader.Prepared.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
            else _reader = clip.OpenReader();
            _rgba = new byte[checked(clip.width * clip.height * 4)];
            _texture = Texture.FromRgba32(clip.width, clip.height, _rgba); _texture.hideFlags = HideFlags.HideAndDontSave; Present();
        }
        catch (Exception exception) { Error = exception.Message; ReleaseFrame(); }
    }
    public void Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_clip is not { isDestroyed: false } || _reader is null && _asyncReader is null || Error.Length > 0) return;
        if (_time >= (double)_clip.length) Time = 0;
        IsPlaying = true; _lastUpdate = EditorApplication.timeSinceStartup;
        _audio.Play(); _audio.Time = _time;
    }
    public void Pause() { IsPlaying = false; _audio.Pause(); }
    public void Stop() { Pause(); Time = 0; }
    public void Step(int offset) { Pause(); if (_clip is { } clip) Time = (double)Math.Clamp(_frame + offset, 0, clip.frameCount - 1) / clip.frameRateExact; }
    private void OnPlayMode(PlayModeStateChange state) { Stop(); ReleaseFrame(); _audio.SetClip(null); _clip = null; }
    private void Update()
    {
        var now = EditorApplication.timeSinceStartup;
        var delta = Math.Clamp(now - _lastUpdate, 0, .1); _lastUpdate = now;
        Advance(delta);
    }
    public void Advance(double delta)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (_clip is not { isDestroyed: false } clip || EditorApplication.isPlaying)
        { Pause(); ReleaseFrame(); _audio.SetClip(null); _clip = null; return; }
        if (IsPlaying) _time += delta;
        if (IsPlaying && _time >= (double)clip.length)
        {
            if (Loop) { _time %= (double)clip.length; _audio.Stop(); _audio.Play(); _audio.Time = _time; }
            else { _time = (double)clip.length; Pause(); }
        }
        Present();
    }
    private void Present()
    {
        if (_clip is not { isDestroyed: false } clip || _texture is null || _reader is null && _asyncReader is null) return;
        try
        {
            double timestamp;
            if (_asyncReader is { } worker)
            {
                if (worker.Error is { } failure) throw failure;
                if (_requestedTime != _time) { worker.Request(_time); _requestedTime = _time; }
                if (!worker.TryTakeFrame(out var snapshot)) return;
                _rgba = snapshot.Rgba; timestamp = snapshot.Timestamp;
            }
            else { _reader!.ReadFrame(Math.Min(_time, _reader.duration), _rgba); timestamp = _reader.timestamp; }
            if (timestamp == _presentedTimestamp) return;
            var frame = Math.Clamp((int)Math.Round(timestamp * clip.frameRateExact), 0, clip.frameCount - 1);
            _texture.SetPixelData(_rgba); _texture.Apply(false); _frame = frame;
            _presentedTimestamp = timestamp;
        }
        catch (Exception exception) { Error = exception.Message; Pause(); ReleaseFrame(); }
    }
    public void Draw(VideoClip? clip)
    {
        if (EditorApplication.isPlaying) { GUILayout.Label("Video preview is available in Edit mode.", EditorStyles.miniLabel); return; }
        SetClip(clip);
        GUILayout.Label("Editor Preview", EditorStyles.boldLabel);
        using var disabled = new EditorGUI.DisabledScope(clip is null || clip.isDestroyed || EditorApplication.isPlaying);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(IsPlaying ? "Pause" : "Play")) { if (IsPlaying) Pause(); else Play(); }
        if (GUILayout.Button("Stop")) Stop();
        if (GUILayout.Button("◀ Frame")) Step(-1);
        if (GUILayout.Button("Frame ▶")) Step(1);
        GUILayout.EndHorizontal();
        Loop = EditorGUILayout.Toggle("Loop Preview", Loop);
        _audio.Volume = EditorGUILayout.Slider("Preview Volume", _audio.Volume, 0, 1);
        var time = EditorGUILayout.Slider("Time (s)", (float)_time, 0, Math.Max(.001f, (float)(clip?.length ?? 0)));
        if (Math.Abs(time - _time) > .001) Time = time;
        var area = GUILayoutUtility.GetControlRect(200);
        GUI.DrawRect(area, Color.black);
        if (_texture is { isDestroyed: false } texture)
        {
            var scale = Fix64.Min(area.width / texture.width, area.height / texture.height);
            var width = texture.width * scale; var height = texture.height * scale;
            GUI.DrawTexture(new Rect(area.x + (area.width - width) / 2, area.y + (area.height - height) / 2, width, height), texture.assetPath);
        }
        if (clip is not null) GUILayout.Label($"Frame {Math.Max(0, _frame) + 1} / {clip.frameCount} · {clip.width} × {clip.height} · {clip.frameRateExact:0.###} fps", EditorStyles.miniLabel);
        if (_reader is not null) GUILayout.Label($"{_reader.backend} · GPU frames {_reader.hardwareDecodedFrames}", EditorStyles.miniLabel);
        if (_asyncReader is not null) GUILayout.Label($"Background decode · {_asyncReader.DecodedFrames} frames", EditorStyles.miniLabel);
        if (Error.Length > 0 || _audio.Error.Length > 0) GUILayout.Label(Error + " " + _audio.Error, EditorStyles.wordWrappedLabel);
    }
    private void ReleaseFrame()
    { _asyncReader?.Dispose(); _asyncReader = null; _requestedTime = double.NaN; _reader?.Dispose(); _reader = null; if (_texture is not null) BObject.Destroy(_texture); _texture = null; _frame = -1; _rgba = []; _presentedTimestamp = double.NegativeInfinity; }
    public void Dispose()
    {
        if (_disposed) return;
        Pause(); _audio.Dispose(); ReleaseFrame(); _clip = null; _disposed = true;
        EditorApplication.update -= Update; EditorApplication.playModeStateChanged -= OnPlayMode;
    }
}
