using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BEngine.Platform;

namespace BEngine.Player.Hosts.Web;

[SupportedOSPlatform("browser")]
internal sealed partial class WebVideoDecoder : IPlatformVideoFrameReader
{
    private readonly int _handle;
    private bool _disposed;
    public int Width { get; }
    public int Height { get; }
    public double FrameRate { get; }
    public double Duration { get; }
    public double Timestamp { get; private set; }
    public string Backend => "HTML video / Canvas RGBA";
    public bool HasAudio { get; }
    public bool RequiresPolling => true;
    public bool HasPendingFrame { get; private set; }
    private WebVideoDecoder(string metadata)
    {
        using var json = JsonDocument.Parse(metadata); var data = json.RootElement;
        _handle = data.GetProperty("handle").GetInt32(); Width = data.GetProperty("width").GetInt32(); Height = data.GetProperty("height").GetInt32();
        Duration = data.GetProperty("duration").GetDouble(); FrameRate = data.GetProperty("frameRate").GetDouble(); HasAudio = data.GetProperty("hasAudio").GetBoolean();
    }
    internal static async Task<IPlatformVideoFrameReader> Open(string source, ReadOnlyMemory<byte> bytes, bool hardware, CancellationToken cancellationToken)
    {
        if (hardware) throw new NotSupportedException("Browser frame extraction does not prove hardware decoding.");
        cancellationToken.ThrowIfCancellationRequested();
        var request = OpenNative(source, bytes.IsEmpty ? "" : Convert.ToBase64String(bytes.Span));
        // Always release a successful JS allocation if cancellation wins the race.
        string metadata;
        try { metadata = await request.WaitAsync(cancellationToken); }
        catch { _ = ReleaseCanceled(request); throw; }
        return new WebVideoDecoder(metadata);
    }
    private static async Task ReleaseCanceled(Task<string> task)
    {
        try { var metadata = await task; using var json = JsonDocument.Parse(metadata); CloseNative(json.RootElement.GetProperty("handle").GetInt32()); } catch { }
    }
    public IPlatformVideoFrameReader Clone()
    { ObjectDisposedException.ThrowIf(_disposed, this); return new WebVideoDecoder(CloneNative(_handle)); }
    public IPlatformAudioSampleReader OpenAudio()
    { ObjectDisposedException.ThrowIf(_disposed, this); return new WebAudioReader(_handle); }
    public void ReadFrame(double seconds, Span<byte> rgba)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var json = JsonDocument.Parse(ReadNative(_handle, seconds)); var data = json.RootElement;
        Convert.FromBase64String(data.GetProperty("rgba").GetString()!).CopyTo(rgba); Timestamp = data.GetProperty("timestamp").GetDouble();
        HasPendingFrame = data.GetProperty("pending").GetBoolean();
    }
    public void Dispose() { if (_disposed) return; _disposed = true; CloseNative(_handle); }
    private sealed class WebAudioReader : IPlatformAudioSampleReader
    {
        private readonly int _handle;
        private bool _disposed;
        public int Channels { get; }
        public int Frequency { get; }
        public int Samples { get; }
        public int Position { get; private set; }
        public bool HasPendingSamples { get; private set; }
        internal WebAudioReader(int video)
        {
            using var json = JsonDocument.Parse(OpenAudioNative(video)); var data = json.RootElement;
            _handle = data.GetProperty("handle").GetInt32(); Channels = data.GetProperty("channels").GetInt32();
            Frequency = data.GetProperty("frequency").GetInt32(); Samples = data.GetProperty("samples").GetInt32();
        }
        public int Read(float[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(buffer);
            if (offset < 0 || count < 0 || offset > buffer.Length - count || count % Channels != 0) throw new ArgumentOutOfRangeException(nameof(count));
            count = Math.Min(count, (Samples - Position) * Channels);
            if (count == 0) { HasPendingSamples = false; return 0; }
            var bytes = Convert.FromBase64String(ReadAudioNative(_handle, Position, count / Channels));
            HasPendingSamples = bytes.Length == 0;
            if (HasPendingSamples) return 0;
            if (bytes.Length != count * 4) throw new InvalidOperationException("Browser audio decoder returned an invalid PCM block.");
            Buffer.BlockCopy(bytes, 0, buffer, offset * 4, bytes.Length); Position += count / Channels; return count;
        }
        public void Seek(int frame)
        { ObjectDisposedException.ThrowIf(_disposed, this); if (frame < 0 || frame > Samples) throw new ArgumentOutOfRangeException(nameof(frame)); Position = frame; }
        public void Dispose() { if (_disposed) return; _disposed = true; CloseAudioNative(_handle); }
    }
    [JSImport("globalThis.BEngineWebVideo.open")]
    private static partial Task<string> OpenNative(string source, string bytesBase64);
    [JSImport("globalThis.BEngineWebVideo.clone")]
    private static partial string CloneNative(int handle);
    [JSImport("globalThis.BEngineWebVideo.read")]
    private static partial string ReadNative(int handle, double seconds);
    [JSImport("globalThis.BEngineWebVideo.close")]
    private static partial void CloseNative(int handle);
    [JSImport("globalThis.BEngineWebVideo.openAudio")]
    private static partial string OpenAudioNative(int handle);
    [JSImport("globalThis.BEngineWebVideo.readAudio")]
    private static partial string ReadAudioNative(int handle, int frame, int frames);
    [JSImport("globalThis.BEngineWebVideo.closeAudio")]
    private static partial void CloseAudioNative(int handle);
}
