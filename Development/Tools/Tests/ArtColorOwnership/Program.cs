using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ArtOwnershipTests;
using BEngine;
using BEngine.Platform;
using BEngine.Platform.DotNet;

var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize();
var checks = new List<string>(); var payloads = new SortedDictionary<string, string>();
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
var timer = Stopwatch.StartNew();
foreach (var mode in new[] { 1, 3, 4 }) foreach (var depth in new[] { 8, 16, 32 }) foreach (var size in new[] { 1, 255, 257, 4097 })
{
    var pixels = ArtFixtures.Pixels(mode, depth, size); var original = pixels.Rgba.ToArray();
    using var transform = ArtFixtures.Open(ArtFixtures.MakeDocument(), mode);
    ArtFixtures.Convert(transform, pixels.Rgba, pixels.Channels, pixels.Precision);
    Check(Enumerable.Range(0, size).All(i => original[i * 4 + 3] == pixels.Rgba[i * 4 + 3]), $"Untagged {mode}/{depth}/{size} alpha preserved");
    if (mode != 4) Check(original.SequenceEqual(pixels.Rgba), $"Untagged {mode}/{depth}/{size} passthrough preserved");
    else Check(Enumerable.Range(0, size).All(i => Enumerable.Range(0, 3).All(c => pixels.Rgba[i * 4 + c] == (byte)((long)pixels.Channels[(short)c][i] * pixels.Channels[3][i] * 255 / (255L * 255)))), $"Untagged CMYK {depth}/{size} Photoshop inversion preserved");
    payloads[$"Untagged-{mode}-{depth}-{size}"] = Convert.ToHexString(SHA256.HashData(pixels.Rgba));
}
foreach (var depth in new[] { 8, 16, 32 }) foreach (var size in new[] { 1, 255, 257, 4097 })
{
    var pixels = ArtFixtures.Pixels(4, depth, size); var original = pixels.Rgba.ToArray();
    using var transform = ArtFixtures.Open(ArtFixtures.MakeDocument(ArtFixtures.Profile(ArtFixtures.Cmyk)), 4);
    ArtFixtures.Convert(transform, pixels.Rgba, pixels.Channels, pixels.Precision);
    Check(Enumerable.Range(0, size).All(i => original[i * 4 + 3] == pixels.Rgba[i * 4 + 3]), $"Real ICC CMYK {depth}/{size} alpha preserved");
    payloads[$"Cmyk-{depth}-{size}"] = Convert.ToHexString(SHA256.HashData(pixels.Rgba));
}
foreach (var depth in new[] { 8, 16, 32 })
{
    // Legacy COLOR has an incorrect x64 stride. Keep the old process at four pixels;
    // never run a large tagged RGB/gray fixture against its undersized native buffer.
    var pixels = ArtFixtures.Pixels(3, depth, 4);
    using var transform = ArtFixtures.Open(ArtFixtures.MakeDocument(ArtFixtures.Profile(ArtFixtures.Srgb)), 3);
    ArtFixtures.Convert(transform, pixels.Rgba, pixels.Channels, pixels.Precision);
    payloads[$"RgbLegacyBug-{depth}"] = Convert.ToHexString(pixels.Rgba);
}
var invalid = ArtFixtures.Srgb; invalid[36] = 0;
Throws<InvalidDataException>(() => ArtFixtures.Open(ArtFixtures.MakeDocument(ArtFixtures.Profile(invalid)), 3), "Malformed embedded ICC rejected");
Throws<InvalidDataException>(() => ArtFixtures.Open(ArtFixtures.MakeDocument(ArtFixtures.Profile(ArtFixtures.Srgb)), 4), "ICC component mismatch rejected");
using (var transform = ArtFixtures.Open(ArtFixtures.MakeDocument(ArtFixtures.Profile(invalid), ArtFixtures.Profile(ArtFixtures.Cmyk)), 4))
{
    var pixels = ArtFixtures.Pixels(4, 8, 1); ArtFixtures.Convert(transform, pixels.Rgba, pixels.Channels, pixels.Precision);
    Check(true, "Last Photoshop image-resource profile wins");
    pixels.Channels.Remove(2);
    Throws<InvalidDataException>(() => ArtFixtures.Convert(transform, pixels.Rgba, pixels.Channels, pixels.Precision), "Missing CMYK channel rejected");
}
var doc = ArtFixtures.MakeDocument();
for (var i = 0; i < 200; i++) ArtFixtures.Open(doc, 3).Dispose();
var start = GC.GetAllocatedBytesForCurrentThread();
for (var i = 0; i < 1000; i++) ArtFixtures.Open(doc, 3).Dispose();
var allocation = GC.GetAllocatedBytesForCurrentThread() - start;
#if LEGACY
var stride = Marshal.SizeOf(ArtFixtures.Transform.GetNestedType("NativeColor", BindingFlags.NonPublic)!);
#else
var stride = Marshal.SizeOf(typeof(WindowsColorManagementHost).GetNestedType("NativeColor", BindingFlags.NonPublic)!);
HostTests.Run(Check, Throws<ArgumentException>, payloads, Path.Combine(Path.GetDirectoryName(root)!, "NativeOracle"));
Check(allocation < 512 * 1000, "Untagged open no longer creates eager native block buffers");
Check(WindowsColorManagementHost.ActiveTransforms == 0, "Every native color lease released");
#endif
File.WriteAllBytes(Path.Combine(root, "Rgb.psd"), ArtFixtures.Psd(ArtFixtures.Srgb));
File.WriteAllText(Path.Combine(root, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Count = checks.Count, Checks = checks, Payloads = payloads, NativeColorStride = stride, UntaggedOpenAllocatedBytes = allocation, OpenCount = 1000, Milliseconds = timer.Elapsed.TotalMilliseconds }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"ART_COLOR_OK|{checks.Count}|stride={stride}|open-allocation={allocation / 1000}");
