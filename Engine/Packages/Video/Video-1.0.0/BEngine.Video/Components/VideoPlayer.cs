using BEngine.Audio;

namespace BEngine.Video;

public enum VideoSource { VideoClip, Url }

[AddComponentMenu("Video/Video Player")]
[EditorIcon("Icons/Assets/AssetVideo.png")]
public sealed class VideoPlayer : MonoBehaviour
{
    private VideoClip? _clip;
    [NonSerialized] private Texture? _texture;
    [NonSerialized] private Sprite? _sprite;
    [NonSerialized] private AudioSource? _audio;
    [NonSerialized] private byte[] _rgba = [];
    [NonSerialized] private Fix64 _time;
    [NonSerialized] private int _frame = -1;
    [NonSerialized] private bool _playing;
    [NonSerialized] private VideoFrameReader? _reader;
    [NonSerialized] private AsyncVideoFrameReader? _asyncReader;
    [NonSerialized] private bool _asyncPending;
    [NonSerialized] private bool _playRequested;
    [NonSerialized] private double _asyncTime = double.NaN;
    [NonSerialized] private VideoClip? _urlClip;
    [NonSerialized] private Task<VideoClip>? _urlLoad;
    [NonSerialized] private int? _requestedFrame;
    [NonSerialized] private bool _seekAwaiting;
    [NonSerialized] private SpriteRenderer? _boundRenderer;
    [NonSerialized] private Sprite? _previousSprite;
    [NonSerialized] private double _presentedTimestamp = double.NegativeInfinity;
    private VideoSource _source;
    private string _url = "";
    private VideoDecodeMode _decodeMode;
    private SpriteRenderer? _targetRenderer;
    /// <summary>Opt-in: retain playback time/audio while skipping frame decoding for a disabled target renderer.</summary>
    public bool skipDecodingWhenHidden { get; set; }

    public VideoClip? clip
    {
        get => _clip;
        set { if (ReferenceEquals(_clip, value)) return; Release(); _clip = value; _time = 0; }
    }
    public SpriteRenderer? targetRenderer
    { get => _targetRenderer; set { if (ReferenceEquals(_targetRenderer, value)) return; Release(); _targetRenderer = value; } }
    public VideoSource source { get => _source; set { if (_source == value) return; Release(); _source = value; _time = 0; } }
    public string url { get => _url; set { value ??= ""; if (_url == value) return; Release(); _url = value; _time = 0; } }
    public VideoDecodeMode decodeMode { get => _decodeMode; set { if (_decodeMode == value) return; Release(); _decodeMode = value; } }
    public VideoClip? preparedClip => source == VideoSource.Url ? _urlClip : clip;
    public string decoderBackend => _reader?.backend ?? (_asyncReader?.Prepared.IsCompletedSuccessfully == true ? _asyncReader.Prepared.Result.Backend : "");
    public long hardwareDecodedFrames => _reader?.hardwareDecodedFrames ?? _asyncReader?.HardwareDecodedFrames ?? 0;
    public string error { get; private set; } = "";
    public bool playOnAwake { get; set; } = true;
    public bool asyncDecoding { get; set; }
    public bool includeAudioTrack { get; set; } = true;
    public bool isPreparing => _urlLoad is not null || _asyncReader is not null && !isPrepared;
    public bool isLooping { get; set; }
    [Range(0.01f, 3)] public Fix64 playbackSpeed { get; set; } = Fix64.One;
    [Range(0, 1)] public Fix64 volume { get; set; } = Fix64.One;
    public bool mute { get; set; }
    public bool isPlaying => _playing;
    public bool isPrepared => _texture is not null && (_asyncReader is null || _presentedTimestamp != double.NegativeInfinity);
    public Texture? texture => _texture;
    public int frame => _frame;
    public Fix64 time
    {
        get => _time;
        set
        {
            _time = Fix64.Clamp(value, 0, preparedClip?.length ?? Fix64.Zero);
            if (_asyncReader is not null)
            { _asyncReader.Request((double)_time); _asyncTime = (double)_time; _asyncPending = true; }
            if (isPrepared)
            {
                try { PresentFrame(); }
                catch (Exception exception) { Release(); error = exception.Message; errorReceived?.Invoke(this, error); return; }
            }
            if (_audio is not null) _audio.time = _time;
        }
    }
    public event Action<VideoPlayer>? prepareCompleted;
    public event Action<VideoPlayer>? started;
    public event Action<VideoPlayer>? loopPointReached;
    public event Action<VideoPlayer>? seekCompleted;
    public event Action<VideoPlayer, int>? frameReady;
    public event Action<VideoPlayer, string>? errorReceived;

    public override void Start() { if (playOnAwake) Play(); }
    public override void Update()
    {
        Advance(Time.deltaTime);
    }
    public override void OnDisable() => Release();
    public override void OnDestroy()
    {
        Release();
    }

    public void Prepare()
    {
        if (_texture is not null || _urlLoad is not null) return;
        try
        {
            if (source == VideoSource.Url && _urlClip is null)
            {
                if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("Specify a video file path or HTTP(S) URL.");
                if (OperatingSystem.IsBrowser() || asyncDecoding) { _urlLoad = VideoClip.LoadAsync(url, includeAudioTrack); return; }
                _urlClip = VideoClip.Load(url, includeAudioTrack);
            }
            if (preparedClip is not { isDestroyed: false } media) return;
            if (asyncDecoding && media.isNative && !media.requiresPolling && !OperatingSystem.IsBrowser()) _asyncReader = media.OpenAsyncReader(decodeMode);
            else _reader = media.OpenReader(decodeMode);
            error = "";
            _rgba = new byte[checked(media.width * media.height * 4)];
            _texture = Texture.FromRgba32(media.width, media.height, _rgba); _texture.name = media.name + " Frame";
            _sprite = _texture.CreateSprite(new Vector2(Fix64.Half, Fix64.Half));
            _targetRenderer ??= GetComponent<SpriteRenderer>() ?? gameObject.AddComponent<SpriteRenderer>();
            _boundRenderer = _targetRenderer; _previousSprite = _targetRenderer.sprite; _targetRenderer.sprite = _sprite;
            if (includeAudioTrack && media.audioClip is not null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.clip = media.audioClip;
                UpdateAudioSettings();
            }
            if (_requestedFrame is { } requested)
            {
                if (requested >= media.frameCount) throw new ArgumentOutOfRangeException(nameof(requested), "Requested frame exceeds video duration.");
                _time = requested == 0 ? Fix64.Zero : (Fix64)(requested / media.frameRateExact) + Fix64.Epsilon;
                _requestedFrame = null; _seekAwaiting = true;
            }
            PresentFrame();
            if (_asyncReader is null) prepareCompleted?.Invoke(this);
        }
        catch (Exception exception)
        {
            Release();
            error = exception.Message;
            errorReceived?.Invoke(this, exception.Message);
            Debug.LogError($"Video '{clip?.name}' could not prepare: {exception.Message}");
        }
    }

    public void Play()
    {
        Prepare();
        if (isPreparing) { _playRequested = true; return; }
        if (!isPrepared || preparedClip is not { } media || _playing) return;
        if (_time >= media.length) time = 0;
        _playing = true;
        if (_audio is not null) { UpdateAudioSettings(); _audio.Play(); _audio.time = _time; }
        started?.Invoke(this);
    }
    public void Pause() { _playing = false; _playRequested = false; _audio?.Pause(); }
    public void Stop() { _playing = false; _playRequested = false; _audio?.Stop(); time = 0; }

    /// <summary>Prepares and seeks to an exact frame while preserving the playing/paused state.</summary>
    public void SeekFrame(int frameIndex)
    {
        if (frameIndex < 0 || preparedClip is { } known && frameIndex >= known.frameCount) throw new ArgumentOutOfRangeException(nameof(frameIndex));
        if (!isPrepared)
        {
            if (_texture is not null && preparedClip is { } preparing)
            {
                _seekAwaiting = true;
                time = frameIndex == 0 ? Fix64.Zero : (Fix64)(frameIndex / preparing.frameRateExact) + Fix64.Epsilon;
            }
            else { _requestedFrame = frameIndex; Prepare(); }
            return;
        }
        Prepare();
        if (!isPrepared) return;
        _requestedFrame = null;
        var media = preparedClip!;
        if (frameIndex < 0 || frameIndex >= media.frameCount) throw new ArgumentOutOfRangeException(nameof(frameIndex));
        // Round upward by one fixed-point unit so frame/rate truncation cannot select the previous frame.
        _seekAwaiting = true;
        time = frameIndex == 0 ? Fix64.Zero : (Fix64)(frameIndex / media.frameRateExact) + Fix64.Epsilon;
    }

    /// <summary>Pauses playback and presents the next frame, wrapping only when looping is enabled.</summary>
    public void StepForward()
    {
        Prepare();
        if (!isPrepared) return;
        var media = preparedClip!;
        Pause();
        var next = _frame + 1;
        SeekFrame(next >= media.frameCount ? isLooping ? 0 : media.frameCount - 1 : next);
    }

    /// <summary>Advances the video on the engine thread; Update calls this with Time.deltaTime.</summary>
    public void Advance(Fix64 deltaTime)
    {
        if (deltaTime < 0) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (_urlLoad is { IsCompleted: true } loading)
        {
            _urlLoad = null;
            try
            {
                _urlClip = loading.GetAwaiter().GetResult(); Prepare();
                if (_playRequested && isPrepared) { _playRequested = false; Play(); }
            }
            catch (Exception exception) { Release(); error = exception.Message; errorReceived?.Invoke(this, error); return; }
        }
        if (preparedClip is { isDestroyed: true }) { Release(); return; }
        if (!_playing || preparedClip is not { } media)
        { if ((_asyncReader is not null || _reader?.requiresPolling == true) && _texture is not null) try { PresentFrame(); } catch (Exception exception) { Release(); error = exception.Message; errorReceived?.Invoke(this, error); } return; }
        UpdateAudioSettings();
        _time += deltaTime * Fix64.Clamp(playbackSpeed, Fix64.FromDecimal(.01m), 3);
        var reachedEnd = _time >= media.length;
        if (reachedEnd)
        {
            if (isLooping) _time %= media.length;
            else { _time = media.length; _playing = false; _audio?.Stop(); }
        }
        try { if (!skipDecodingWhenHidden || _targetRenderer is null || _targetRenderer.enabled && _targetRenderer.gameObject.activeInHierarchy) PresentFrame(); }
        catch (Exception exception) { Release(); error = exception.Message; errorReceived?.Invoke(this, error); return; }
        if (reachedEnd)
        {
            if (_playing && _audio is not null) { _audio.Play(); _audio.time = _time; }
            loopPointReached?.Invoke(this);
        }
    }

    private void UpdateAudioSettings()
    {
        if (_audio is null) return;
        _audio.loop = isLooping; _audio.volume = volume; _audio.mute = mute;
        _audio.pitch = Fix64.Clamp(playbackSpeed, Fix64.FromDecimal(.01m), 3);
    }

    private void PresentFrame()
    {
        if (preparedClip is not { } media || _texture is null) return;
        double timestamp;
        if (_asyncReader is { } asynchronous)
        {
            if (asynchronous.Error is { } decodeError) throw new InvalidOperationException("Asynchronous video decoding failed.", decodeError);
            if (asynchronous.TryTakeFrame(out var frame))
            { _rgba = frame.Rgba; timestamp = frame.Timestamp; _asyncPending = false; }
            else
            {
                if (!_asyncPending && _asyncTime != (double)_time) { asynchronous.Request((double)_time); _asyncTime = (double)_time; _asyncPending = true; }
                return;
            }
            if (_playing && _asyncTime != (double)_time) { asynchronous.Request((double)_time); _asyncTime = (double)_time; _asyncPending = true; }
        }
        else
        {
            if (_reader is null) return;
            // Portable clips have a fixed frame grid. A fast game loop may
            // visit the same frame many times; avoid opening a new Deflate
            // decoder before discovering that its timestamp is unchanged.
            // Native/polling readers still run so pending output can arrive.
            if (media.isPortable && _frame >= 0 &&
                Math.Clamp((int)Math.Floor((double)_time * media.frameRateExact + 1e-7), 0, media.frameCount - 1) == _frame)
            { CompleteSeek(); return; }
            _reader.ReadFrame(Math.Min((double)_time, _reader.duration), _rgba); timestamp = _reader.timestamp;
        }
        if (timestamp == _presentedTimestamp) { if (_reader?.hasPendingFrame != true) CompleteSeek(); return; }
        var next = Math.Clamp((int)Math.Round(timestamp * media.frameRateExact), 0, media.frameCount - 1);
        _texture.SetPixelData(_rgba); _texture.Apply(false);
        _frame = next;
        var firstAsyncFrame = _asyncReader is not null && _presentedTimestamp == double.NegativeInfinity;
        _presentedTimestamp = timestamp;
        frameReady?.Invoke(this, next);
        if (firstAsyncFrame)
        {
            prepareCompleted?.Invoke(this);
            if (_playRequested) { _playRequested = false; Play(); }
        }
        if (_reader?.hasPendingFrame != true) CompleteSeek();
    }

    private void CompleteSeek() { if (_seekAwaiting) { _seekAwaiting = false; seekCompleted?.Invoke(this); } }

    private static async BTask ReleasePending(Task<VideoClip> pending)
    {
        while (!pending.IsCompleted) await BTask.Yield();
        if (pending.IsCompletedSuccessfully) DestroyImmediate(pending.GetAwaiter().GetResult());
        else _ = pending.Exception;
    }

    private void Release()
    {
        if (_urlLoad is { } pending)
        {
            _urlLoad = null;
            _ = ReleasePending(pending);
        }
        _requestedFrame = null; _seekAwaiting = false;
        _playing = false;
        _audio?.Stop();
        if (_audio is not null) Destroy(_audio);
        if (_boundRenderer is { isDestroyed: false } && ReferenceEquals(_boundRenderer.sprite, _sprite)) _boundRenderer.sprite = _previousSprite;
        if (_sprite is not null) Destroy(_sprite);
        if (_texture is not null) Destroy(_texture);
        _reader?.Dispose(); _reader = null;
        _asyncReader?.Dispose(); _asyncReader = null; _asyncPending = false; _playRequested = false; _asyncTime = double.NaN;
        if (_urlClip is not null) Destroy(_urlClip);
        _urlClip = null; _boundRenderer = null; _previousSprite = null;
        _audio = null; _texture = null; _sprite = null; _rgba = []; _frame = -1;
        _presentedTimestamp = double.NegativeInfinity;
    }

    /// <summary>Stops playback and frees decoder, GPU texture and owned URL resources.</summary>
    public void Close() => Release();
}
