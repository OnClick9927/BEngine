using System.Diagnostics;
using System.Text.Json;
using BEngine;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;

var folder = Path.GetFullPath(args[0]); Directory.CreateDirectory(folder);
var checks = new List<string>();
void Check(bool value, string message) { if (!value) throw new Exception(message); checks.Add(message); }
void Throws<T>(Action action, string message) where T : Exception
{ try { action(); } catch (T) { Check(true, message); return; } throw new Exception(message); }
DotNetPlatformServices.Initialize();
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "BEngine.Audio"), "Platform consumer loads no Audio package");
Throws<ArgumentOutOfRangeException>(() => new PlatformAudioOutputOptions(0, 2), "Invalid sample rate rejected");
Throws<ArgumentOutOfRangeException>(() => new PlatformAudioOutputOptions(48000, 0), "Invalid channel count rejected");
Throws<ArgumentOutOfRangeException>(() => new PlatformAudioOutputOptions(48000, 2, 0), "Invalid buffer budget rejected");
using (var services = new ServiceCollection().AddBEngineDesktopPlatform().BuildServiceProvider())
    Check(services.GetService<IPlatformAudioOutputFactory>() is DesktopAudioOutputFactory, "Desktop initialization registers lazy native device capability");
var custom = new MissingFactory();
using (var services = new ServiceCollection().AddSingleton<IPlatformAudioOutputFactory>(custom).AddBEngineDesktopPlatform().BuildServiceProvider())
    Check(ReferenceEquals(services.GetRequiredService<IPlatformAudioOutputFactory>(), custom), "Explicit host output override wins");
var queue = QueueProbe.Run(typeof(DesktopAudioOutputFactory).Assembly, "BEngine.Platform.BufferedStereoPcmStream");
File.WriteAllText(Path.Combine(folder, "Queue.json"), JsonSerializer.Serialize(queue, new JsonSerializerOptions { WriteIndented = true }));
Check(JsonSerializer.SerializeToElement(queue).GetProperty("WarmAllocatedBytes").GetInt64() == 0, "PCM queue steady loop allocates zero bytes");

using var output = DesktopAudioOutputFactory.Shared.CreateDefault(new PlatformAudioOutputOptions(48000, 2, 120));
Check(output is not null, "Actual desktop device opens");
Check(output is IPlatformIndependentAudioOutput, "Actual Windows device opts into independent queue operations");
var buffered = (IPlatformBufferedAudioOutput)output!;
Check(output!.Backend == "Windows waveOut" && output.SampleRate == 48000 && output.ChannelCount == 2, "Actual Windows waveOut backend owns the device format");
Check(buffered.CapacityFrames == 5760, "Consumer queue budget reaches native sink");
Throws<ArgumentOutOfRangeException>(() => buffered.SetExpectedSubmissionFrames(-1), "Negative producer hint rejected");
var samples = new float[480]; // Silence: test native consumption without audible content.
var timer = Stopwatch.StartNew();
long acceptedTotal = 0;
while (timer.Elapsed < TimeSpan.FromMilliseconds(180))
{
    buffered.SetExpectedSubmissionFrames(240);
    buffered.TrySubmit(samples, out var accepted); acceptedTotal += accepted;
    BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(5));
    Thread.Sleep(5);
}
var submitted = output.SubmittedFrames; var completed = output.CompletedFrames; var dropped = output.DroppedFrames;
Check(submitted == acceptedTotal && submitted > 0, "Actual device tracks accepted producer frames");
Check(completed > 0 && completed + dropped <= submitted, "Driver completion is bounded by accepted frames");
Check(buffered.QueuedFrames <= buffered.CapacityFrames && buffered.AvailableFrames >= 0, "Actual native queue remains bounded");
output.Dispose(); output.Dispose();
Check(output.CompletedFrames + output.DroppedFrames <= output.SubmittedFrames, "Disposal preserves played/cancelled accounting and is repeatable");
Throws<ObjectDisposedException>(() => output.Submit(samples), "Disposed native output rejects submission");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Audio" or "BEngine.Graphics" or "BEngine.IMGUI.Editor"), "PCM consumer initializes no Audio, Graphics or GUI package");
File.WriteAllText(Path.Combine(folder, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Count = checks.Count, Checks = checks, Backend = output.Backend, AcceptedFrames = submitted, CompletedBeforeDispose = completed, DroppedBeforeDispose = dropped, AcousticLoopback = false }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PLATFORM_AUDIO_OK {checks.Count}");

sealed class MissingFactory : IPlatformAudioOutputFactory
{
    public IPlatformAudioOutput? CreateDefault(int sampleRate, int channelCount) => null;
}
