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
        private float[]? _pcm;
        private int _offset;
        private bool _slot, _disposed;
        internal IDisposable? Registration;
        internal DecodeRequest(AudioClip owner, Func<AudioSampleReader> factory, int count,
            AudioLoadingSettings.PcmReservation reservation, PcmKey? key)
        { _owners.Add(owner); _frames = owner.samples; _channels = owner.channels; _frequency = owner.frequency;
          _key = key; _factory = factory; _count = count; _reservation = reservation; AudioLoadingSettings.AddPending(this); }
        internal void AddOwner(AudioClip owner)
        { if (_owners.Count >= 8192) throw new InvalidOperationException("A shared audio load already has 8192 subscribers."); _owners.Add(owner); }
        internal void RemoveOwner(AudioClip owner) { _owners.Remove(owner); if (_owners.Count == 0) Dispose(); }

        internal CooperativeWorkStatus Step()
        {
            if (_disposed) return CooperativeWorkStatus.Complete;
            if (_reader is null)
            {
                if (!AudioLoadingSettings.TryOpenDecoder()) return CooperativeWorkStatus.Blocked;
                _slot = true;
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
            try { _reader?.Dispose(); }
            finally
            {
                _reader = null; _pcm = null; _reservation?.Dispose(); _reservation = null;
                if (_slot) { _slot = false; AudioLoadingSettings.CloseDecoder(); }
                AudioLoadingSettings.RemovePending(this);
            }
        }
    }
}
