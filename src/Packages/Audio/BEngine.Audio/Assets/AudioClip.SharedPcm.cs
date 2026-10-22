namespace BEngine.Audio;

public sealed partial class AudioClip
{
    private readonly record struct PcmKey(string Path, long Modified, long Length, long Offset, bool Mono, float Gain,
        int Frames, int Channels, int Frequency);
    private static readonly Dictionary<PcmKey, DecodeRequest> SharedLoads = [];
    private static readonly Dictionary<PcmKey, SharedPcm> SharedData = [];
    private SharedPcm? _sharedPcm;
    private PcmKey? SharedKey()
    {
        if (!AudioLoadingSettings.shareDecodedResources || _streamPath is null || _factory is not null || _encoded is not null) return null;
        var files = RuntimeServiceAccess.FileSystem;
        return new(RuntimeServiceAccess.PathService.GetFullPath(_streamPath), files.GetLastWriteTimeUtc(_streamPath).Ticks,
            files.GetFileLength(_streamPath), _streamOffset, _mono, _gain, samples, channels, frequency);
    }
    private void Attach(SharedPcm storage)
    {
        ReleaseDecodedData(); _sharedPcm = storage; storage.References++; _pcm = storage.Pcm;
        _pendingLoad = null; _loadState = AudioDataLoadState.Loaded; loadError = ""; dataVersion++;
    }
    private void MakeWritable()
    {
        if (_sharedPcm is not { } storage) return;
        CloseReaders();
        if (storage.References == 1)
        {
            storage.RemoveFromCache(); _pcmReservation = storage.Reservation; storage.Reservation = null;
            storage.References = 0; _sharedPcm = null; return;
        }
        var copy = AllocatePcm(_pcm.Length); _pcm.CopyTo(copy, 0); _pcmReservation!.Commit();
        storage.Release(); _sharedPcm = null; _pcm = copy;
    }
    private sealed class SharedPcm(PcmKey key, float[] pcm, AudioLoadingSettings.PcmReservation reservation)
    {
        internal readonly float[] Pcm = pcm;
        internal AudioLoadingSettings.PcmReservation? Reservation = reservation;
        internal int References;
        internal void RemoveFromCache()
        { if (SharedData.TryGetValue(key, out var current) && ReferenceEquals(current, this)) SharedData.Remove(key); }
        internal void Release()
        { if (--References != 0) return; RemoveFromCache(); Reservation?.Dispose(); Reservation = null; }
    }
}
