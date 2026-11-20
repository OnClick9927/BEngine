using System.Reflection;
using System.Buffers.Binary;
using ArtOwnershipTests;
using BEngine.Platform;
using Microsoft.Extensions.DependencyInjection;

static class HostTests
{
    internal static void Run(Action<bool, string> check, Action<Action, string> throwsArgument, SortedDictionary<string, string> payloads, string oracle)
    {
        check(ReferenceEquals(PlatformRuntime.GetService<IEditorColorManagementHost>(), WindowsColorManagementHost.Shared), "Real module initialization exposes the color host through the global import entry");
        var expectedRgb = File.ReadAllBytes(Path.Combine(oracle, "Rgb.bin"));
        var expectedDirect = File.ReadAllBytes(Path.Combine(oracle, "Direct.bin"));
        byte ExpectedByte(int pixel, int channel) => (byte)((BinaryPrimitives.ReadUInt16LittleEndian(expectedRgb.AsSpan(pixel * 6 + channel * 2)) + 128) / 257);
        var services = new ServiceCollection(); DotNetEditorPlatform.Configure(services);
        using var provider = services.BuildServiceProvider();
        check(ReferenceEquals(provider.GetRequiredService<IEditorColorManagementHost>(), WindowsColorManagementHost.Shared), "Windowless editor host registers Windows CMM");
        check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "BEngine.Platform.Desktop"), "Native color conversion does not load a window or GPU platform");
        foreach (var depth in new[] { 8, 16, 32 }) foreach (var size in new[] { 1, 2, 3, 255, 256, 257, 4097 })
        {
            var pixels = ArtFixtures.Pixels(3, depth, size); var before = pixels.Rgba.ToArray();
            using var lease = ArtFixtures.Open(ArtFixtures.MakeDocument(ArtFixtures.Profile(ArtFixtures.Srgb)), 3);
            ArtFixtures.Convert(lease, pixels.Rgba, pixels.Channels, pixels.Precision);
            check(Enumerable.Range(0, size).All(i => Enumerable.Range(0, 3).All(c => pixels.Rgba[i * 4 + c] == ExpectedByte(i, c))), $"Corrected ICC output matches independent Windows SDK C++ oracle {depth}/{size}");
            check(Enumerable.Range(0, size).All(i => before[i * 4 + 3] == pixels.Rgba[i * 4 + 3]), $"Tagged RGB alpha preserved {depth}/{size}");
        }
        var srgb = ArtFixtures.Srgb;
        using (var gray = WindowsColorManagementHost.Shared.OpenToSrgb(ArtFixtures.GrayProfile(), EditorColorSpace.Gray))
        {
            var input = Enumerable.Range(0, 257).Select(i => new EditorColorSample((ushort)(i * 65535 / 256))).ToArray();
            var output = new EditorColorSample[input.Length]; gray.Convert(input, output);
            var expected = File.ReadAllBytes(Path.Combine(oracle, "Gray.bin"));
            check(Enumerable.Range(0, input.Length).All(i => output[i].X == BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(i * 6)) && output[i].Y == BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(i * 6 + 2)) && output[i].Z == BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(i * 6 + 4))), "257 actual gray ICC samples match independent Windows SDK oracle");
            check(output[0].X < output[^1].X, "Actual gray ICC covers black-to-white range");
        }
        foreach (var depth in new[] { 8, 16, 32 }) foreach (var psb in new[] { false, true })
        {
            var bytes = ArtFixtures.DecodePixels(ArtFixtures.Psd(srgb, 257, depth, psb));
            var original = ArtFixtures.Pixels(3, depth, 257).Rgba;
            check(Enumerable.Range(0, 257).All(i => bytes[i * 4 + 3] == original[i * 4 + 3] && Enumerable.Range(0, 3).All(c => bytes[i * 4 + c] == ExpectedByte(i, c))), $"Actual {(psb ? "PSB" : "PSD")} layer parser matches native oracle with {depth}-bit colors and alpha");
        }
        using (var lease = WindowsColorManagementHost.Shared.OpenToSrgb(srgb, EditorColorSpace.Rgb))
        {
            var input = Enumerable.Range(0, 513).Select(i => new EditorColorSample((ushort)(i * 127), (ushort)(i * 89), (ushort)(i * 31))).ToArray();
            var output = Enumerable.Repeat(new EditorColorSample(1, 2, 3), 514).ToArray();
            lease.Convert(input, output);
            check(output[513] == new EditorColorSample(1, 2, 3), "Native CMM does not touch destination guard element");
            check(Enumerable.Range(0, input.Length).All(i => output[i].X == BinaryPrimitives.ReadUInt16LittleEndian(expectedDirect.AsSpan(i * 6)) && output[i].Y == BinaryPrimitives.ReadUInt16LittleEndian(expectedDirect.AsSpan(i * 6 + 2)) && output[i].Z == BinaryPrimitives.ReadUInt16LittleEndian(expectedDirect.AsSpan(i * 6 + 4))), "513 normalized 16-bit samples match independent Windows SDK oracle");
            lease.Convert(input, input);
            check(true, "Exact in-place normalized conversion allowed");
            throwsArgument(() => lease.Convert(input, new EditorColorSample[1]), "Short destination rejected");
            throwsArgument(() => lease.Convert(input.AsSpan(0, 100), input.AsSpan(1, 100)), "Partial overlap rejected before native access");
            lease.Convert([], []); check(true, "Empty conversion accepted");
            for (var i = 0; i < 100; i++) lease.Convert(input, output);
            var allocation = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) lease.Convert(input, output);
            allocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
            payloads["NativeConvertAllocatedBytes"] = allocation.ToString();
            check(allocation == 0, "1000 native ICC conversions allocate no managed memory after warm-up");
            lease.Dispose(); lease.Dispose();
            try { lease.Convert(input, output); throw new Exception("Disposed native color lease accepted"); } catch (ObjectDisposedException) { check(true, "Disposed native lease rejected"); }
        }
        var custom = new RecordingHost(); services = new ServiceCollection(); services.AddSingleton<IEditorColorManagementHost>(custom); DotNetEditorPlatform.Configure(services);
        using var customProvider = services.BuildServiceProvider();
        check(ReferenceEquals(customProvider.GetRequiredService<IEditorColorManagementHost>(), custom), "Explicit custom color host is preserved");
        PlatformRuntime.Initialize(customProvider);
        foreach (var mode in new[] { 1, 3, 4 })
        {
            var profile = mode == 4 ? ArtFixtures.Cmyk : ArtFixtures.Srgb;
            if (mode == 1) "GRAY"u8.CopyTo(profile.AsSpan(16));
            var pixels = ArtFixtures.Pixels(mode, 16, 257); var before = pixels.Rgba.ToArray();
            using (var lease = ArtFixtures.Open(ArtFixtures.MakeDocument(ArtFixtures.Profile(profile)), mode))
            {
                ArtFixtures.Convert(lease, pixels.Rgba, pixels.Channels, pixels.Precision);
                check(custom.Space == (mode == 1 ? EditorColorSpace.Gray : mode == 4 ? EditorColorSpace.Cmyk : EditorColorSpace.Rgb), "PSD policy chooses neutral component space " + mode);
                check(custom.ReadCount == 257, "PSD adapter forwards every normalized input sample " + mode);
                check(custom.First.X == (mode == 4 ? 65535 - 13 * 257 : 13 * 257), "PSD precision and CMYK inversion remain in importer " + mode);
                check(Enumerable.Range(0, 257).All(i => pixels.Rgba[i * 4] == 11 && pixels.Rgba[i * 4 + 1] == 22 && pixels.Rgba[i * 4 + 2] == 33 && pixels.Rgba[i * 4 + 3] == before[i * 4 + 3]), "PSD adapter consumes host output and preserves alpha " + mode);
            }
            check(custom.Disposed, "PSD lifetime releases host lease " + mode);
        }
        PlatformRuntime.Initialize(provider);
    }
    private sealed class RecordingHost : IEditorColorManagementHost, IEditorColorTransform
    {
        internal EditorColorSpace Space; internal EditorColorSample First; internal int ReadCount; internal bool Disposed;
        public IEditorColorTransform OpenToSrgb(ReadOnlyMemory<byte> profile, EditorColorSpace space) { Space = space; ReadCount = 0; Disposed = false; return this; }
        public void Convert(ReadOnlySpan<EditorColorSample> source, Span<EditorColorSample> destination)
        {
            if (ReadCount == 0 && source.Length != 0) First = source[0]; ReadCount += source.Length;
            for (var i = 0; i < source.Length; i++) destination[i] = new(11 * 257, 22 * 257, 33 * 257);
        }
        public void Dispose() => Disposed = true;
    }
}
