using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Android.Media;
using BEngine.Platform;

namespace BEngine.Player.Hosts.Android;

internal sealed class AndroidVideoDecoder : IPlatformVideoFrameReader
{
    private readonly MediaMetadataRetriever _retriever = new();
    private readonly int[] _pixels;
    private readonly string? _temporary;
    public int Width { get; }
    public int Height { get; }
    public double FrameRate { get; } = 30;
    public double Duration { get; }
    public double Timestamp { get; private set; }
    public string Backend => "Android MediaMetadataRetriever";
    public bool HasAudio { get; }
    public AndroidVideoDecoder(string source, ReadOnlyMemory<byte> bytes, bool requireHardware)
    {
        if (requireHardware) { _retriever.Dispose(); throw new NotSupportedException("Android frame extraction does not expose proof of hardware decode."); }
        try
        {
            if (!bytes.IsEmpty) { _temporary = Path.Combine(Path.GetTempPath(), "bengine-video-" + Guid.NewGuid().ToString("N")); File.WriteAllBytes(_temporary, bytes.ToArray()); source = _temporary; }
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") _retriever.SetDataSource(source, new Dictionary<string, string>());
            else _retriever.SetDataSource(source);
            Duration = double.Parse(_retriever.ExtractMetadata(MetadataKey.Duration) ?? "0", CultureInfo.InvariantCulture) / 1000;
            HasAudio = _retriever.ExtractMetadata(MetadataKey.HasAudio) == "yes";
            using var frame = _retriever.GetFrameAtTime(0, Option.Closest) ?? throw new InvalidDataException("Android could not decode the first video frame.");
            Width = frame.Width; Height = frame.Height;
            if (Duration <= 0 || Width <= 0 || Height <= 0 || (long)Width * Height > 16_777_216) throw new InvalidDataException("Android video dimensions or duration are invalid.");
            _pixels = new int[checked(Width * Height)];
            using var extractor = new MediaExtractor();
            extractor.SetDataSource(source, new Dictionary<string, string>());
            for (var track = 0; track < extractor.TrackCount; track++)
            {
                using var format = extractor.GetTrackFormat(track);
                if (format.GetString(MediaFormat.KeyMime)?.StartsWith("video/", StringComparison.Ordinal) == true && format.ContainsKey(MediaFormat.KeyFrameRate))
                { FrameRate = format.GetInteger(MediaFormat.KeyFrameRate); break; }
            }
            if (FrameRate <= 0) FrameRate = 30;
        }
        catch { Dispose(); throw; }
    }
    public void ReadFrame(double seconds, Span<byte> rgba)
    {
        using var frame = _retriever.GetFrameAtTime((long)(Math.Min(seconds, Math.Max(0, Duration - 1 / FrameRate)) * 1_000_000), Option.Closest) ?? throw new InvalidDataException("Android video frame extraction failed.");
        if (frame.Width != Width || frame.Height != Height) throw new InvalidDataException("Android video dimensions changed during playback.");
        frame.GetPixels(_pixels, 0, Width, 0, 0, Width, Height);
        for (var index = 0; index < _pixels.Length; index++)
        {
            var argb = (uint)_pixels[index]; var offset = index * 4;
            rgba[offset] = (byte)(argb >> 16); rgba[offset + 1] = (byte)(argb >> 8); rgba[offset + 2] = (byte)argb; rgba[offset + 3] = (byte)(argb >> 24);
        }
        // Android's bitmap API does not return a presentation timestamp; retain the requested time.
        Timestamp = seconds;
    }
    public void Dispose() { _retriever.Dispose(); if (_temporary is not null) File.Delete(_temporary); }
}
