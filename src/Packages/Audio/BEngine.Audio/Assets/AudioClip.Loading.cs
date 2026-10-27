namespace BEngine.Audio;

public sealed partial class AudioClip
{
    private DecodeRequest? _pendingLoad;
    private AudioLoadingSettings.PcmReservation? _pcmReservation;

    private void StartPendingLoad()
    {
        CancelPendingLoad();
        var key = SharedKey();
        if (key is { } cachedKey && SharedData.TryGetValue(cachedKey, out var cached)) { Attach(cached); return; }
        if (key is { } pendingKey && SharedLoads.TryGetValue(pendingKey, out var shared))
        { shared.AddOwner(this); _pendingLoad = shared; _loadState = AudioDataLoadState.Loading; return; }
        var count = checked(samples * channels);
        var reservation = AudioLoadingSettings.Reserve((long)count * sizeof(float));
        DecodeRequest? request = null;
        try
        {
            request = new DecodeRequest(this, SourceReaderFactory(), count, reservation, key);
            if (key is { } newKey) SharedLoads[newKey] = request;
            _pendingLoad = request; _loadState = AudioDataLoadState.Loading;
            request.Registration = CooperativeWorkScheduler.Main.Schedule(request.Step, request.Fail, CooperativeWorkPriority.Background);
        }
        catch { _pendingLoad = null; request?.Dispose(); reservation.Dispose(); throw; }
    }

    private void CancelPendingLoad() { var pending = _pendingLoad; _pendingLoad = null; pending?.RemoveOwner(this); }
    internal void PrioritizeLoading()
    { if (_pendingLoad?.Registration is { } work) CooperativeWorkScheduler.Main.SetPriority(work, CooperativeWorkPriority.Realtime); }

    private void ReleaseDecodedData()
    {
        _pcm = []; _sharedPcm?.Release(); _sharedPcm = null; _pcmReservation?.Dispose(); _pcmReservation = null;
    }

    private float[] AllocatePcm(int count)
    {
        var reservation = AudioLoadingSettings.Reserve((long)count * sizeof(float));
        try { var pcm = new float[count]; reservation.Allocated(); _pcmReservation?.Dispose(); _pcmReservation = reservation; return pcm; }
        catch { reservation.Dispose(); throw; }
    }

    private float[] DecodePcm(AudioSampleReader reader, CancellationToken token)
    {
        var count = (long)reader.samples * reader.channels;
        if (count > MaxDecodedSamples) throw new InvalidDataException("Audio PCM exceeds 256 MiB. Use Streaming.");
        var pcm = AllocatePcm((int)count); var offset = 0;
        try
        {
            using var budget = LoopExecutionGuard.AllowIterations(pcm.Length + 1);
            while (offset < pcm.Length)
            {
                token.ThrowIfCancellationRequested();
                var read = reader.Read(pcm, offset, Math.Min(pcm.Length - offset, 4096 * reader.channels));
                if (read == 0) throw new InvalidDataException("Truncated audio sample data."); offset += read;
            }
            _pcmReservation!.Commit(); return pcm;
        }
        catch { ReleaseDecodedData(); throw; }
    }

    private sealed class DecodeRequest : IDisposable
    {
        private readonly HashSet<AudioClip> _owners = [];
        private readonly int _frames, _channels, _frequency;
        private readonly PcmKey? _key;
        private readonly Func<AudioSampleReader> _factory;
        private readonly int _count;
        private AudioLoadingSettings.PcmReservation? _reservation;
        private AudioSampleReader? _reader;
        private WebAudioDecoder? _platformDecoder;
        private readonly byte[]? _encoded;
        private readonly bool _mono;
        private readonly float _gain;
        private bool _platformAttempted;
        private float[]? _pcm;
        private int _offset;
        private bool _slot, _disposed;
        internal IDisposable? Registration;
        internal DecodeRequest(AudioClip owner, Func<AudioSampleReader> factory, int count,
            AudioLoadingSettings.PcmReservation reservation, PcmKey? key)
        { _owners.Add(owner); _frames = owner.samples; _channels = owner.channels; _frequency = owner.frequency;
          _key = key; _factory = factory; _count = count; _reservation = reservation;
          _encoded = owner._encoded; _mono = owner._mono; _gain = owner._gain; AudioLoadingSettings.AddPending(this); }
        internal void AddOwner(AudioClip owner)
        { if (_owners.Count >= 8192) throw new InvalidOperationException("A shared audio load already has 8192 subscribers."); _owners.Add(owner); }
        internal void RemoveOwner(AudioClip owner) { _owners.Remove(owner); if (_owners.Count == 0) Dispose(); }

        internal CooperativeWorkStatus Step()
        {
            if (_disposed) return CooperativeWorkStatus.Complete;
            if (!_slot)
            {
                if (!AudioLoadingSettings.TryOpenDecoder()) return CooperativeWorkStatus.Blocked;
                _slot = true;
            }
            if (OperatingSystem.IsBrowser() && !_platformAttempted)
            {
                _platformAttempted = true;
                _platformDecoder = WebAudioDecoder.TryCreate(_encoded, _frames, _channels, _frequency, _mono, _gain);
            }
            if (OperatingSystem.IsBrowser() && _platformDecoder is { } decoder)
            {
                var state = decoder.status;
                if (state == 0) return CooperativeWorkStatus.Blocked;
                if (state < 0)
                {
                    decoder.Dispose(); _platformDecoder = null;
                    // Native format/duration differences must never change the
                    // authored clip or retry the same browser decoder forever.
                }
                else
                {
                    if (_pcm is null) { _pcm = new float[_count]; _reservation!.Allocated(); }
                    // Transfer whole sample frames in bounded blocks. Explicit
                    // loading frames can submit 256 KiB; ordinary gameplay uses
                    // 128 KiB and retains its existing scheduler time guards.
                    var blockSamples = FrameWorkBudget.isLoading ? 65536 : 32768;
                    var count = Math.Min(_count - _offset, Math.Max(1, blockSamples / _channels) * _channels);
                    var copied = decoder.Copy(_offset / _channels, _pcm.AsSpan(_offset, count));
                    if (copied != count) throw new InvalidDataException("The platform audio decoder returned an incomplete PCM block.");
                    _offset += copied;
                    if (_offset < _count) return CooperativeWorkStatus.Continue;
                    return Complete();
                }
            }
            if (_reader is null)
            {
                var reader = _factory();
                if (_disposed) { reader.Dispose(); return CooperativeWorkStatus.Complete; }
                _reader = reader;
                if (_reader.samples != _frames || _reader.channels != _channels || _reader.frequency != _frequency)
                    throw new InvalidDataException("Audio source format changed during asynchronous loading.");
                _pcm = new float[_count];
                _reservation!.Allocated();
                return CooperativeWorkStatus.Continue;
            }
            var read = _reader.Read(_pcm!, _offset, Math.Min(_count - _offset, 1024 * _reader.channels));
            if (_disposed) return CooperativeWorkStatus.Complete;
            if (read == 0) throw new InvalidDataException("Truncated audio sample data.");
            _offset += read;
            if (_offset < _count) return CooperativeWorkStatus.Continue;
            return Complete();
        }

        private CooperativeWorkStatus Complete()
        {
            _reservation!.Commit();
            if (_key is { } key)
            {
                var storage = new SharedPcm(key, _pcm!, _reservation); SharedData[key] = storage;
                foreach (var owner in _owners) owner.Attach(storage);
            }
            else
            {
                var owner = _owners.Single(); owner.ReleaseDecodedData();
                owner._pcm = _pcm!; owner._pcmReservation = _reservation;
                owner.dataVersion++; owner._loadState = AudioDataLoadState.Loaded; owner.loadError = ""; owner._pendingLoad = null;
            }
            _pcm = null; _reservation = null;
            _owners.Clear(); Dispose();
            return CooperativeWorkStatus.Complete;
        }

        internal void Fail(Exception error)
        {
            if (_disposed) return;
            foreach (var owner in _owners)
            { owner.loadError = error.Message; owner._loadState = AudioDataLoadState.Failed; owner._pendingLoad = null; }
            _owners.Clear(); Dispose();
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; Registration?.Dispose(); Registration = null;
            if (_key is { } key && SharedLoads.TryGetValue(key, out var load) && ReferenceEquals(load, this)) SharedLoads.Remove(key);
            foreach (var owner in _owners)
                if (ReferenceEquals(owner._pendingLoad, this))
                { owner._pendingLoad = null; owner._loadState = AudioDataLoadState.Unloaded; owner.loadError = ""; }
            _owners.Clear();
            try
            {
                _reader?.Dispose();
                if (OperatingSystem.IsBrowser()) _platformDecoder?.Dispose();
            }
            finally
            {
                _reader = null; _platformDecoder = null; _pcm = null; _reservation?.Dispose(); _reservation = null;
                if (_slot) { _slot = false; AudioLoadingSettings.CloseDecoder(); }
                AudioLoadingSettings.RemovePending(this);
            }
        }
    }
}
