using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AVFoundation;
using BEngine.Platform;
using CoreGraphics;
using CoreMedia;
using Foundation;

namespace BEngine.Player.Hosts.iOS;

// Metadata and synchronous extraction run on the AsyncVideoFrameReader worker for compatibility with iOS 15.
#pragma warning disable CA1422
internal sealed class IOSVideoDecoder : IPlatformVideoFrameReader
{
    private readonly AVUrlAsset _asset;
    private readonly AVAssetImageGenerator _generator;
    private readonly byte[] _pixels;
    private readonly string? _temporary;
    public int Width { get; }
    public int Height { get; }
    public double FrameRate { get; }
    public double Duration { get; }
    public double Timestamp { get; private set; }
    public string Backend => "Apple AVFoundation";
    public bool HasAudio { get; }
    public IOSVideoDecoder(string source, ReadOnlyMemory<byte> bytes, bool requireHardware)
    {
        if (requireHardware) throw new NotSupportedException("AVAssetImageGenerator does not expose proof of hardware decode.");
        if (!bytes.IsEmpty) { _temporary = Path.Combine(Path.GetTempPath(), "bengine-video-" + Guid.NewGuid().ToString("N")); File.WriteAllBytes(_temporary, bytes.ToArray()); source = _temporary; }
        _asset = new AVUrlAsset(Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? new NSUrl(source) : NSUrl.FromFilename(source), new AVUrlAssetOptions());
        _generator = new AVAssetImageGenerator(_asset) { AppliesPreferredTrackTransform = true, RequestedTimeToleranceBefore = CMTime.Zero, RequestedTimeToleranceAfter = CMTime.Zero };
        try
        {
            var track = _asset.GetTracks(AVMediaTypes.Video).FirstOrDefault() ?? throw new InvalidDataException("No Apple video track was found.");
            FrameRate = track.NominalFrameRate > 0 ? track.NominalFrameRate : 30; Duration = _asset.Duration.Seconds;
            HasAudio = _asset.GetTracks(AVMediaTypes.Audio).Length > 0;
            using var first = _generator.CopyCGImageAtTime(CMTime.Zero, out _, out var error) ?? throw new InvalidDataException(error?.LocalizedDescription ?? "Apple video frame extraction failed.");
            Width = checked((int)first.Width); Height = checked((int)first.Height);
            if (Duration <= 0 || !double.IsFinite(Duration) || (long)Width * Height > 16_777_216) throw new InvalidDataException("Apple video metadata exceeds decoder limits.");
            _pixels = new byte[checked(Width * Height * 4)];
        }
        catch { Dispose(); throw; }
    }
    public void ReadFrame(double seconds, Span<byte> rgba)
    {
        using var frame = _generator.CopyCGImageAtTime(CMTime.FromSeconds(Math.Min(seconds, Math.Max(0, Duration - 1 / FrameRate)), 600_000), out var actual, out var error)
            ?? throw new InvalidDataException(error?.LocalizedDescription ?? "Apple video frame extraction failed.");
        var pin = GCHandle.Alloc(_pixels, GCHandleType.Pinned);
        try
        {
            using var colors = CGColorSpace.CreateDeviceRGB();
            using var context = new CGBitmapContext(pin.AddrOfPinnedObject(), Width, Height, 8, Width * 4, colors, CGImageAlphaInfo.PremultipliedLast);
            context.TranslateCTM(0, Height); context.ScaleCTM(1, -1); context.DrawImage(new CGRect(0, 0, Width, Height), frame);
            _pixels.CopyTo(rgba); Timestamp = actual.Seconds;
        }
        finally { pin.Free(); }
    }
    public void Dispose() { _generator.Dispose(); _asset.Dispose(); if (_temporary is not null) File.Delete(_temporary); }
}
#pragma warning restore CA1422
