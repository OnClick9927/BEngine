using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BEngine.Audio;

[SupportedOSPlatform("browser")]
public sealed class WebAudioOutputFactory : IAudioOutputFactory
{
    public static WebAudioOutputFactory Shared { get; } = new();

    public IAudioOutput CreateDefault() => new WebAudioOutput();
    public IAudioOutput SilentOutput => OpenAlAudioOutputFactory.Shared.SilentOutput;
    public bool IsSystemOutput(IAudioOutput output) => output is WebAudioOutput;
}

[SupportedOSPlatform("browser")]
public sealed class WebAudioOutput : IAudioBufferedOutput, IAudioOutputDiagnostics, IDisposable
{
    private readonly int _handle;
    private bool _disposed;

    public WebAudioOutput(int sampleRate = 48000, int channelCount = 2)
    {
        if (sampleRate is < 8000 or > 384000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channelCount is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channelCount));
        this.sampleRate = sampleRate;
        this.channelCount = channelCount;
        var bufferMilliseconds = AudioRuntimeSettings.outputBufferMilliseconds;
        capacityFrames = checked(sampleRate * bufferMilliseconds / 1000);
        _handle = WebAudioInterop.Create(sampleRate, channelCount, bufferMilliseconds);
        if (_handle <= 0) throw new InvalidOperationException("The browser did not create a Web Audio output.");
    }

    public int sampleRate { get; }
    public int channelCount { get; }
    public int capacityFrames { get; }
    public int queuedFrames
    { get { ObjectDisposedException.ThrowIf(_disposed, this); return WebAudioInterop.GetQueuedFrames(_handle); } }
    public int availableFrames
    { get { ObjectDisposedException.ThrowIf(_disposed, this); return WebAudioInterop.GetAvailableFrames(_handle); } }
    /// <summary>Scheduled tail relative to the browser device clock, including its start lead.</summary>
    public double bufferedMilliseconds => ReadStatistics().BufferedMilliseconds;
    public string backend => "Web Audio";
    public long submittedFrames => ReadStatistics().SubmittedFrames;
    public long completedFrames => ReadStatistics().CompletedFrames;
    public long droppedFrames => ReadStatistics().DroppedFrames;
    public int pendingBuffers => ReadStatistics().PendingBuffers;

    public void SetExpectedSubmissionFrames(int frames)
    { ObjectDisposedException.ThrowIf(_disposed, this); if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames)); }

    public void Submit(ReadOnlySpan<float> interleavedSamples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (interleavedSamples.IsEmpty || interleavedSamples.Length % channelCount != 0) return;
        TrySubmit(interleavedSamples, out _);
    }

    public bool TrySubmit(ReadOnlySpan<float> interleavedSamples, out int acceptedFrames)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        acceptedFrames = 0;
        if (interleavedSamples.IsEmpty) return true;
        if (interleavedSamples.Length % channelCount != 0) return false;
        // Browser MemoryView supports byte spans. JS copies this read-only alias
        // synchronously before the pooled mix buffer can be returned or reused.
        var bytes = MemoryMarshal.AsBytes(interleavedSamples);
        acceptedFrames = WebAudioInterop.TrySubmitPcm(_handle,
            MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(bytes), bytes.Length));
        return acceptedFrames == interleavedSamples.Length / channelCount;
    }

    public void Dispose()
    {
        if (_disposed) return;
        WebAudioInterop.Dispose(_handle);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private WebAudioStatistics ReadStatistics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return JsonSerializer.Deserialize(
                   WebAudioInterop.GetStatistics(_handle),
                   WebAudioJsonContext.Default.WebAudioStatistics) ?? new();
    }
}

internal sealed class WebAudioStatistics
{
    public long SubmittedFrames { get; set; }
    public long CompletedFrames { get; set; }
    public long DroppedFrames { get; set; }
    public int PendingBuffers { get; set; }
    public double BufferedMilliseconds { get; set; }
}

[JsonSerializable(typeof(WebAudioStatistics))]
internal sealed partial class WebAudioJsonContext : JsonSerializerContext;

[SupportedOSPlatform("browser")]
internal static partial class WebAudioInterop
{
    [JSImport("globalThis.BEngineWebAudio.create")]
    internal static partial int Create(int sampleRate, int channelCount, int bufferMilliseconds);
    [JSImport("globalThis.BEngineWebAudio.trySubmitPcm")]
    internal static partial int TrySubmitPcm(int handle, [JSMarshalAs<JSType.MemoryView>] Span<byte> samples);
    [JSImport("globalThis.BEngineWebAudio.getQueuedFrames")]
    internal static partial int GetQueuedFrames(int handle);
    [JSImport("globalThis.BEngineWebAudio.getAvailableFrames")]
    internal static partial int GetAvailableFrames(int handle);
    [JSImport("globalThis.BEngineWebAudio.getStatistics")]
    internal static partial string GetStatistics(int handle);
    [JSImport("globalThis.BEngineWebAudio.dispose")]
    internal static partial void Dispose(int handle);
}
