using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using BEngine.Platform;

if (OperatingSystem.IsBrowser()) await Probe.Run();

[SupportedOSPlatform("browser")]
internal static partial class Probe
{
    private static readonly List<string> Checks = [];
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); Checks.Add(message); }
    [JSImport("globalThis.audioProbeResult")]
    private static partial void Report(string json);
    [JSImport("globalThis.BEngineWebAudio.getDiagnostics")]
    private static partial string Diagnostics();

    internal static async Task Run()
    {
        try
        {
            WebPlatform.Initialize();
            var factory = PlatformRuntime.GetRequiredService<IPlatformAudioOutputFactory>();
            using (var output = factory.CreateDefault(new PlatformAudioOutputOptions(48000, 2, 120))!)
            {
                Check(output is IPlatformBufferedAudioOutput && output is not IPlatformIndependentAudioOutput, "Browser factory exposes its bounded single-threaded sink");
                var buffered = (IPlatformBufferedAudioOutput)output;
                Check(output.Backend == "Web Audio" && output.SampleRate == 48000 && output.ChannelCount == 2 && buffered.CapacityFrames == 5760, "Web output options and registration are correct");
                var samples = new float[480];
                Check(buffered.TrySubmit(samples, out var accepted) && accepted == 240, "Managed byte MemoryView crosses the relocated output import");
                Check(output.SubmittedFrames == 240 && buffered.QueuedFrames <= buffered.CapacityFrames && buffered.AvailableFrames >= 0, "Browser queue counters are bounded");
                Check(output.BufferedMilliseconds is >= 0, "Device-clock latency is observable through the neutral contract");
                var deviceTimer = Stopwatch.StartNew();
                while (output.CompletedFrames < 240 && deviceTimer.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
                Check(output.CompletedFrames == 240, "Actual Web Audio context consumes submitted frames");
                output.Dispose(); output.Dispose();
                try { output.Submit(samples); throw new Exception("Disposed output accepted samples"); } catch (ObjectDisposedException) { Check(true, "Disposed browser output rejects submission"); }
            }
            using (var diagnostics = JsonDocument.Parse(Diagnostics()))
                Check(diagnostics.RootElement.GetProperty("OutputCount").GetInt32() == 0, "Browser output disposal releases its native handle");
            var decoder = PlatformRuntime.GetRequiredService<IPlatformAudioDecoderFactory>();
            var bytes = Wav(12000);
            using (var operation = decoder.TryCreate(bytes, new PlatformAudioDecodeOptions(12000, 2, 48000, false, 1))!)
            {
                Check(operation is not null, "Browser decode factory allocates an operation without an Audio package");
                var timer = Stopwatch.StartNew();
                while (operation!.Status == PlatformAudioDecodeStatus.Pending && timer.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
                Check(operation.Status == PlatformAudioDecodeStatus.Ready, "Relocated browser decoder completes a real WAV decode");
                var samples = new float[24000];
                Check(operation.Copy(0, samples) == samples.Length, "Managed PCM MemoryView crosses the relocated decode import");
                // Chromium's native PCM16 decoder normalizes positive samples by
                // 32767. Check its actual contract rather than the managed reader's 32768 scale.
                var nativePcm = 8192f / short.MaxValue;
                Check(samples.All(value => Math.Abs(value - nativePcm) < .0000001f), "Decoded interleaved PCM matches the browser PCM16 conversion");
                operation.Dispose(); operation.Dispose();
                try { operation.Copy(0, samples); throw new Exception("Disposed decoder accepted a copy"); } catch (ObjectDisposedException) { Check(true, "Disposed decoder rejects copies"); }
            }
            using (var operation = decoder.TryCreate(bytes, new PlatformAudioDecodeOptions(12000, 1, 48000, true, .5f))!)
            {
                var timer = Stopwatch.StartNew();
                while (operation.Status == PlatformAudioDecodeStatus.Pending && timer.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
                Check(operation.Status == PlatformAudioDecodeStatus.Ready, "Native mono/gain decoding completes");
                var samples = new float[12000];
                var nativePcm = (8192f / short.MaxValue) * .5f;
                Check(operation.Copy(0, samples) == samples.Length && samples.All(value => Math.Abs(value - nativePcm) < .0000001f), "Authored mono and normalization transform survives native migration");
            }
            using (var pending = decoder.TryCreate(bytes, new PlatformAudioDecodeOptions(12000, 2, 48000, false, 1))!)
            { Check(pending is not null, "Pending browser decode can be created for cancellation"); pending!.Dispose(); }
            await Task.Delay(100);
            using (var diagnostics = JsonDocument.Parse(Diagnostics()))
            {
                Check(diagnostics.RootElement.GetProperty("NativeDecodeActive").GetInt32() == 0 && diagnostics.RootElement.GetProperty("NativeDecodePending").GetInt32() == 0, "Browser decode completion and cancellation leave no running work");
                Check(diagnostics.RootElement.GetProperty("NativeDecodeCanceled").GetInt32() >= 1, "Canceled browser work is observed by the native bridge");
            }
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Audio" or "BEngine.Graphics" or "BEngine.Core.Editor"), "Real browser probe loads no Audio, Graphics or Editor package");
            Report(JsonSerializer.Serialize(new { Passed = true, Count = Checks.Count, Checks, BrowserDiagnostics = Diagnostics(), AcousticLoopback = false }));
        }
        catch (Exception exception) { Report(JsonSerializer.Serialize(new { Passed = false, Error = exception.ToString(), Checks })); }
    }

    private static byte[] Wav(int frames)
    {
        var bytes = new byte[44 + frames * 4]; "RIFF"u8.CopyTo(bytes); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8); "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), 48000); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), 192000); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 4); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
        "data"u8.CopyTo(bytes.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), frames * 4);
        for (var i = 0; i < frames * 2; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44 + i * 2), 8192);
        return bytes;
    }
}
