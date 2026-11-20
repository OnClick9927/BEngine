using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using BEngine;
using BEngine.AssetBundles;
using BEngine.Audio;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;

var folder = Path.GetFullPath(args[0]); Directory.CreateDirectory(folder);
var checks = new List<string>();
void Check(bool value, string message) { if (!value) throw new Exception(message); checks.Add(message); }
void Throws<T>(Action action, string message) where T : Exception
{ try { action(); } catch (T) { Check(true, message); return; } throw new Exception(message); }
DotNetPlatformServices.Initialize();
RuntimeHelpers.RunModuleConstructor(typeof(AudioClip).Module.ModuleHandle);
var providers = new List<ServiceProvider>();
void Host(IPlatformAudioOutputFactory? sink = null, IPlatformAudioDecoderFactory? decoder = null)
{
    var services = new ServiceCollection().AddBEngineDotNetPlatform();
    if (sink is not null) services.AddSingleton(sink);
    if (decoder is not null) services.AddSingleton(decoder);
    var provider = services.BuildServiceProvider(); providers.Add(provider); PlatformRuntime.Initialize(provider);
}
Host();
var factory = PlatformAudioOutputFactory.Shared;
Check(ReferenceEquals(factory.CreateDefault(), factory.SilentOutput), "No host output capability falls back to silent output");
Check(!factory.IsSystemOutput(factory.SilentOutput), "Silent output is not reported as a system device");
var basic = new BasicSink(); var basicFactory = new SinkFactory(basic);
Host(basicFactory);
AudioRuntimeSettings.outputBufferMilliseconds = 240;
var output = factory.CreateDefault();
Check(basicFactory.Options == new PlatformAudioOutputOptions(48000, 2, 240), "Audio queue policy is passed to the host factory");
Check(output is not IAudioBufferedOutput && output is not IIndependentAudioOutput, "Plain native sink does not acquire unsupported queue capabilities");
Check(output.sampleRate == 48000 && output.channelCount == 2 && factory.IsSystemOutput(output), "Native format and system identity are adapted");
var samples = new float[] { .25f, -.5f, .125f, .75f };
output.Submit(samples);
Check(basic.Last.AsSpan().SequenceEqual(samples), "Interleaved samples reach the native sink unchanged");
var diagnostics = (IAudioOutputDiagnostics)output;
Check(diagnostics.backend == "probe" && diagnostics.submittedFrames == 2 && diagnostics.completedFrames == 2 && diagnostics.droppedFrames == 0 && diagnostics.pendingBuffers == 0 && diagnostics.underrunEvents == 3 && diagnostics.bufferedMilliseconds == 12.5, "All native diagnostics are preserved");
((IDisposable)output).Dispose(); Check(basic.Disposals == 1, "Adapter releases its native sink");
var legacy = new LegacyFactory(); Host(legacy);
((IDisposable)factory.CreateDefault()).Dispose();
Check(legacy.Calls == 1, "Existing two-argument platform factories remain supported");
var buffered = new BufferedSink(); Host(new SinkFactory(buffered));
var queued = factory.CreateDefault();
Check(queued is IAudioBufferedOutput && queued is not IIndependentAudioOutput, "Buffered output preserves the independent-thread opt-in boundary");
((IAudioBufferedOutput)queued).SetExpectedSubmissionFrames(9);
Check(buffered.Hint == 9, "Producer queue hint reaches the native sink");
Check(!((IAudioBufferedOutput)queued).TrySubmit(new float[16], out var accepted) && accepted == 4, "Backpressure and accepted prefix are preserved");
Check(((IAudioBufferedOutput)queued).capacityFrames == 4 && ((IAudioBufferedOutput)queued).queuedFrames == 4 && ((IAudioBufferedOutput)queued).availableFrames == 0, "Queue counters remain native-owned");
Throws<ArgumentOutOfRangeException>(() => ((IAudioBufferedOutput)queued).SetExpectedSubmissionFrames(-1), "Invalid queue hint remains rejected");
((IDisposable)queued).Dispose();
var independent = new IndependentSink(); Host(new SinkFactory(independent));
var independentOutput = factory.CreateDefault();
Check(independentOutput is IIndependentAudioOutput, "Native independent-thread capability survives adaptation");
((IDisposable)independentOutput).Dispose();
var steady = new SteadySink(); Host(new SinkFactory(steady));
var steadyOutput = (IAudioBufferedOutput)factory.CreateDefault();
for (var i = 0; i < 100; i++) steadyOutput.TrySubmit(samples, out _);
var allocated = GC.GetAllocatedBytesForCurrentThread();
for (var i = 0; i < 1000; i++) steadyOutput.TrySubmit(samples, out _);
allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
Check(allocated == 0, "Steady queue adaptation allocates zero bytes");
((IDisposable)steadyOutput).Dispose();
var old = new BasicSink(); Host(new SinkFactory(old)); AudioOutput.ResetToSystemDefault();
var replacement = new BasicSink(); Host(new SinkFactory(replacement)); AudioOutput.ResetToSystemDefault();
Check(old.Disposals == 1 && replacement.Disposals == 0, "Reset disposes only the previous output");
AudioOutput.Shutdown(); Check(replacement.Disposals == 1, "Shutdown releases the current native sink");

var managers = new List<VirtualAssetBundleManager>(); var clips = new List<AudioClip>();
AudioClip Clip(byte[] bytes)
{
    var name = "Assets/Resources/probe" + managers.Count + ".bgaudio";
    var entry = VirtualAssetBundleEntry.FromMemory(name, bytes, "AudioClip", Guid.NewGuid());
    var manager = new VirtualAssetBundleManager(new VirtualAssetBundleSnapshot("probe", Guid.NewGuid().ToString(), [entry])); managers.Add(manager);
    var task = manager.InitializeAsync().AsBTask();
    for (var i = 0; !task.IsCompleted && i < 500; i++) BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(16));
    task.GetAwaiter().GetResult();
    var provider = new AssetBundleResourceProvider(manager);
    if (!provider.TryLoadAsset(name, "Resources", typeof(AudioClip), out var asset)) throw new Exception("Production audio asset codec did not load the bundle fixture");
    var clip = (AudioClip)asset; clips.Add(clip); return clip;
}
void Until(Func<bool> ready)
{
    for (var i = 0; i < 500 && !ready(); i++) BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(16));
    if (!ready()) throw new TimeoutException("Audio work did not reach its expected state");
}
float[] Data(AudioClip clip)
{
    var data = new float[clip.samples * clip.channels];
    if (!clip.GetData(data, 0)) throw new Exception("Decoded PCM is unavailable"); return data;
}
var baseline = AudioLoadingSettings.snapshot;
AudioLoadingSettings.usePlatformDecoding = false; Host();
var bytes = Fixture(20000, 48000);
var managed = Clip(bytes); Until(() => managed.loadState != AudioDataLoadState.Loading);
Check(managed.loadState == AudioDataLoadState.Loaded, "Production bundle codec completes the managed background path");
var expected = Data(managed); BObject.DestroyImmediate(managed);
AudioLoadingSettings.usePlatformDecoding = true;
var decode = new DecodeFactory(expected); Host(decoder: decode);
var pending = Clip(bytes); Until(() => decode.Operations.Count == 1);
Check(pending.loadState == AudioDataLoadState.Loading && decode.Operations[0].Copies == 0, "Pending native decoding does not expose partial PCM");
Check(decode.Options.Frames == 20000 && decode.Options.Channels == 2 && decode.Options.Frequency == 48000 && !decode.Options.Mono && decode.Options.Gain == 1, "Native decoder receives authored format and transforms");
decode.Operations[0].State = PlatformAudioDecodeStatus.Ready;
Until(() => pending.loadState != AudioDataLoadState.Loading);
Check(Data(pending).AsSpan().SequenceEqual(expected), "Native decode through the production bundle codec produces exact PCM");
Check(decode.Operations[0].Copies > 1 && decode.Operations[0].MaxCopy <= 32768 && decode.Operations[0].Disposals == 1, "Native PCM transfers remain bounded and release the completed operation");
BObject.DestroyImmediate(pending);
var failed = new DecodeFactory(expected) { Initial = PlatformAudioDecodeStatus.Failed }; Host(decoder: failed);
var fallback = Clip(bytes); Until(() => fallback.loadState != AudioDataLoadState.Loading);
Check(Data(fallback).AsSpan().SequenceEqual(expected) && failed.Operations.Count == 1 && failed.Operations[0].Disposals == 1, "Failed native attempt is released once and falls back to managed decoding");
BObject.DestroyImmediate(fallback);
var canceledFactory = new DecodeFactory(expected); Host(decoder: canceledFactory);
var canceled = Clip(bytes); Until(() => canceledFactory.Operations.Count == 1);
Check(canceled.UnloadAudioData(), "Pending clip can be unloaded");
Check(canceledFactory.Operations[0].Disposals == 1 && canceled.loadState == AudioDataLoadState.Unloaded, "Unload cancels and releases the pending native operation");
BObject.DestroyImmediate(canceled);
var brokenFactory = new DecodeFactory(expected) { Initial = PlatformAudioDecodeStatus.Ready, Incomplete = true }; Host(decoder: brokenFactory);
var broken = Clip(bytes); Until(() => broken.loadState != AudioDataLoadState.Loading);
Check(broken.loadState == AudioDataLoadState.Failed && brokenFactory.Operations[0].Disposals == 1, "Incomplete native PCM fails cleanly and releases its operation");
BObject.DestroyImmediate(broken);
var disabled = new DecodeFactory(expected); Host(decoder: disabled); AudioLoadingSettings.usePlatformDecoding = false;
var managedOnly = Clip(bytes); Until(() => managedOnly.loadState != AudioDataLoadState.Loading);
Check(disabled.Operations.Count == 0 && Data(managedOnly).AsSpan().SequenceEqual(expected), "Decoder opt-out remains package policy");
BObject.DestroyImmediate(managedOnly); AudioLoadingSettings.usePlatformDecoding = true;
var smallFactory = new DecodeFactory([]); Host(decoder: smallFactory);
var small = Clip(Fixture(64, 48000)); Until(() => small.loadState != AudioDataLoadState.Loading);
Check(smallFactory.Operations.Count == 0 && small.loadState == AudioDataLoadState.Loaded, "Tiny effects retain the managed path");
BObject.DestroyImmediate(small);
var unusual = Clip(Fixture(10000, 192000)); Until(() => unusual.loadState != AudioDataLoadState.Loading);
Check(smallFactory.Operations.Count == 0 && unusual.loadState == AudioDataLoadState.Loaded, "Unsupported platform decode rates retain managed decoding");
BObject.DestroyImmediate(unusual);
var after = AudioLoadingSettings.snapshot;
Check(after.ActiveDecoders == baseline.ActiveDecoders && after.PendingRequests == baseline.PendingRequests && after.ReservedPcmBytes == baseline.ReservedPcmBytes && after.LoadedPcmBytes == baseline.LoadedPcmBytes, "Success, fallback, cancellation and malformed PCM leave no decoder or PCM lease");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Graphics" or "BEngine.IMGUI.Editor" or "BEngine.Core.Editor" or "BEngine.Platform.Desktop" or "BEngine.Web"), "Audio and content consumer requires no graphics, Editor or concrete device assembly");
foreach (var manager in managers) manager.Dispose();
var retiringSink = new BasicSink(); Host(new SinkFactory(retiringSink)); AudioOutput.ResetToSystemDefault();
RuntimeAssemblyCleanup.UnregisterExtensions(typeof(AudioClip).Assembly);
Check(retiringSink.Disposals == 1, "Package retirement releases the host-owned sink before dropping its adapter");
foreach (var provider in providers) provider.Dispose();
File.WriteAllText(Path.Combine(folder, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Count = checks.Count, Checks = checks, WarmAdapterAllocatedBytes = allocated, PcmSamples = expected.Length, PcmSHA256 = Convert.ToHexString(SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(expected.AsSpan()))), PostLoading = after }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"AUDIO_CAPABILITIES_AND_CONTENT_OK {checks.Count}");

static byte[] Fixture(int frames, int frequency)
{
    var wav = new byte[44 + frames * 4];
    "RIFF"u8.CopyTo(wav); BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), wav.Length - 8); "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
    BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16); BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 2);
    BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), frequency); BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), frequency * 4);
    BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 4); BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16); "data"u8.CopyTo(wav.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), frames * 4);
    for (var i = 0; i < frames * 2; i++) BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(44 + i * 2), (short)(Math.Sin(i * .013) * 8192));
    var bytes = new byte[18 + wav.Length]; "BGAUDIO2"u8.CopyTo(bytes); bytes[8] = 0; bytes[9] = 2;
    BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(10), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), wav.Length); wav.CopyTo(bytes, 18); return bytes;
}

class BasicSink : IPlatformAudioOutput
{
    public int SampleRate => 48000;
    public int ChannelCount => 2;
    public string Backend => "probe";
    public long SubmittedFrames { get; protected set; }
    public long CompletedFrames => SubmittedFrames;
    public long DroppedFrames => 0;
    public int PendingBuffers => 0;
    public long? UnderrunEvents => 3;
    public double? BufferedMilliseconds => 12.5;
    public float[] Last = [];
    public int Disposals;
    public virtual void Submit(ReadOnlySpan<float> samples) { Last = samples.ToArray(); SubmittedFrames += samples.Length / 2; }
    public void Dispose() => Disposals++;
}
class BufferedSink : BasicSink, IPlatformBufferedAudioOutput
{
    public int CapacityFrames => 4;
    public int QueuedFrames { get; protected set; }
    public int AvailableFrames => CapacityFrames - QueuedFrames;
    public int Hint;
    public void SetExpectedSubmissionFrames(int frames) { if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames)); Hint = frames; }
    public virtual bool TrySubmit(ReadOnlySpan<float> samples, out int acceptedFrames)
    { acceptedFrames = Math.Min(samples.Length / 2, AvailableFrames); QueuedFrames += acceptedFrames; SubmittedFrames += acceptedFrames; return acceptedFrames == samples.Length / 2; }
}
sealed class IndependentSink : BufferedSink, IPlatformIndependentAudioOutput;
sealed class SteadySink : BufferedSink
{
    public override bool TrySubmit(ReadOnlySpan<float> samples, out int acceptedFrames)
    { acceptedFrames = samples.Length / 2; SubmittedFrames += acceptedFrames; return true; }
}
sealed class SinkFactory(IPlatformAudioOutput output) : IPlatformAudioOutputFactory
{
    public PlatformAudioOutputOptions Options;
    public IPlatformAudioOutput CreateDefault(int sampleRate, int channelCount) => output;
    public IPlatformAudioOutput CreateDefault(PlatformAudioOutputOptions options) { Options = options; return output; }
}
sealed class LegacyFactory : IPlatformAudioOutputFactory
{
    public int Calls;
    public IPlatformAudioOutput CreateDefault(int sampleRate, int channelCount) { Calls++; return new BasicSink(); }
}
sealed class DecodeFactory(float[] pcm) : IPlatformAudioDecoderFactory
{
    public PlatformAudioDecodeStatus Initial = PlatformAudioDecodeStatus.Pending;
    public bool Incomplete;
    public PlatformAudioDecodeOptions Options;
    public readonly List<DecodeOperation> Operations = [];
    public IPlatformAudioDecodeOperation TryCreate(ReadOnlyMemory<byte> encoded, PlatformAudioDecodeOptions options)
    { Options = options; var operation = new DecodeOperation(pcm, options.Channels) { State = Initial, Incomplete = Incomplete }; Operations.Add(operation); return operation; }
}
sealed class DecodeOperation(float[] pcm, int channels) : IPlatformAudioDecodeOperation
{
    public PlatformAudioDecodeStatus State;
    public bool Incomplete;
    public PlatformAudioDecodeStatus Status => State;
    public int Copies, MaxCopy, Disposals;
    public int Copy(int firstFrame, Span<float> destination)
    { Copies++; MaxCopy = Math.Max(MaxCopy, destination.Length); pcm.AsSpan(firstFrame * channels, destination.Length).CopyTo(destination); return Incomplete ? destination.Length - 1 : destination.Length; }
    public void Dispose() => Disposals++;
}
