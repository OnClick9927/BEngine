using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BEngine.Platform;

[SupportedOSPlatform("browser")]
public sealed class WebAudioOutput : IPlatformBufferedAudioOutput
{
    private readonly int _handle;
    private bool _disposed;

    public WebAudioOutput(int SampleRate = 48000, int ChannelCount = 2, int bufferMilliseconds = 120)
    {
        if (SampleRate is < 8000 or > 384000) throw new ArgumentOutOfRangeException(nameof(SampleRate));
        if (ChannelCount is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(ChannelCount));
        if (bufferMilliseconds is < 20 or > 2000) throw new ArgumentOutOfRangeException(nameof(bufferMilliseconds));
        this.SampleRate = SampleRate;
        this.ChannelCount = ChannelCount;
        CapacityFrames = checked(SampleRate * bufferMilliseconds / 1000);
        _handle = WebAudioInterop.Create(SampleRate, ChannelCount, bufferMilliseconds);
        if (_handle <= 0) throw new InvalidOperationException("The browser did not create a Web Audio output.");
    }

    public int SampleRate { get; }
    public int ChannelCount { get; }
    public int CapacityFrames { get; }
    public int QueuedFrames
    { get { ObjectDisposedException.ThrowIf(_disposed, this); return WebAudioInterop.GetQueuedFrames(_handle); } }
    public int AvailableFrames
    { get { ObjectDisposedException.ThrowIf(_disposed, this); return WebAudioInterop.GetAvailableFrames(_handle); } }
    /// <summary>Scheduled tail relative to the browser device clock, including its start lead.</summary>
    public double? BufferedMilliseconds => ReadStatistics().BufferedMilliseconds;
    public string Backend => "Web Audio";
    public long SubmittedFrames => ReadStatistics().SubmittedFrames;
    public long CompletedFrames => ReadStatistics().CompletedFrames;
    public long DroppedFrames => ReadStatistics().DroppedFrames;
    public int PendingBuffers => ReadStatistics().PendingBuffers;

    public void SetExpectedSubmissionFrames(int frames)
    { ObjectDisposedException.ThrowIf(_disposed, this); if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames)); }

    public void Submit(ReadOnlySpan<float> interleavedSamples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (interleavedSamples.IsEmpty || interleavedSamples.Length % ChannelCount != 0) return;
        TrySubmit(interleavedSamples, out _);
    }

    public bool TrySubmit(ReadOnlySpan<float> interleavedSamples, out int acceptedFrames)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        acceptedFrames = 0;
        if (interleavedSamples.IsEmpty) return true;
        if (interleavedSamples.Length % ChannelCount != 0) return false;
        // Browser MemoryView supports byte spans. JS copies this read-only alias
        // synchronously before the pooled mix buffer can be returned or reused.
        var bytes = MemoryMarshal.AsBytes(interleavedSamples);
        acceptedFrames = WebAudioInterop.TrySubmitPcm(_handle,
            MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(bytes), bytes.Length));
        return acceptedFrames == interleavedSamples.Length / ChannelCount;
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
    internal static partial int Create(int SampleRate, int ChannelCount, int bufferMilliseconds);
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
