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
public sealed class WebAudioOutput : IAudioOutput, IAudioOutputDiagnostics, IDisposable
{
    private readonly int _handle;
    private bool _disposed;

    public WebAudioOutput(int sampleRate = 48000, int channelCount = 2)
    {
        if (sampleRate is < 8000 or > 384000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channelCount is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channelCount));
        this.sampleRate = sampleRate;
        this.channelCount = channelCount;
        _handle = WebAudioInterop.Create(sampleRate, channelCount);
        if (_handle <= 0) throw new InvalidOperationException("The browser did not create a Web Audio output.");
    }

    public int sampleRate { get; }
    public int channelCount { get; }
    public string backend => "Web Audio";
    public long submittedFrames => ReadStatistics().SubmittedFrames;
    public long completedFrames => ReadStatistics().CompletedFrames;
    public long droppedFrames => ReadStatistics().DroppedFrames;
    public int pendingBuffers => ReadStatistics().PendingBuffers;

    public void Submit(ReadOnlySpan<float> interleavedSamples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (interleavedSamples.IsEmpty || interleavedSamples.Length % channelCount != 0) return;
        WebAudioInterop.Submit(_handle,
            Convert.ToBase64String(MemoryMarshal.AsBytes(interleavedSamples)));
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
}

[JsonSerializable(typeof(WebAudioStatistics))]
internal sealed partial class WebAudioJsonContext : JsonSerializerContext;

[SupportedOSPlatform("browser")]
internal static partial class WebAudioInterop
{
    [JSImport("globalThis.BEngineWebAudio.create")]
    internal static partial int Create(int sampleRate, int channelCount);
    [JSImport("globalThis.BEngineWebAudio.submit")]
    internal static partial void Submit(int handle, string samplesBase64);
    [JSImport("globalThis.BEngineWebAudio.getStatistics")]
    internal static partial string GetStatistics(int handle);
    [JSImport("globalThis.BEngineWebAudio.dispose")]
    internal static partial void Dispose(int handle);
}
