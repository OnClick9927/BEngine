using System.Text.Json;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Video;
using Microsoft.Extensions.DependencyInjection;

static class AdapterTests
{
    public static void Run(Action<bool, string> check, string root)
    {
        void Throws<T>(Action action, string name) where T : Exception
        { try { action(); } catch (T) { check(true, name); return; } throw new Exception(name); }
        var defaultDecoder = PlatformRuntime.GetService<IPlatformVideoDecoder>();
        check(defaultDecoder is WindowsMediaDecoder, "Desktop selects the native Windows host");
        check(PlatformRuntime.GetService<IPlatformExternalVideoDecoder>() is FfmpegMediaDecoder, "External decoder remains independently addressable");
        check(defaultDecoder!.GetType().Assembly != typeof(VideoClip).Assembly, "Native media implementation is outside the Video assembly");
        check(!typeof(VideoClip).Assembly.GetReferencedAssemblies().Any(a => a.Name is "BEngine.Platform.Desktop" or "BEngine.Platform.DotNet"), "Video does not reference concrete platform assemblies");
        var fake = new Decoder();
        using var provider = new ServiceCollection().AddSingleton<IPlatformVideoDecoder>(fake).AddBEngineDesktopPlatform().BuildServiceProvider();
        PlatformRuntime.Initialize(provider);
        check(ReferenceEquals(PlatformRuntime.GetRequiredService<IPlatformVideoDecoder>(), fake), "Explicit decoder survives Desktop defaults");
        check(VideoPlaybackCapabilities.Current.OriginalContainers && !VideoPlaybackCapabilities.Current.HardwareDecoding, "Current capabilities come from the actual host");
        foreach (var mode in Enum.GetValues<VideoDecodeMode>())
        {
            using var reader = VideoDecoder.Open("custom:clip", mode);
            check((int)fake.Mode == (int)mode, "Decode mode reaches host: " + mode);
            check(!reader.canSeek && reader.requiresPolling && reader.hasPendingFrame, "Host seek/polling flags survive adaptation: " + mode);
            check(reader.decodedFrames == 73 && reader.hardwareDecodedFrames == 19, "Host actual decode counters survive adaptation: " + mode);
        }
        check(fake.Disposed == 3, "Each adapted host reader is disposed once");
        var previousBudget = VideoRuntimeSettings.maximumEstimatedMemoryBytes;
        try
        {
            VideoRuntimeSettings.maximumEstimatedMemoryBytes = 8 * 1024 * 1024 + 64;
            fake.Huge = true;
            Throws<InvalidOperationException>(() => VideoDecoder.Open("custom:large"), "Host admission rejects large dimensions before pixel allocation");
            check(fake.Allocated == 3 && VideoRuntimeSettings.activeDecoders == 0, "Rejected dimensions allocate no fake frame and release admission");
        }
        finally { fake.Huge = false; VideoRuntimeSettings.maximumEstimatedMemoryBytes = previousBudget; }
        using (var audio = VideoDecoder.OpenAudio("custom:audio"))
        {
            var pcm = new float[4]; audio.Read(pcm, 0, 4);
            check(pcm.SequenceEqual(new float[] { 0, 1, -1, .5f }), "Domain adapter applies Audio normalization");
            check(audio.hasPendingSamples, "Pending native samples survive adaptation");
        }
        Throws<ArgumentNullException>(() => new PlatformVideoFrameReader(null!), "Null frame adapter rejected immediately");
        Throws<ArgumentNullException>(() => new PlatformVideoAudioReader(null!), "Null PCM adapter rejected immediately");
        var legacyFrames = new Frames(); var wasHardware = false;
        try
        {
            PlatformVideoDecoding.OpenVideo = (_, _, hardware) => { wasHardware = hardware; return legacyFrames; };
            using var reader = VideoDecoder.Open("custom:legacy", VideoDecodeMode.HardwareRequired);
            check(wasHardware && reader.width == 2, "Existing browser/mobile/custom delegates retain precedence");
        }
        finally { PlatformVideoDecoding.OpenVideo = null; }
        check(legacyFrames.Disposed == 1, "Legacy delegate lifetime retained");
        using (var fallback = new PlatformVideoFrameReader(new UnknownCounterFrames()))
        {
            var pixels = new byte[8]; fallback.ReadFrame(0, pixels); fallback.ReadFrame(0, pixels);
            check(fallback.decodedFrames == 2 && fallback.hardwareDecodedFrames == 0 && fallback.canSeek, "Old native reader defaults preserve fallback counters and seeking");
        }
        using (var reader = new PlatformVideoFrameReader(new Frames()))
        {
            var pixels = new byte[8]; for (var i = 0; i < 10000; i++) reader.ReadFrame(0, pixels);
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++) reader.ReadFrame(0, pixels);
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            File.WriteAllText(Path.Combine(root, "AdapterPerformance.json"), JsonSerializer.Serialize(new { Reads = 10000, AllocatedBytes = allocated }));
            check(allocated == 0, "10000 warmed frame adapter reads allocate zero managed bytes");
        }
        // FFmpeg must also reject admission before allocating a frame or launching frame-index work.
        using var externalProvider = new ServiceCollection().AddSingleton<IPlatformVideoDecoder>(FfmpegMediaDecoder.Shared).BuildServiceProvider();
        PlatformRuntime.Initialize(externalProvider);
        var calls = Path.Combine(root, "RejectedExternalCalls.log");
        Environment.SetEnvironmentVariable("BENGINE_MEDIA_CALL_LOG", calls);
        try
        {
            VideoRuntimeSettings.maximumEstimatedMemoryBytes = 8 * 1024 * 1024 + 64;
            Throws<InvalidOperationException>(() => VideoDecoder.Open("fixture.mp4"), "External dimensions reject before frame allocation");
            check(File.ReadAllLines(calls).Length == 1 && !File.ReadAllText(calls).Contains("-select_streams"), "Rejected external source launches metadata probe only");
        }
        finally
        {
            VideoRuntimeSettings.maximumEstimatedMemoryBytes = previousBudget;
            Environment.SetEnvironmentVariable("BENGINE_MEDIA_CALL_LOG", null);
            DesktopPlatformServices.Initialize();
        }
        check(VideoPlaybackCapabilities.ForPlatform(VideoPlatformFamily.Windows).HardwareDecoding, "Build-time target capability remains available");
    }
    sealed class Decoder : IPlatformVideoDecoder
    {
        public PlatformVideoDecoderCapabilities Capabilities => new(true, true, false);
        public PlatformVideoDecodeMode Mode; public int Disposed, Allocated; public bool Huge;
        public IPlatformVideoFrameReader OpenVideo(string source, ReadOnlyMemory<byte> bytes, PlatformVideoOpenOptions options)
        {
            Mode = options.Mode; options.PrepareDimensions?.Invoke(Huge ? 8192 : 2, Huge ? 8192 : 1);
            Allocated++; return new Frames(() => Disposed++);
        }
        public IPlatformAudioSampleReader OpenAudio(string source, ReadOnlyMemory<byte> bytes) => new Pcm();
    }
    sealed class Frames(Action? dispose = null) : IPlatformVideoFrameReader
    {
        public int Disposed; public int Width => 2; public int Height => 1; public double FrameRate => 30;
        public double Duration => 1; public double Timestamp => 0; public string Backend => "custom"; public bool HasAudio => true;
        public bool CanSeek => false; public bool RequiresPolling => true; public bool HasPendingFrame => true;
        public long DecodedFrames => 73; public long HardwareDecodedFrames => 19;
        public void ReadFrame(double seconds, Span<byte> rgba) => rgba.Fill(255);
        public void Dispose() { Disposed++; dispose?.Invoke(); }
    }
    sealed class UnknownCounterFrames : IPlatformVideoFrameReader
    {
        public int Width => 2; public int Height => 1; public double FrameRate => 30; public double Duration => 1;
        public double Timestamp => 0; public string Backend => "old host"; public bool HasAudio => false;
        public void ReadFrame(double seconds, Span<byte> rgba) => rgba.Clear(); public void Dispose() { }
    }
    sealed class Pcm : IPlatformAudioSampleReader
    {
        public int Channels => 2; public int Frequency => 48000; public int Samples => 48000; public int Position => 0;
        public bool HasPendingSamples => true;
        public int Read(float[] buffer, int offset, int count) { new float[] { float.NaN, 2, -2, .5f }.CopyTo(buffer, offset); return 4; }
        public void Seek(int frame) { } public void Dispose() { }
    }
}
