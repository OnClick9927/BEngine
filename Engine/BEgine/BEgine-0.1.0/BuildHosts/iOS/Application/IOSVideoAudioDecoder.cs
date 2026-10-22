using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AudioToolbox;
using AVFoundation;
using BEngine.Platform;
using CoreMedia;
using Foundation;

namespace BEngine.Player.Hosts.iOS;

#pragma warning disable CA1422
internal sealed class IOSVideoAudioDecoder : TimestampedPcmReader
{
    private AVUrlAsset? _asset;
    private AVAssetTrack? _track;
    private AVAssetReader? _reader;
    private AVAssetReaderTrackOutput? _output;
    private string? _temporary;
    public override int Channels => 2;
    public override int Frequency => 48000;
    public override int Samples { get; }
    internal IOSVideoAudioDecoder(string source, ReadOnlyMemory<byte> bytes)
    {
        try
        {
            if (!bytes.IsEmpty) { _temporary = Path.Combine(Path.GetTempPath(), "bengine-audio-" + Guid.NewGuid().ToString("N")); File.WriteAllBytes(_temporary, bytes.ToArray()); source = _temporary; }
            using var url = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? new NSUrl(source) : NSUrl.FromFilename(source);
            _asset = new AVUrlAsset(url, new AVUrlAssetOptions());
            _track = _asset.GetTracks(AVMediaTypes.Audio).FirstOrDefault() ?? throw new InvalidDataException("No Apple container audio track.");
            var duration = _asset.Duration.Seconds;
            if (!double.IsFinite(duration) || duration <= 0 || duration * Frequency > int.MaxValue) throw new InvalidDataException("Apple audio duration exceeds mixer limits.");
            Samples = checked((int)Math.Ceiling(duration * Frequency));
            SeekDecoder(0);
        }
        catch { Dispose(); throw; }
    }
    protected override bool ReadPacket(out int startFrame, out float[] samples)
    {
        startFrame = 0; samples = [];
        using var sample = _output!.CopyNextSampleBuffer();
        if (sample is null)
        {
            if (_reader!.Status == AVAssetReaderStatus.Failed) throw new IOException(_reader.Error?.LocalizedDescription ?? "Apple audio decoding failed.");
            return false;
        }
        using var block = sample.GetDataBuffer() ?? throw new InvalidDataException("Apple PCM sample has no data buffer.");
        var size = checked((int)block.DataLength);
        if (size > 16 * 1024 * 1024 || size % (Channels * 4) != 0) throw new InvalidDataException("Apple PCM buffer is invalid.");
        samples = new float[size / 4]; var pin = GCHandle.Alloc(samples, GCHandleType.Pinned);
        try
        {
            if (CMBlockBufferCopyDataBytes(block.Handle, 0, (nuint)size, pin.AddrOfPinnedObject()) != 0) throw new InvalidDataException("Apple PCM buffer copy failed.");
        }
        finally { pin.Free(); }
        startFrame = checked((int)Math.Round(sample.PresentationTimeStamp.Seconds * Frequency)); return true;
    }
    protected override void SeekDecoder(int frame)
    {
        _reader?.CancelReading(); _output?.Dispose(); _reader?.Dispose(); _output = null; _reader = null;
        _reader = new AVAssetReader(_asset!, out var error);
        if (error is not null) throw new IOException(error.LocalizedDescription);
        var settings = new AudioSettings { Format = AudioFormatType.LinearPCM, SampleRate = Frequency, NumberChannels = Channels,
            LinearPcmBitDepth = 32, LinearPcmFloat = true, LinearPcmBigEndian = false, LinearPcmNonInterleaved = false };
        _output = AVAssetReaderTrackOutput.Create(_track!, settings); _output.AlwaysCopiesSampleData = false;
        _reader.AddOutput(_output);
        var start = CMTime.FromSeconds(Math.Max(0, frame / (double)Frequency - .25), 48000);
        _reader.TimeRange = new CMTimeRange { Start = start, Duration = CMTime.Subtract(_asset!.Duration, start) };
        if (!_reader.StartReading()) throw new IOException(_reader.Error?.LocalizedDescription ?? "Apple audio reader could not start.");
    }
    protected override void DisposeDecoder()
    {
        _reader?.CancelReading(); _output?.Dispose(); _reader?.Dispose(); _track?.Dispose(); _asset?.Dispose();
        _output = null; _reader = null; _track = null; _asset = null;
        if (_temporary is not null) { File.Delete(_temporary); _temporary = null; }
    }
    [DllImport("/System/Library/Frameworks/CoreMedia.framework/CoreMedia")]
    private static extern int CMBlockBufferCopyDataBytes(IntPtr block, nuint offset, nuint length, IntPtr destination);
}
#pragma warning restore CA1422
