using System.Buffers.Binary;

namespace BEngine.Audio;

public sealed partial class AudioClip
{
    // The payload contains only encoded data, decoded samples and a thread-safe
    // budget lease. It neither creates nor retains an engine object on a worker.
    internal sealed class PreparedAudio : IDisposable
    {
        internal byte[] Encoded = [];
        internal float[] Pcm = [];
        internal AudioLoadingSettings.PcmReservation? Reservation;
        internal int Samples, Channels, Frequency;
        internal AudioClipLoadType LoadType;
        internal bool Mono, Background, Preload = true, BudgetFallback;
        internal float Gain = 1;
        public void Dispose() { Pcm = []; Encoded = []; Reservation?.Dispose(); Reservation = null; }
    }

    internal static PreparedAudio PrepareAudio(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        var prepared = new PreparedAudio();
        var slot = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = bytes.Span;
            if (IsContainer(data))
            {
                var legacy = data.StartsWith("BGAUDIO1"u8);
                if (data.Length < ContainerHeaderSize || data[8] > 1 || data[9] > (legacy ? 1 : 7) ||
                    BinaryPrimitives.ReadInt32LittleEndian(data[14..18]) != data.Length - ContainerHeaderSize)
                    throw new InvalidDataException("Invalid imported audio container.");
                prepared.LoadType = (AudioClipLoadType)data[8]; prepared.Mono = (data[9] & 1) != 0;
                prepared.Background = (data[9] & 2) != 0; prepared.Preload = (data[9] & 4) == 0;
                prepared.Gain = BinaryPrimitives.ReadSingleLittleEndian(data[10..14]);
                if (!float.IsFinite(prepared.Gain) || prepared.Gain < 0 || prepared.Gain > 100_000_000)
                    throw new InvalidDataException("Invalid audio normalization gain.");
                bytes = bytes[ContainerHeaderSize..];
            }
            prepared.Encoded = bytes.ToArray();
            while (!(slot = AudioLoadingSettings.TryOpenDecoder()))
            { cancellationToken.ThrowIfCancellationRequested(); Thread.Sleep(1); }
            AudioSampleReader reader = AudioDecoder.Open(new MemoryStream(prepared.Encoded, writable: false));
            if (prepared.Mono || prepared.Gain != 1) reader = new TransformReader(reader, prepared.Mono, prepared.Gain);
            using (reader)
            {
                ValidateFormat(reader.samples, reader.channels, reader.frequency);
                prepared.Samples = reader.samples; prepared.Channels = reader.channels; prepared.Frequency = reader.frequency;
                if (prepared.Preload && prepared.LoadType == AudioClipLoadType.DecompressOnLoad)
                {
                    var count = (long)reader.samples * reader.channels;
                    if (count > MaxDecodedSamples) throw new InvalidDataException("Audio PCM exceeds 256 MiB. Use Streaming.");
                    try { prepared.Reservation = AudioLoadingSettings.Reserve(count * sizeof(float), allowCollection: false); }
                    catch (AudioLoadingSettings.PcmBudgetExceededException)
                    { prepared.LoadType = AudioClipLoadType.Streaming; prepared.BudgetFallback = true; return prepared; }
                    prepared.Pcm = new float[(int)count]; prepared.Reservation.Allocated();
                    var offset = 0;
                    using var budget = LoopExecutionGuard.AllowIterations(prepared.Pcm.Length + 1);
                    while (offset < prepared.Pcm.Length)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var read = reader.Read(prepared.Pcm, offset, Math.Min(prepared.Pcm.Length - offset, 4096 * reader.channels));
                        if (read == 0) throw new InvalidDataException("Truncated audio sample data.");
                        offset += read;
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return prepared;
        }
        catch { prepared.Dispose(); throw; }
        finally { if (slot) AudioLoadingSettings.CloseDecoder(); }
    }

    internal static AudioClip CommitAudio(PreparedAudio prepared, string path)
    {
        var clip = new AudioClip { name = RuntimeServiceAccess.PathService.GetFileNameWithoutExtension(path) };
        try
        {
            clip.samples = prepared.Samples; clip.channels = prepared.Channels; clip.frequency = prepared.Frequency;
            clip.loadType = prepared.LoadType; clip._mono = prepared.Mono; clip._gain = prepared.Gain;
            clip.loadInBackground = prepared.Background; clip.preloadAudioData = prepared.Preload;
            clip._encoded = prepared.Encoded; prepared.Encoded = [];
            clip._pcm = prepared.Pcm; prepared.Pcm = [];
            clip._pcmReservation = prepared.Reservation; prepared.Reservation = null;
            clip._pcmReservation?.Commit();
            clip._loadState = prepared.Preload ? AudioDataLoadState.Loaded : AudioDataLoadState.Unloaded;
            clip.dataVersion++;
            clip.BindAssetReference(path);
            if (prepared.BudgetFallback)
                Debug.LogWarning($"AudioClip '{clip.name}' exceeded the decoded PCM budget (async bundle); using streaming playback.");
            return clip;
        }
        catch { DestroyImmediate(clip); throw; }
    }
}
