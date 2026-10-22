namespace BEngine.Audio;

[EditorIcon("Icons/Assets/AssetAudio.png")]
public sealed partial class AudioClip : BAsset
{
    private float[] _pcm = [];
    private byte[]? _encoded;
    private string? _streamPath;
    private long _streamOffset;
    private Func<AudioSampleReader>? _factory;
    private bool _mono;
    private float _gain = 1;
    private readonly HashSet<AudioSampleReader> _readers = [];
    internal int dataVersion { get; private set; }
    public int samples { get; private set; }
    public int channels { get; private set; }
    public int frequency { get; private set; }
    public Fix64 length => frequency <= 0 ? Fix64.Zero : (Fix64)samples / frequency;
    private AudioDataLoadState _loadState;
    public AudioDataLoadState loadState { get => _loadState; private set => _loadState = value; }
    public AudioClipLoadType loadType { get; private set; }
    public bool loadInBackground { get; private set; }
    public bool preloadAudioData { get; private set; } = true;
    public string loadError { get; private set; } = "";
    public bool ambisonic => false;
    public long decodedMemoryBytes => (long)_pcm.Length * sizeof(float);
    public long encodedMemoryBytes => _encoded?.LongLength ?? 0;
    public int activeReaderCount => _readers.Count;
    private const int MaxDecodedSamples = 64 * 1024 * 1024;
    private AudioClip() { }

    public delegate void PCMReaderCallback(float[] data);
    public delegate void PCMSetPositionCallback(int frame);
    public static AudioClip Create(string name, int lengthSamples, int channels, int frequency,
        bool stream = false, PCMReaderCallback? pcmReaderCallback = null, PCMSetPositionCallback? pcmSetPositionCallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ValidateFormat(lengthSamples, channels, frequency);
        if (stream)
        {
            ArgumentNullException.ThrowIfNull(pcmReaderCallback);
            return CreateStream(name, () => new CallbackReader(lengthSamples, channels, frequency, pcmReaderCallback, pcmSetPositionCallback));
        }
        var count = checked(lengthSamples * channels);
        if (count > MaxDecodedSamples) throw new InvalidDataException("Audio PCM exceeds 256 MiB. Use Streaming.");
        var clip = new AudioClip { name = name, samples = lengthSamples, channels = channels, frequency = frequency };
        try
        {
            clip._pcm = clip.AllocatePcm(count); clip._pcmReservation!.Commit(); clip.loadState = AudioDataLoadState.Loaded;
            if (pcmReaderCallback is not null) { pcmReaderCallback(clip._pcm); Sanitize(clip._pcm); }
            return clip;
        }
        catch { BObject.DestroyImmediate(clip); throw; }
    }

    /// <summary>The factory must return a new independent reader for each voice.</summary>
    public static AudioClip CreateStream(string name, Func<AudioSampleReader> readerFactory)
    {
        ArgumentNullException.ThrowIfNull(readerFactory);
        var clip = new AudioClip { name = name, _factory = readerFactory, loadType = AudioClipLoadType.Streaming };
        try { clip.InitializeFormat(); return clip; }
        catch { BObject.DestroyImmediate(clip); throw; }
    }

    public static AudioClip Load(string path) => Load(path, AudioClipLoadType.DecompressOnLoad);
    public static AudioClip Load(string path, AudioClipLoadType loadType) => LoadInternal(path, loadType, false);
    private static AudioClip LoadInternal(string path, AudioClipLoadType loadType, bool forceBackground)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // Imported containers retain their authored load policy. Plain files may be read on demand.
        var clip = new AudioClip { name = RuntimeServiceAccess.PathService.GetFileNameWithoutExtension(path), _streamPath = path, loadType = loadType };
        try
        {
            using (var source = RuntimeServiceAccess.FileSystem.OpenRead(path))
            {
                Span<byte> header = stackalloc byte[ContainerHeaderSize];
                var read = source.ReadAtLeast(header, header.Length, false);
                if (IsContainer(header[..read])) { clip.ReadContainerHeader(header[..read], source.Length); clip._streamOffset = ContainerHeaderSize; }
            }
            if (forceBackground) clip.loadInBackground = true;
            try
            {
                clip.InitializeFormat(applyLoadPolicy: !forceBackground);
            }
            catch (AudioLoadingSettings.PcmBudgetExceededException)
            {
                // A file loaded directly from a platform path still has an
                // independent encoded source. Keep that source and switch to
                // the streaming reader when the device cannot admit its PCM.
                clip.SwitchToStreamingAfterBudget("path");
            }
            clip.BindAssetReference(path); return clip;
        }
        catch { BObject.DestroyImmediate(clip); throw; }
    }
    public static AudioClip Load(string path, bool forceToMono, bool normalize)
    {
        if (!forceToMono && !normalize) return Load(path);
        return FromAudio(BuildImportedBytes(RuntimeServiceAccess.FileSystem.ReadAllBytes(path), AudioClipLoadType.DecompressOnLoad, forceToMono, normalize), path);
    }

    public AudioSampleReader OpenReader()
    {
        ObjectDisposedException.ThrowIf(isDestroyed, this);
        if (loadState != AudioDataLoadState.Loaded) throw new InvalidOperationException("Audio data is not loaded.");
        AudioSampleReader raw = loadType == AudioClipLoadType.DecompressOnLoad
            ? new MemoryReader(_pcm, channels, frequency) : CreateSourceReader();
        if (raw.channels != channels || raw.frequency != frequency || raw.samples != samples)
        { raw.Dispose(); throw new InvalidDataException("Audio source format changed. Reimport the clip."); }
        var owned = new OwnedReader(this, raw); _readers.Add(owned); return owned;
    }
    public bool GetData(float[] data, int offsetSamples)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (isDestroyed || loadState != AudioDataLoadState.Loaded || offsetSamples < 0 || offsetSamples > samples ||
            data.Length > (long)(samples - offsetSamples) * channels) return false;
        if (data.Length % channels != 0)
        {
            var aligned = new float[checked((data.Length / channels + 1) * channels)];
            if (!GetData(aligned, offsetSamples)) return false;
            aligned.AsSpan(0, data.Length).CopyTo(data); return true;
        }
        using var reader = OpenReader(); reader.Seek(offsetSamples);
        var offset = 0;
        using var budget = LoopExecutionGuard.AllowIterations(data.Length / Math.Max(1, channels) + 1);
        while (offset < data.Length)
        { var read = reader.Read(data, offset, data.Length - offset); if (read == 0) return false; offset += read; }
        return true;
    }
    public bool SetData(float[] data, int offsetSamples)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (isDestroyed || loadState != AudioDataLoadState.Loaded || loadType == AudioClipLoadType.Streaming || offsetSamples < 0 ||
            offsetSamples > samples || data.Length > _pcm.Length - (long)offsetSamples * channels) return false;
        if (!InvalidatePlaybackData())
        { loadError = "Audio data is still in use by the independent mixer; retry SetData."; return false; }
        try { MakeWritable(); }
        catch (InvalidOperationException error) { loadError = error.Message; return false; }
        data.CopyTo(_pcm, checked(offsetSamples * channels)); Sanitize(_pcm.AsSpan(offsetSamples * channels, data.Length)); dataVersion++; return true;
    }
    public bool LoadAudioData()
    {
        if (isDestroyed) return false;
        if (loadState is AudioDataLoadState.Loaded or AudioDataLoadState.Loading) return true;
        try { InitializeFormat(); return true; }
        catch (Exception error) { loadError = error.Message; loadState = AudioDataLoadState.Failed; return false; }
    }
    public bool UnloadAudioData()
    {
        if (isDestroyed || _encoded is null && _streamPath is null && _factory is null) return false;
        if (!InvalidatePlaybackData(stop: true)) return false;
        CancelPendingLoad(); CloseReaders(); ReleaseDecodedData(); dataVersion++; loadState = AudioDataLoadState.Unloaded; loadError = ""; return true;
    }
    internal float Sample(int frame, int channel) => !isDestroyed && (uint)frame < (uint)samples && _pcm.Length > 0
        ? _pcm[frame * channels + Math.Clamp(channel, 0, channels - 1)] : 0;
    internal bool HasDecodedPcm => _pcm.Length > 0;
    internal float[] DecodedPcmForVoice => _pcm;
    internal static AudioClip FromWav(ReadOnlyMemory<byte> bytes, string path) => FromAudio(bytes, path);
    internal static AudioClip FromAudio(ReadOnlyMemory<byte> bytes, string path)
    {
        var clip = new AudioClip { name = RuntimeServiceAccess.PathService.GetFileNameWithoutExtension(path) };
        try
        {
            clip.ReadContainer(bytes);
            try
            {
                clip.InitializeFormat(applyLoadPolicy: true);
            }
            catch (AudioLoadingSettings.PcmBudgetExceededException)
            {
                // Imported bundle assets keep their encoded bytes in _encoded,
                // so streaming is a safe fallback even during AOT bootstrap.
                clip.SwitchToStreamingAfterBudget("bundle");
            }
            clip.BindAssetReference(path); return clip;
        }
        catch { BObject.DestroyImmediate(clip); throw; }
    }

    private void SwitchToStreamingAfterBudget(string source)
    {
        CancelPendingLoad();
        ReleaseDecodedData();
        loadType = AudioClipLoadType.Streaming;
        loadState = AudioDataLoadState.Loaded;
        loadError = "";
        dataVersion++;
        Debug.LogWarning($"AudioClip '{name}' exceeded the decoded PCM budget ({source}); using streaming playback.");
    }
    private AudioSampleReader CreateSourceReader() => SourceReaderFactory()();
    private Func<AudioSampleReader> SourceReaderFactory()
    {
        // Snapshot dependencies before starting work; no background task mutates the asset.
        var factory = _factory; var encoded = _encoded; var path = _streamPath; var offset = _streamOffset;
        var mono = _mono; var gain = _gain; var files = RuntimeServiceAccess.FileSystem;
        return () =>
        {
            AudioSampleReader reader;
            if (factory is not null) reader = factory();
            else
            {
                Stream stream = encoded is not null ? new MemoryStream(encoded, writable: false) :
                    path is not null ? files.OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.SequentialScan) :
                    throw new InvalidOperationException("Audio source is no longer available.");
                if (encoded is null && offset > 0) stream = new IO.OffsetReadStream(stream, offset);
                reader = AudioDecoder.Open(stream);
            }
            return mono || gain != 1 ? new TransformReader(reader, mono, gain) : reader;
        };
    }
    private void InitializeFormat(bool applyLoadPolicy = false)
    {
        if (!Enum.IsDefined(loadType)) throw new InvalidDataException("Invalid audio load type.");
        using var reader = CreateSourceReader(); ValidateFormat(reader.samples, reader.channels, reader.frequency);
        samples = reader.samples; channels = reader.channels; frequency = reader.frequency;
        if (applyLoadPolicy && !preloadAudioData) { loadState = AudioDataLoadState.Unloaded; return; }
        loadError = "";
        if (loadType == AudioClipLoadType.DecompressOnLoad)
        {
            var count = (long)samples * channels;
            if (count > MaxDecodedSamples) throw new InvalidDataException("Audio PCM exceeds 256 MiB. Use Streaming.");
            try
            {
                if (loadInBackground) { StartPendingLoad(); return; }
                _pcm = DecodePcm(reader, CancellationToken.None);
            }
            catch (AudioLoadingSettings.PcmBudgetExceededException)
            {
                // A clip whose authored policy is DecompressOnLoad must still be
                // usable on devices that cannot admit its full PCM payload. The
                // encoded source is retained and the reader path is used instead.
                SwitchToStreamingAfterBudget("format");
                return;
            }
        }
        dataVersion++; loadState = AudioDataLoadState.Loaded;
    }
    private static void ValidateFormat(int frames, int channels, int frequency)
    {
        if (frames <= 0 || channels is < 1 or > 8 || frequency is < 8000 or > 384000)
            throw new InvalidDataException("Audio must have positive frames, 1–8 channels and 8000–384000 Hz.");
    }
    private static void Sanitize(Span<float> data)
    {
        using var budget = LoopExecutionGuard.AllowIterations(data.Length + 1);
        for (var i = 0; i < data.Length; i++) data[i] = float.IsFinite(data[i]) ? Math.Clamp(data[i], -1, 1) : 0;
    }
    private void CloseReaders() { foreach (var reader in _readers.ToArray()) reader.Dispose(); }
    protected override void ReleaseAssetResources()
    {
        if (!InvalidatePlaybackData(stop: true))
        {
            AudioIndependentClock.Shutdown();
            InvalidatePlaybackData(stop: true);
        }
        CancelPendingLoad(); CloseReaders(); ReleaseDecodedData(); _encoded = null; _streamPath = null; _factory = null;
        loadState = AudioDataLoadState.Unloaded; base.ReleaseAssetResources();
    }
    protected override object CaptureClonePayload() => new object?[] { samples, channels, frequency, (int)loadState,
        _pcm, _encoded, _streamPath, _factory, (int)loadType, _mono, _gain, _streamOffset, loadInBackground, preloadAudioData };
    protected override void RestoreClonePayload(object? payload, Func<BObject, BObject> cloneObject)
    {
        if (payload is not object?[] { Length: 14 } data) throw new InvalidDataException("Invalid AudioClip clone payload.");
        if (!InvalidatePlaybackData(stop: true))
        {
            AudioIndependentClock.Shutdown();
            InvalidatePlaybackData(stop: true);
        }
        CancelPendingLoad(); CloseReaders(); ReleaseDecodedData();
        samples = (int)data[0]!; channels = (int)data[1]!; frequency = (int)data[2]!; loadState = (AudioDataLoadState)(int)data[3]!;
        var pcm = (float[])data[4]!;
        if (pcm.Length > 0) { _pcm = AllocatePcm(pcm.Length); pcm.CopyTo(_pcm, 0); _pcmReservation!.Commit(); }
        _encoded = data[5] is byte[] encoded ? (byte[])encoded.Clone() : null; _streamPath = (string?)data[6]; _factory = (Func<AudioSampleReader>?)data[7];
        loadType = (AudioClipLoadType)(int)data[8]!; _mono = (bool)data[9]!; _gain = (float)data[10]!;
        _streamOffset = (long)data[11]!; loadInBackground = (bool)data[12]!; preloadAudioData = (bool)data[13]!;
        if (_loadState == AudioDataLoadState.Loading) StartPendingLoad();
        dataVersion++;
    }
}
